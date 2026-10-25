using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Cash;

/// <summary>
/// A period of work on the pharmacy's cash drawer, from opening it with a starting balance to counting the
/// cash and closing it. At most one shift is open at a time. While it is open every cash movement is recorded
/// on it; once closed it never changes. Cash differences are recorded, never hidden.
/// </summary>
public sealed class CashShift
{
    public const decimal MaxAmount = 1_000_000_000m;
    public const int Decimals = 2;

    // Used by EF Core.
    private CashShift()
    {
    }

    public int Id { get; private set; }

    public bool IsOpen { get; private set; }

    public DateTime OpenedAtUtc { get; private set; }

    public int? OpenedByUserId { get; private set; }

    public string OpenedByUserName { get; private set; } = string.Empty;

    public decimal OpeningBalance { get; private set; }

    /// <summary>What the previous shift counted when it closed (0 for the first shift), to show any gap at opening.</summary>
    public decimal PreviousCountedBalance { get; private set; }

    public string? OpeningNote { get; private set; }

    /// <summary>Identifies the "open shift" operation, so a repeated submission is not applied twice.</summary>
    public Guid? OpenOperationId { get; private set; }

    /// <summary>The drawer's expected cash right now: opening balance plus all movements so far.</summary>
    public decimal CurrentBalance { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public int? ClosedByUserId { get; private set; }

    public string? ClosedByUserName { get; private set; }

    /// <summary>What was physically counted at closing.</summary>
    public decimal? CountedBalance { get; private set; }

    /// <summary>Counted minus expected: negative is a shortage, positive an overage. Null while open.</summary>
    public decimal? Difference { get; private set; }

    public string? CloseNote { get; private set; }

    /// <summary>True when a manager closed a shift that someone else opened.</summary>
    public bool WasForceClosed { get; private set; }

    public Guid? CloseOperationId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static bool IsValidAmount(decimal amount) =>
        amount is > 0 and <= MaxAmount && decimal.Round(amount, Decimals) == amount;

    public static Result<CashShift> Open(
        DateTime openedAtUtc, int? userId, string userName, decimal openingBalance, decimal previousCountedBalance,
        string? note, Guid? operationId)
    {
        if (openingBalance is < 0 or > MaxAmount || decimal.Round(openingBalance, Decimals) != openingBalance)
        {
            return Result.Failure<CashShift>(new Error(CashErrors.InvalidAmount));
        }

        var cleanNote = CleanNote(note);
        if (cleanNote?.Length > CashMovement.NoteMaxLength)
        {
            return Result.Failure<CashShift>(new Error(CashErrors.NoteTooLong, CashMovement.NoteMaxLength));
        }

        if (openingBalance != previousCountedBalance && cleanNote is null)
        {
            return Result.Failure<CashShift>(new Error(CashErrors.OpeningDifferenceNoteRequired));
        }

        return Result.Success(new CashShift
        {
            IsOpen = true,
            OpenedAtUtc = openedAtUtc,
            OpenedByUserId = userId,
            OpenedByUserName = userName,
            OpeningBalance = openingBalance,
            PreviousCountedBalance = previousCountedBalance,
            OpeningNote = cleanNote,
            OpenOperationId = operationId,
            CurrentBalance = openingBalance,
        });
    }

    /// <summary>Puts cash into the drawer.</summary>
    public Result<CashMovement> Receive(CashMovementType type, decimal amount, CashContext context)
    {
        if (!CashMovementTypes.Receipts.Contains(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a cash receipt type.");
        }

        return Apply(type, amount, signedAmount: amount, context);
    }

    /// <summary>Takes cash out of the drawer. The drawer can never hold less than nothing.</summary>
    public Result<CashMovement> Pay(CashMovementType type, decimal amount, CashContext context)
    {
        if (!CashMovementTypes.Payments.Contains(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a cash payment type.");
        }

        return Apply(type, amount, signedAmount: -amount, context);
    }

    /// <summary>
    /// Closes the shift with the cash that was counted. A difference from the expected cash needs a note; closing a
    /// shift that someone else opened (<paramref name="forced"/>) always needs a reason.
    /// </summary>
    public Result Close(DateTime closedAtUtc, int? userId, string userName, decimal countedBalance, string? note, bool forced, Guid? operationId)
    {
        if (!IsOpen)
        {
            return Result.Failure(new Error(CashErrors.ShiftClosed));
        }

        if (countedBalance is < 0 or > MaxAmount || decimal.Round(countedBalance, Decimals) != countedBalance)
        {
            return Result.Failure(new Error(CashErrors.InvalidAmount));
        }

        var cleanNote = CleanNote(note);
        if (cleanNote?.Length > CashMovement.NoteMaxLength)
        {
            return Result.Failure(new Error(CashErrors.NoteTooLong, CashMovement.NoteMaxLength));
        }

        if (forced && cleanNote is null)
        {
            return Result.Failure(new Error(CashErrors.ForceCloseReasonRequired));
        }

        var difference = countedBalance - CurrentBalance;
        if (difference != 0 && cleanNote is null)
        {
            return Result.Failure(new Error(CashErrors.CloseDifferenceNoteRequired));
        }

        IsOpen = false;
        ClosedAtUtc = closedAtUtc;
        ClosedByUserId = userId;
        ClosedByUserName = userName;
        CountedBalance = countedBalance;
        Difference = difference;
        CloseNote = cleanNote;
        WasForceClosed = forced;
        CloseOperationId = operationId;
        return Result.Success();
    }

    private Result<CashMovement> Apply(CashMovementType type, decimal amount, decimal signedAmount, CashContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsOpen)
        {
            return Result.Failure<CashMovement>(new Error(CashErrors.ShiftClosed));
        }

        if (amount <= 0 || decimal.Round(amount, Decimals) != amount)
        {
            return Result.Failure<CashMovement>(new Error(CashErrors.InvalidAmount));
        }

        if (amount > MaxAmount)
        {
            return Result.Failure<CashMovement>(new Error(CashErrors.AmountTooLarge, MaxAmount));
        }

        var balance = CurrentBalance + signedAmount;
        if (balance < 0)
        {
            return Result.Failure<CashMovement>(new Error(CashErrors.InsufficientCash, CurrentBalance));
        }

        if (balance > MaxAmount * 10)
        {
            return Result.Failure<CashMovement>(new Error(CashErrors.AmountTooLarge, MaxAmount));
        }

        CurrentBalance = balance;
        return Result.Success(new CashMovement(this, type, signedAmount, context));
    }

    private static string? CleanNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
