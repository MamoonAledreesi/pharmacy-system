namespace Pharmacy.Domain.Receivables;

public enum CustomerEntryType
{
    /// <summary>What the customer already owed when the system started.</summary>
    OpeningBalance = 0,

    /// <summary>The credit part of a sales invoice.</summary>
    Sale = 1,

    /// <summary>Cash collected from the customer against what they owe.</summary>
    Collection = 2,

    /// <summary>A collection that was entered by mistake and taken back.</summary>
    CollectionReversal = 3,
}

/// <summary>Who did it, when, and why; supplied by the caller for every customer account entry.</summary>
public sealed record CustomerAccountContext(
    DateTime OccurredAtUtc,
    int? UserId,
    string UserName,
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// One entry of the append-only customer statement. What a customer owes equals the sum of their entries;
/// entries are never updated or deleted, mistakes are corrected with an opposite entry.
/// </summary>
public sealed class CustomerAccountEntry
{
    public const int UserNameMaxLength = 256;
    public const int NoteMaxLength = 300;
    public const int ReferenceTypeMaxLength = 30;

    // Used by EF Core.
    private CustomerAccountEntry()
    {
    }

    internal CustomerAccountEntry(Customer customer, CustomerEntryType type, decimal signedAmount, CustomerAccountContext context)
    {
        CustomerId = customer.Id;
        Customer = customer;
        Type = type;
        Amount = signedAmount;
        BalanceAfter = customer.CurrentBalance;
        OccurredAtUtc = context.OccurredAtUtc;
        UserId = context.UserId;
        UserName = context.UserName.Length <= UserNameMaxLength ? context.UserName : context.UserName[..UserNameMaxLength];
        Note = context.Note;
        ReferenceType = context.ReferenceType;
        ReferenceId = context.ReferenceId;
        OperationId = context.OperationId;
    }

    public long Id { get; private set; }

    public int CustomerId { get; private set; }

    /// <summary>The customer, set when the entry is created in memory; not loaded by queries.</summary>
    public Customer? Customer { get; private set; }

    public CustomerEntryType Type { get; private set; }

    /// <summary>Positive when the customer owes more, negative when they owe less.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What the customer owes right after this entry (running balance for the statement).</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    /// <summary>The document that caused the entry ("DebtCollection" now; sales invoices later).</summary>
    public string? ReferenceType { get; private set; }

    public long? ReferenceId { get; private set; }

    public Guid? OperationId { get; private set; }
}
