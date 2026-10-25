namespace Pharmacy.Domain.Inventory;

/// <summary>Who did it, when, and why; supplied by the caller for every stock change.</summary>
public sealed record MovementContext(
    DateTime OccurredAtUtc,
    int? UserId,
    string UserName,
    string? ReasonCode = null,
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// One entry of the append-only stock ledger. A batch's balances always equal the sum of its movements;
/// entries are never updated or deleted, mistakes are corrected with a new opposite movement.
/// </summary>
public sealed class StockMovement
{
    public const int UserNameMaxLength = 256;
    public const int ReasonCodeMaxLength = 30;
    public const int ReferenceTypeMaxLength = 30;

    // Used by EF Core.
    private StockMovement()
    {
    }

    internal StockMovement(
        Batch batch, StockMovementType type, int availableDelta, int quarantineDelta, MovementContext context)
    {
        BatchId = batch.Id;
        Batch = batch;
        ProductId = batch.ProductId;
        Type = type;
        AvailableDelta = availableDelta;
        QuarantineDelta = quarantineDelta;
        AvailableAfter = batch.QuantityAvailable;
        QuarantineAfter = batch.QuantityQuarantined;
        UnitCost = batch.UnitCost;
        OccurredAtUtc = context.OccurredAtUtc;
        UserId = context.UserId;
        UserName = context.UserName.Length <= UserNameMaxLength ? context.UserName : context.UserName[..UserNameMaxLength];
        ReasonCode = context.ReasonCode;
        Note = context.Note;
        ReferenceType = context.ReferenceType;
        ReferenceId = context.ReferenceId;
        OperationId = context.OperationId;
    }

    public long Id { get; private set; }

    public int BatchId { get; private set; }

    /// <summary>The batch, set when the movement is created in memory; not loaded by queries.</summary>
    public Batch? Batch { get; private set; }

    public int ProductId { get; private set; }

    public StockMovementType Type { get; private set; }

    /// <summary>Change in sellable quantity, in base units.</summary>
    public int AvailableDelta { get; private set; }

    /// <summary>Change in quarantined quantity, in base units.</summary>
    public int QuarantineDelta { get; private set; }

    /// <summary>The batch balances right after this movement (running balance for the history view).</summary>
    public int AvailableAfter { get; private set; }

    public int QuarantineAfter { get; private set; }

    /// <summary>Cost per base unit at the time (copied from the batch, which never changes its cost).</summary>
    public decimal UnitCost { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? ReasonCode { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The document that caused the movement (for example "SalesInvoice"), when there is one.</summary>
    public string? ReferenceType { get; private set; }

    public long? ReferenceId { get; private set; }

    /// <summary>
    /// Identifies the user operation that produced the movement. A repeated submission of the same
    /// operation (double click, retry) is recognized by it and not applied twice.
    /// </summary>
    public Guid? OperationId { get; private set; }
}
