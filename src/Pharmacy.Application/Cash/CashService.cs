using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Cash;

/// <summary>
/// Opening a shift. <c>OperationId</c> is generated once per form: if the same submission arrives twice
/// (double click, retry) the second one changes nothing and reports the first result.
/// </summary>
public sealed record OpenShiftRequest(Guid OperationId, decimal OpeningBalance, string? Note);

/// <summary>Closing a shift with the cash that was counted. <c>OperationId</c> works as for <see cref="OpenShiftRequest"/>.</summary>
public sealed record CloseShiftRequest(Guid OperationId, int ShiftId, decimal CountedBalance, string? Note);

public enum CashDirection
{
    In = 0,
    Out = 1,
}

/// <summary>A manual cash movement; the note saying why is mandatory.</summary>
public sealed record ManualCashRequest(Guid OperationId, CashDirection Direction, decimal Amount, string? Note);

/// <summary>Cash register operations done from the screens. Each runs in one transaction with its audit entry.</summary>
public sealed class CashService(
    IPharmacyDbContext db, CashLedger ledger, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider clock)
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Opens the shift. Fails when one is already open; returns the new shift id.</summary>
    public async Task<Result<int>> OpenShiftAsync(OpenShiftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.CashOpenShift))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var open = await db.CashShifts.AsNoTracking()
                .Where(s => s.IsOpen)
                .Select(s => new { s.Id, s.OpenOperationId })
                .SingleOrDefaultAsync(cancellationToken);
            if (open is not null)
            {
                // The same form submitted twice is not an error: report the shift the first submission opened.
                return open.OpenOperationId == request.OperationId
                    ? Result.Success(open.Id)
                    : Result.Failure<int>(new Error(CashErrors.ShiftAlreadyOpen));
            }

            var previousCounted = await db.CashShifts.AsNoTracking()
                .Where(s => !s.IsOpen)
                .OrderByDescending(s => s.Id)
                .Select(s => s.CountedBalance)
                .FirstOrDefaultAsync(cancellationToken) ?? 0m;

            var opened = CashShift.Open(
                clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.UserName ?? string.Empty,
                request.OpeningBalance, previousCounted, request.Note, request.OperationId);
            if (opened.IsFailure)
            {
                return Result.Failure<int>(opened.Error!);
            }

            var shift = opened.Value;
            db.CashShifts.Add(shift);
            auditLog.Record(AuditActions.CashShiftOpened, details: new
            {
                shift.OpeningBalance,
                PreviousCounted = previousCounted,
                Note = shift.OpeningNote,
            });

            // The shift id is assigned by the database, so save before returning it.
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(shift.Id);
        }, Result.Failure<int>);
    }

    /// <summary>
    /// Counts the cash and closes the shift. The user who opened it closes it; closing another user's shift
    /// (a forgotten one) needs Cash.ForceClose and a reason.
    /// </summary>
    public async Task<Result> CloseShiftAsync(CloseShiftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.CashOpenShift) && !currentUser.HasPermission(Permissions.CashForceClose))
        {
            return Result.Failure(_forbidden);
        }

        return await db.RunGuardedAsync(async () =>
        {
            var shift = await db.LoadShiftForUpdateAsync(request.ShiftId, cancellationToken);
            if (shift is null)
            {
                return Result.Failure(new Error(CashErrors.UnknownShift));
            }

            if (!shift.IsOpen)
            {
                return shift.CloseOperationId == request.OperationId
                    ? Result.Success()
                    : Result.Failure(new Error(CashErrors.ShiftClosed));
            }

            var forced = shift.OpenedByUserId != currentUser.UserId;
            if (forced && !currentUser.HasPermission(Permissions.CashForceClose))
            {
                return Result.Failure(_forbidden);
            }

            var expected = shift.CurrentBalance;
            var closed = shift.Close(
                clock.GetUtcNow().UtcDateTime, currentUser.UserId, currentUser.UserName ?? string.Empty,
                request.CountedBalance, request.Note, forced, request.OperationId);
            if (closed.IsFailure)
            {
                return closed;
            }

            auditLog.Record(forced ? AuditActions.CashShiftForceClosed : AuditActions.CashShiftClosed, details: new
            {
                ShiftId = shift.Id,
                OpenedBy = shift.OpenedByUserName,
                Expected = expected,
                Counted = shift.CountedBalance,
                shift.Difference,
                Note = shift.CloseNote,
            });
            return Result.Success();
        }, Result.Failure);
    }

    /// <summary>Records cash put into or taken out of the drawer by hand, on the open shift.</summary>
    public async Task<Result> RecordManualMovementAsync(ManualCashRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.CashManualMovement))
        {
            return Result.Failure(_forbidden);
        }

        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            return Result.Failure(new Error(CashErrors.NoteRequired));
        }

        if (note.Length > CashMovement.NoteMaxLength)
        {
            return Result.Failure(new Error(CashErrors.NoteTooLong, CashMovement.NoteMaxLength));
        }

        return await db.RunGuardedAsync(async () =>
        {
            if (await ledger.OperationExistsAsync(request.OperationId, cancellationToken))
            {
                return Result.Success();
            }

            var info = new CashMovementInfo(note, OperationId: request.OperationId);
            var movement = request.Direction == CashDirection.In
                ? await ledger.ReceiveAsync(CashMovementType.ManualIn, request.Amount, info, cancellationToken)
                : await ledger.PayAsync(CashMovementType.ManualOut, request.Amount, info, cancellationToken);
            if (movement.IsFailure)
            {
                return Result.Failure(movement.Error!);
            }

            auditLog.Record(AuditActions.CashManualMovement, details: new
            {
                request.Direction,
                request.Amount,
                Note = note,
                BalanceAfter = movement.Value.BalanceAfter,
            });
            return Result.Success();
        }, Result.Failure);
    }
}
