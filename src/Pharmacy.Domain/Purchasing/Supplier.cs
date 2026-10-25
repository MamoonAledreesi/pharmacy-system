using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Purchasing;

/// <summary>
/// A company the pharmacy buys from, with the running balance it owes them. The balance only changes through
/// <see cref="Increase"/> and <see cref="Decrease"/>, each of which returns the statement entry to store, so the
/// balance and the statement cannot drift apart.
/// </summary>
public sealed class Supplier
{
    public const int NameMaxLength = 150;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 300;
    public const int NotesMaxLength = 500;
    public const decimal MaxAmount = 1_000_000_000m;
    public const int Decimals = 2;

    // Used by EF Core.
    private Supplier()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>What the pharmacy owes this supplier. Never negative.</summary>
    public decimal CurrentBalance { get; private set; }

    /// <summary>Normalized name and phone (see <see cref="SearchNormalizer"/>), maintained on every change.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = [];

    public static bool IsValidAmount(decimal amount) =>
        amount is > 0 and <= MaxAmount && decimal.Round(amount, Decimals) == amount;

    public static Result<Supplier> Create(string? name, string? phone, string? address, string? notes)
    {
        var supplier = new Supplier();
        var applied = supplier.Update(name, phone, address, notes, isActive: true);
        return applied.IsFailure ? Result.Failure<Supplier>(applied.Error!) : Result.Success(supplier);
    }

    public Result Update(string? name, string? phone, string? address, string? notes, bool isActive)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        if (cleanName.Length == 0)
        {
            return Result.Failure(new Error(PurchasingErrors.SupplierNameRequired));
        }

        if (cleanName.Length > NameMaxLength)
        {
            return Result.Failure(new Error(PurchasingErrors.SupplierNameTooLong, NameMaxLength));
        }

        var cleanPhone = Clean(phone);
        var cleanAddress = Clean(address);
        var cleanNotes = Clean(notes);
        if (cleanPhone?.Length > PhoneMaxLength || cleanAddress?.Length > AddressMaxLength || cleanNotes?.Length > NotesMaxLength)
        {
            return Result.Failure(new Error(PurchasingErrors.FieldTooLong));
        }

        Name = cleanName;
        Phone = cleanPhone;
        Address = cleanAddress;
        Notes = cleanNotes;
        IsActive = isActive;
        SearchText = SearchNormalizer.Normalize($"{cleanName} {cleanPhone}");
        return Result.Success();
    }

    /// <summary>The pharmacy owes more: an opening balance, a credit purchase, or a reversed payment.</summary>
    public Result<SupplierAccountEntry> Increase(SupplierEntryType type, decimal amount, AccountContext context)
    {
        if (type is not (SupplierEntryType.OpeningBalance or SupplierEntryType.Invoice or SupplierEntryType.PaymentReversal))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not an entry that increases the balance.");
        }

        return Apply(type, amount, signedAmount: amount, context);
    }

    /// <summary>The pharmacy paid: the balance goes down, and never below zero.</summary>
    public Result<SupplierAccountEntry> Decrease(SupplierEntryType type, decimal amount, AccountContext context)
    {
        if (type != SupplierEntryType.Payment)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not an entry that decreases the balance.");
        }

        return Apply(type, amount, signedAmount: -amount, context);
    }

    private Result<SupplierAccountEntry> Apply(SupplierEntryType type, decimal amount, decimal signedAmount, AccountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsValidAmount(amount))
        {
            return Result.Failure<SupplierAccountEntry>(new Error(PurchasingErrors.InvalidAmount));
        }

        var balance = CurrentBalance + signedAmount;
        if (balance < 0)
        {
            return Result.Failure<SupplierAccountEntry>(new Error(PurchasingErrors.PaymentExceedsBalance, CurrentBalance));
        }

        if (balance > MaxAmount * 10)
        {
            return Result.Failure<SupplierAccountEntry>(new Error(PurchasingErrors.TotalTooLarge, MaxAmount));
        }

        CurrentBalance = balance;
        return Result.Success(new SupplierAccountEntry(this, type, signedAmount, context));
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
