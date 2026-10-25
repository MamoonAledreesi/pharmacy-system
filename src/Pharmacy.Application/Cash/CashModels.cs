using Pharmacy.Domain.Cash;

namespace Pharmacy.Application.Cash;

/// <summary>
/// A shift with its totals. <c>TotalIn</c> and <c>TotalOut</c> are the cash that came in and went out (both positive);
/// <c>CurrentBalance</c> is the expected cash in the drawer. <c>Counted</c> and <c>Difference</c> exist once it is closed.
/// <c>IsStale</c> marks an open shift that has been open for more than a day, most likely forgotten.
/// </summary>
public sealed record ShiftSummary(
    int Id,
    bool IsOpen,
    DateTime OpenedAtUtc,
    string OpenedBy,
    decimal OpeningBalance,
    decimal PreviousCounted,
    string? OpeningNote,
    decimal CurrentBalance,
    decimal TotalIn,
    decimal TotalOut,
    DateTime? ClosedAtUtc,
    string? ClosedBy,
    decimal? Counted,
    decimal? Difference,
    string? CloseNote,
    bool WasForceClosed,
    bool IsStale);

/// <summary>What the cash register page needs: the open shift (if any) and the opening balance to suggest.</summary>
public sealed record CashRegisterState(ShiftSummary? Current, decimal SuggestedOpeningBalance);

public sealed record ShiftQuery(int PageIndex, int PageSize);

public sealed record CashMovementQuery(int ShiftId, CashMovementType? Type, int PageIndex, int PageSize);

public sealed record CashMovementRow(
    long Id,
    DateTime OccurredAtUtc,
    CashMovementType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Note,
    string UserName,
    string? ReferenceType,
    long? ReferenceId);

public sealed record DailyTypeTotal(CashMovementType Type, decimal In, decimal Out, int Count);

/// <summary>
/// One local calendar day of cash. Movements belong to the day they happened on, even when their shift crossed
/// midnight; <c>Shifts</c> lists every shift that opened, closed, or had a movement that day.
/// </summary>
public sealed record DailySummary(
    DateOnly Date,
    decimal TotalIn,
    decimal TotalOut,
    IReadOnlyList<DailyTypeTotal> ByType,
    IReadOnlyList<ShiftSummary> Shifts)
{
    public decimal Net => TotalIn - TotalOut;
}
