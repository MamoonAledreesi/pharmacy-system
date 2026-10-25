using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Cash;

/// <summary>Why a cash movement happened and which document caused it; the ledger adds who and when.</summary>
public sealed record CashMovementInfo(
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// The cash engine used by every feature that moves cash (manual movements now; sales, collections, supplier
/// payments, refunds, and expenses later). It stages the movement on the open shift in the current unit of work
/// and never commits: the caller owns the transaction, so a document and its cash movement succeed or fail together.
/// <para>
/// The open shift is locked while the movement is applied, so two cashiers working at once cannot both read the old
/// balance. Callers must run inside <see cref="IPharmacyDbContext.ExecuteInTransactionAsync{TResult}"/>.
/// </para>
/// </summary>
public sealed class CashLedger(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public Task<bool> OperationExistsAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        db.CashMovements.AnyAsync(m => m.OperationId == operationId, cancellationToken);

    /// <summary>Brings cash into the drawer. Fails with <see cref="CashErrors.NoOpenShift"/> when no shift is open.</summary>
    public async Task<Result<CashMovement>> ReceiveAsync(
        CashMovementType type, decimal amount, CashMovementInfo info, CancellationToken cancellationToken = default)
    {
        var shift = await db.LoadOpenShiftForUpdateAsync(cancellationToken);
        return shift is null ? NoOpenShift() : Stage(shift.Receive(type, amount, ContextFor(info)));
    }

    /// <summary>Takes cash out of the drawer. Fails when no shift is open or the drawer holds less than the amount.</summary>
    public async Task<Result<CashMovement>> PayAsync(
        CashMovementType type, decimal amount, CashMovementInfo info, CancellationToken cancellationToken = default)
    {
        var shift = await db.LoadOpenShiftForUpdateAsync(cancellationToken);
        return shift is null ? NoOpenShift() : Stage(shift.Pay(type, amount, ContextFor(info)));
    }

    public CashContext ContextFor(CashMovementInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new CashContext(
            clock.GetUtcNow().UtcDateTime,
            currentUser.UserId,
            currentUser.UserName ?? string.Empty,
            info.Note?.Trim(),
            info.ReferenceType,
            info.ReferenceId,
            info.OperationId);
    }

    private Result<CashMovement> Stage(Result<CashMovement> movement)
    {
        if (movement.IsSuccess)
        {
            db.CashMovements.Add(movement.Value);
        }

        return movement;
    }

    private static Result<CashMovement> NoOpenShift() => Result.Failure<CashMovement>(new Error(CashErrors.NoOpenShift));
}
