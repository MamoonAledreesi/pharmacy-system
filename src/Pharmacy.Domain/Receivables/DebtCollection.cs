using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Receivables;

/// <summary>
/// Cash collected from a customer against what they owe, received into the open shift. A collection entered by
/// mistake is not deleted: it is reversed (once), which takes the cash back out of the drawer and puts the amount
/// back on the customer balance.
/// </summary>
public sealed class DebtCollection
{
    public const int NumberMaxLength = 30;

    // Used by EF Core.
    private DebtCollection()
    {
    }

    public int Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public DateTime CollectedAtUtc { get; private set; }

    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public Guid OperationId { get; private set; }

    public bool IsReversed { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }

    public string? ReversedByUserName { get; private set; }

    public string? ReverseReason { get; private set; }

    public Guid? ReverseOperationId { get; private set; }

    public static Result<DebtCollection> Create(
        string number, int customerId, decimal amount, string? note, Guid operationId, DateTime collectedAtUtc, int? userId, string userName)
    {
        if (!Customer.IsValidAmount(amount))
        {
            return Result.Failure<DebtCollection>(new Error(ReceivablesErrors.InvalidAmount));
        }

        var cleanNote = note?.Trim();
        if (cleanNote?.Length > CustomerAccountEntry.NoteMaxLength)
        {
            return Result.Failure<DebtCollection>(new Error(ReceivablesErrors.NoteTooLong, CustomerAccountEntry.NoteMaxLength));
        }

        return Result.Success(new DebtCollection
        {
            Number = number,
            CustomerId = customerId,
            Amount = amount,
            CollectedAtUtc = collectedAtUtc,
            UserId = userId,
            UserName = Truncate(userName),
            Note = string.IsNullOrEmpty(cleanNote) ? null : cleanNote,
            OperationId = operationId,
        });
    }

    public Result Reverse(string? reason, Guid operationId, DateTime reversedAtUtc, string userName)
    {
        var cleanReason = reason?.Trim();
        if (string.IsNullOrEmpty(cleanReason))
        {
            return Result.Failure(new Error(ReceivablesErrors.ReasonRequired));
        }

        if (cleanReason.Length > CustomerAccountEntry.NoteMaxLength)
        {
            return Result.Failure(new Error(ReceivablesErrors.NoteTooLong, CustomerAccountEntry.NoteMaxLength));
        }

        if (IsReversed)
        {
            return Result.Failure(new Error(ReceivablesErrors.CollectionAlreadyReversed));
        }

        IsReversed = true;
        ReversedAtUtc = reversedAtUtc;
        ReversedByUserName = Truncate(userName);
        ReverseReason = cleanReason;
        ReverseOperationId = operationId;
        return Result.Success();
    }

    private static string Truncate(string userName) =>
        userName.Length <= CustomerAccountEntry.UserNameMaxLength ? userName : userName[..CustomerAccountEntry.UserNameMaxLength];
}
