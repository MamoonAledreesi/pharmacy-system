namespace Pharmacy.Domain.Cash;

/// <summary>Who did it, when, and why; supplied by the caller for every cash movement.</summary>
public sealed record CashContext(
    DateTime OccurredAtUtc,
    int? UserId,
    string UserName,
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// One entry of the append-only cash ledger. The drawer's expected cash equals the shift's opening balance plus
/// the sum of its movements; entries are never updated or deleted, mistakes are corrected with an opposite movement.
/// </summary>
public sealed class CashMovement
{
    public const int UserNameMaxLength = 256;
    public const int NoteMaxLength = 300;
    public const int ReferenceTypeMaxLength = 30;

    // Used by EF Core.
    private CashMovement()
    {
    }

    internal CashMovement(CashShift shift, CashMovementType type, decimal signedAmount, CashContext context)
    {
        ShiftId = shift.Id;
        Shift = shift;
        Type = type;
        Amount = signedAmount;
        BalanceAfter = shift.CurrentBalance;
        OccurredAtUtc = context.OccurredAtUtc;
        UserId = context.UserId;
        UserName = context.UserName.Length <= UserNameMaxLength ? context.UserName : context.UserName[..UserNameMaxLength];
        Note = context.Note;
        ReferenceType = context.ReferenceType;
        ReferenceId = context.ReferenceId;
        OperationId = context.OperationId;
    }

    public long Id { get; private set; }

    public int ShiftId { get; private set; }

    /// <summary>The shift, set when the movement is created in memory; not loaded by queries.</summary>
    public CashShift? Shift { get; private set; }

    public CashMovementType Type { get; private set; }

    /// <summary>Positive when cash comes in, negative when it goes out.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The drawer's expected cash right after this movement (running balance for the statement).</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    /// <summary>The document that caused the movement (for example "SalesInvoice"), when there is one.</summary>
    public string? ReferenceType { get; private set; }

    public long? ReferenceId { get; private set; }

    /// <summary>A repeated submission of the same operation (double click, retry) is recognized by it and not applied twice.</summary>
    public Guid? OperationId { get; private set; }
}
