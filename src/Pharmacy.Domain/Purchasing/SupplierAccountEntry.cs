namespace Pharmacy.Domain.Purchasing;

public enum SupplierEntryType
{
    /// <summary>What the pharmacy already owed the supplier when the system started.</summary>
    OpeningBalance = 0,

    /// <summary>The credit part of a purchase invoice.</summary>
    Invoice = 1,

    /// <summary>Cash paid to the supplier.</summary>
    Payment = 2,

    /// <summary>A payment that was entered by mistake and taken back.</summary>
    PaymentReversal = 3,

    /// <summary>Goods sent back to the supplier, taken off what the pharmacy owes.</summary>
    PurchaseReturn = 4,
}

/// <summary>Who did it, when, and why; supplied by the caller for every supplier account entry.</summary>
public sealed record AccountContext(
    DateTime OccurredAtUtc,
    int? UserId,
    string UserName,
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// One entry of the append-only supplier statement. The amount the pharmacy owes a supplier equals the sum of
/// its entries; entries are never updated or deleted, mistakes are corrected with an opposite entry.
/// </summary>
public sealed class SupplierAccountEntry
{
    public const int UserNameMaxLength = 256;
    public const int NoteMaxLength = 300;
    public const int ReferenceTypeMaxLength = 30;

    // Used by EF Core.
    private SupplierAccountEntry()
    {
    }

    internal SupplierAccountEntry(Supplier supplier, SupplierEntryType type, decimal signedAmount, AccountContext context)
    {
        SupplierId = supplier.Id;
        Supplier = supplier;
        Type = type;
        Amount = signedAmount;
        BalanceAfter = supplier.CurrentBalance;
        OccurredAtUtc = context.OccurredAtUtc;
        UserId = context.UserId;
        UserName = context.UserName.Length <= UserNameMaxLength ? context.UserName : context.UserName[..UserNameMaxLength];
        Note = context.Note;
        ReferenceType = context.ReferenceType;
        ReferenceId = context.ReferenceId;
        OperationId = context.OperationId;
    }

    public long Id { get; private set; }

    public int SupplierId { get; private set; }

    /// <summary>The supplier, set when the entry is created in memory; not loaded by queries.</summary>
    public Supplier? Supplier { get; private set; }

    public SupplierEntryType Type { get; private set; }

    /// <summary>Positive when the pharmacy owes more, negative when it owes less.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What the pharmacy owes the supplier right after this entry (running balance for the statement).</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    /// <summary>The document that caused the entry ("PurchaseInvoice" or "SupplierPayment").</summary>
    public string? ReferenceType { get; private set; }

    public long? ReferenceId { get; private set; }

    public Guid? OperationId { get; private set; }
}
