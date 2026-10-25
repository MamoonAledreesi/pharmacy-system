using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Cash;

/// <summary>Shifts, movements, and daily summaries. Requires Cash.View.</summary>
public sealed class CashQueryService(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxPageSize = 100;

    /// <summary>An open shift older than this is probably forgotten.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<CashRegisterState>> GetRegisterStateAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CashView))
        {
            return Result.Failure<CashRegisterState>(_forbidden);
        }

        var current = await Summaries(db.CashShifts.AsNoTracking().Where(s => s.IsOpen)).SingleOrDefaultAsync(cancellationToken);
        var suggested = await db.CashShifts.AsNoTracking()
            .Where(s => !s.IsOpen)
            .OrderByDescending(s => s.Id)
            .Select(s => s.CountedBalance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        return Result.Success(new CashRegisterState(current, suggested));
    }

    public async Task<Result<ShiftSummary>> GetShiftAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CashView))
        {
            return Result.Failure<ShiftSummary>(_forbidden);
        }

        var shift = await Summaries(db.CashShifts.AsNoTracking().Where(s => s.Id == shiftId)).SingleOrDefaultAsync(cancellationToken);
        return shift is null ? Result.Failure<ShiftSummary>(new Error(CommonErrors.NotFound)) : Result.Success(shift);
    }

    public async Task<Result<PagedResult<ShiftSummary>>> SearchShiftsAsync(ShiftQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.CashView))
        {
            return Result.Failure<PagedResult<ShiftSummary>>(_forbidden);
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await db.CashShifts.CountAsync(cancellationToken);
        var items = await Summaries(db.CashShifts.AsNoTracking()
                .OrderByDescending(s => s.Id)
                .Skip(Math.Max(query.PageIndex, 0) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<ShiftSummary>(items, total));
    }

    public async Task<Result<PagedResult<CashMovementRow>>> SearchMovementsAsync(
        CashMovementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.CashView))
        {
            return Result.Failure<PagedResult<CashMovementRow>>(_forbidden);
        }

        var movements = db.CashMovements.AsNoTracking().Where(m => m.ShiftId == query.ShiftId);
        if (query.Type is { } type)
        {
            movements = movements.Where(m => m.Type == type);
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await movements.CountAsync(cancellationToken);
        var items = await movements
            .OrderByDescending(m => m.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(m => new CashMovementRow(
                m.Id, m.OccurredAtUtc, m.Type, m.Amount, m.BalanceAfter, m.Note, m.UserName, m.ReferenceType, m.ReferenceId))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<CashMovementRow>(items, total));
    }

    /// <summary>The cash of one local calendar day, across all shifts.</summary>
    public async Task<Result<DailySummary>> GetDailySummaryAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CashView))
        {
            return Result.Failure<DailySummary>(_forbidden);
        }

        var (start, end) = LocalDayRangeUtc(date);
        var movements = db.CashMovements.AsNoTracking().Where(m => m.OccurredAtUtc >= start && m.OccurredAtUtc < end);

        var byType = await movements
            .GroupBy(m => m.Type)
            .Select(g => new DailyTypeTotal(
                g.Key,
                g.Sum(m => m.Amount > 0 ? m.Amount : 0m),
                g.Sum(m => m.Amount < 0 ? -m.Amount : 0m),
                g.Count()))
            .ToListAsync(cancellationToken);

        var shifts = await Summaries(db.CashShifts.AsNoTracking()
                .Where(s => (s.OpenedAtUtc >= start && s.OpenedAtUtc < end)
                    || (s.ClosedAtUtc >= start && s.ClosedAtUtc < end)
                    || db.CashMovements.Any(m => m.ShiftId == s.Id && m.OccurredAtUtc >= start && m.OccurredAtUtc < end))
                .OrderBy(s => s.Id))
            .ToListAsync(cancellationToken);

        return Result.Success(new DailySummary(
            date,
            byType.Sum(t => t.In),
            byType.Sum(t => t.Out),
            [.. byType.OrderBy(t => t.Type)],
            shifts));
    }

    /// <summary>The start (inclusive) and end (exclusive) of a local calendar day, as UTC instants.</summary>
    public (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly date)
    {
        var zone = clock.LocalTimeZone;
        return (ToUtc(date, zone), ToUtc(date.AddDays(1), zone));

        static DateTime ToUtc(DateOnly day, TimeZoneInfo zone)
        {
            var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

            // A zone that skips midnight (daylight saving starts then) has no such local time; use the first valid hour.
            if (zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
            }

            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
    }

    private IQueryable<ShiftSummary> Summaries(IQueryable<Domain.Cash.CashShift> shifts)
    {
        var staleCutoff = clock.GetUtcNow().UtcDateTime - StaleAfter;

        return shifts.Select(s => new ShiftSummary(
            s.Id,
            s.IsOpen,
            s.OpenedAtUtc,
            s.OpenedByUserName,
            s.OpeningBalance,
            s.PreviousCountedBalance,
            s.OpeningNote,
            s.CurrentBalance,
            db.CashMovements.Where(m => m.ShiftId == s.Id && m.Amount > 0).Sum(m => (decimal?)m.Amount) ?? 0m,
            -(db.CashMovements.Where(m => m.ShiftId == s.Id && m.Amount < 0).Sum(m => (decimal?)m.Amount) ?? 0m),
            s.ClosedAtUtc,
            s.ClosedByUserName,
            s.CountedBalance,
            s.Difference,
            s.CloseNote,
            s.WasForceClosed,
            s.IsOpen && s.OpenedAtUtc < staleCutoff));
    }
}
