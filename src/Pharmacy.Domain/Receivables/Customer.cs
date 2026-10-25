using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Receivables;

/// <summary>
/// A person the pharmacy sells to, with the running balance they owe. The balance only changes through
/// <see cref="Debit"/> and <see cref="Credit"/>, each of which returns the statement entry to store, so the
/// balance and the statement cannot drift apart. Debt never blocks a sale and there is no credit limit.
/// <para>
/// The default cash customer (<see cref="IsDefaultCash"/>) is an ordinary customer record that sales select when
/// nobody is named; it can be renamed and edited like any other, but never deactivated.
/// </para>
/// </summary>
public sealed class Customer
{
    public const int NameMaxLength = 150;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 300;
    public const int NotesMaxLength = 500;
    public const decimal MaxAmount = 1_000_000_000m;
    public const int Decimals = 2;

    // Used by EF Core.
    private Customer()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>The customer that sales use when no one is named. There is exactly one.</summary>
    public bool IsDefaultCash { get; private set; }

    /// <summary>What this customer owes the pharmacy. Never negative.</summary>
    public decimal CurrentBalance { get; private set; }

    /// <summary>Normalized name and phone (see <see cref="SearchNormalizer"/>), maintained on every change.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public byte[] RowVersion { get; private set; } = [];

    public static bool IsValidAmount(decimal amount) =>
        amount is > 0 and <= MaxAmount && decimal.Round(amount, Decimals) == amount;

    public static string SearchTextOf(string name, string? phone) => SearchNormalizer.Normalize($"{name} {phone}");

    public static Result<Customer> Create(string? name, string? phone, string? address, string? notes)
    {
        var customer = new Customer();
        var applied = customer.Update(name, phone, address, notes, isActive: true);
        return applied.IsFailure ? Result.Failure<Customer>(applied.Error!) : Result.Success(customer);
    }

    public Result Update(string? name, string? phone, string? address, string? notes, bool isActive)
    {
        var cleanName = name?.Trim() ?? string.Empty;
        if (cleanName.Length == 0)
        {
            return Result.Failure(new Error(ReceivablesErrors.CustomerNameRequired));
        }

        if (cleanName.Length > NameMaxLength)
        {
            return Result.Failure(new Error(ReceivablesErrors.CustomerNameTooLong, NameMaxLength));
        }

        var cleanPhone = Clean(phone);
        var cleanAddress = Clean(address);
        var cleanNotes = Clean(notes);
        if (cleanPhone?.Length > PhoneMaxLength || cleanAddress?.Length > AddressMaxLength || cleanNotes?.Length > NotesMaxLength)
        {
            return Result.Failure(new Error(ReceivablesErrors.FieldTooLong));
        }

        if (IsDefaultCash && !isActive)
        {
            return Result.Failure(new Error(ReceivablesErrors.DefaultCustomerCannotBeDeactivated));
        }

        Name = cleanName;
        Phone = cleanPhone;
        Address = cleanAddress;
        Notes = cleanNotes;
        IsActive = isActive;
        SearchText = SearchTextOf(cleanName, cleanPhone);
        return Result.Success();
    }

    /// <summary>The customer owes more: an opening balance, a credit sale, or a reversed collection.</summary>
    public Result<CustomerAccountEntry> Debit(CustomerEntryType type, decimal amount, CustomerAccountContext context)
    {
        if (type is not (CustomerEntryType.OpeningBalance or CustomerEntryType.Sale or CustomerEntryType.CollectionReversal))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not an entry that increases the balance.");
        }

        return Apply(type, amount, signedAmount: amount, context);
    }

    /// <summary>The customer paid: the balance goes down, and never below zero.</summary>
    public Result<CustomerAccountEntry> Credit(CustomerEntryType type, decimal amount, CustomerAccountContext context)
    {
        if (type != CustomerEntryType.Collection)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not an entry that decreases the balance.");
        }

        return Apply(type, amount, signedAmount: -amount, context);
    }

    private Result<CustomerAccountEntry> Apply(CustomerEntryType type, decimal amount, decimal signedAmount, CustomerAccountContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsValidAmount(amount))
        {
            return Result.Failure<CustomerAccountEntry>(new Error(ReceivablesErrors.InvalidAmount));
        }

        var balance = CurrentBalance + signedAmount;
        if (balance < 0)
        {
            return Result.Failure<CustomerAccountEntry>(new Error(ReceivablesErrors.CollectionExceedsBalance, CurrentBalance));
        }

        if (balance > MaxAmount * 10)
        {
            return Result.Failure<CustomerAccountEntry>(new Error(ReceivablesErrors.AmountTooLarge, MaxAmount));
        }

        CurrentBalance = balance;
        return Result.Success(new CustomerAccountEntry(this, type, signedAmount, context));
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
