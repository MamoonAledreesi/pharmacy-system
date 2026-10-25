using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Inventory;

/// <summary>
/// Opening stock for a product. <c>OperationId</c> is generated once per form: if the same submission arrives
/// twice (double click, retry) the second one changes nothing and reports the first result.
/// <c>CostPerUnit</c> is the cost of one unit of the chosen packaging (for example one box).
/// </summary>
public sealed record OpeningStockRequest(
    Guid OperationId, int ProductId, int ProductUnitId, int Quantity, decimal CostPerUnit, string? BatchNumber, DateOnly? ExpiryDate);

/// <summary>
/// A manual stock operation on one batch. <c>Quantity</c> is in the chosen unit and is converted to base
/// units using that unit's factor.
/// </summary>
public sealed record StockActionRequest(
    Guid OperationId, int BatchId, int ProductUnitId, int Quantity, string? ReasonCode, string? Note);

public enum AdjustmentDirection
{
    Increase = 0,
    Decrease = 1,
}

/// <summary>
/// Manual stock operations: opening stock, adjustments, quarantine, and write-off. Each runs in one
/// transaction together with its ledger movement and audit entry.
/// </summary>
public sealed class InventoryService(
    IPharmacyDbContext db, InventoryLedger ledger, ICurrentUser currentUser, IAuditLog auditLog)
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>Records stock that exists at start-up (or was forgotten): creates or tops up a batch. Returns the batch id.</summary>
    public async Task<Result<int>> ReceiveOpeningStockAsync(OpeningStockRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.InventoryOpeningStock))
        {
            return Result.Failure<int>(_forbidden);
        }

        return await RunAsync(async () =>
        {
            if (await ledger.BatchOfOperationAsync(request.OperationId, cancellationToken) is { } existing)
            {
                return Result.Success(existing);
            }

            var product = await LoadProductAsync(request.ProductId, cancellationToken);
            var unit = product?.Units.SingleOrDefault(u => u.Id == request.ProductUnitId);
            if (product is null || unit is null)
            {
                return Failure<int>(product is null ? InventoryErrors.UnknownProduct : InventoryErrors.UnknownUnit);
            }

            if (!product.IsActive)
            {
                return Failure<int>(InventoryErrors.InactiveProduct);
            }

            var quantity = ToBaseQuantity(request.Quantity, unit.Factor);
            if (quantity.IsFailure)
            {
                return Result.Failure<int>(quantity.Error!);
            }

            if (request.CostPerUnit is < 0 or > Batch.MaxUnitCost)
            {
                return Failure<int>(InventoryErrors.InvalidUnitCost);
            }

            var expiry = product.TracksExpiry ? request.ExpiryDate : null;
            if (product.TracksExpiry && expiry is null)
            {
                return Failure<int>(InventoryErrors.ExpiryRequired);
            }

            var baseCost = Batch.UnitCostFromPackCost(request.CostPerUnit, unit.Factor);
            var received = await ledger.ReceiveAsync(
                StockMovementType.OpeningStock, product.Id, request.BatchNumber, expiry, baseCost, quantity.Value,
                new MovementInfo(OperationId: request.OperationId), cancellationToken);
            if (received.IsFailure)
            {
                return Result.Failure<int>(received.Error!);
            }

            auditLog.Record(AuditActions.StockOpening, product.DisplayName(arabic: false), new
            {
                Batch = received.Value.BatchNumber,
                Expiry = expiry,
                BaseQuantity = quantity.Value,
                UnitCost = baseCost,
            });

            // The batch id is assigned by the database, so save before returning it.
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(received.Value.Id);
        }, Result.Failure<int>);
    }

    /// <summary>Corrects a batch quantity (count difference, loss, damage). The reason is mandatory.</summary>
    public Task<Result> AdjustAsync(AdjustmentDirection direction, StockActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            Permissions.InventoryAdjust, AuditActions.StockAdjusted, StockReasons.ForAdjustment, reasonRequired: true, request,
            (batch, quantity, context) => direction == AdjustmentDirection.Increase
                ? batch.Receive(StockMovementType.AdjustmentIncrease, quantity, context)
                : batch.Deduct(StockMovementType.AdjustmentDecrease, quantity, context),
            cancellationToken);
    }

    /// <summary>Sets sellable stock aside (it can no longer be sold).</summary>
    public Task<Result> QuarantineAsync(StockActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            Permissions.InventoryAdjust, AuditActions.StockQuarantined, StockReasons.ForQuarantine, reasonRequired: true, request,
            (batch, quantity, context) => batch.Quarantine(quantity, context),
            cancellationToken);
    }

    /// <summary>Returns quarantined stock to sellable stock. A reason is optional.</summary>
    public Task<Result> ReleaseFromQuarantineAsync(StockActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            Permissions.InventoryAdjust, AuditActions.StockReleased, StockReasons.All, reasonRequired: false, request,
            (batch, quantity, context) => batch.Release(quantity, context),
            cancellationToken);
    }

    /// <summary>Destroys stock: expired, damaged, or lost. It leaves the books at the batch's cost.</summary>
    public Task<Result> WriteOffAsync(bool fromQuarantine, StockActionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ApplyAsync(
            Permissions.InventoryWriteOff, AuditActions.StockWrittenOff, StockReasons.ForWriteOff, reasonRequired: true, request,
            (batch, quantity, context) => fromQuarantine
                ? batch.WriteOffQuarantined(quantity, context)
                : batch.Deduct(StockMovementType.WriteOff, quantity, context),
            cancellationToken);
    }

    private async Task<Result> ApplyAsync(
        string permission,
        string auditAction,
        IReadOnlyList<string> allowedReasons,
        bool reasonRequired,
        StockActionRequest request,
        Func<Batch, int, MovementContext, Result<StockMovement>> operation,
        CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(permission))
        {
            return Result.Failure(_forbidden);
        }

        var reasonError = ValidateReason(request.ReasonCode, request.Note, allowedReasons, reasonRequired);
        if (reasonError is not null)
        {
            return Result.Failure(reasonError);
        }

        return await RunAsync(async () =>
        {
            if (await ledger.OperationExistsAsync(request.OperationId, cancellationToken))
            {
                return Result.Success();
            }

            var batch = await ledger.LockBatchAsync(request.BatchId, cancellationToken);
            if (batch is null)
            {
                return Failure(InventoryErrors.UnknownBatch);
            }

            var product = await LoadProductAsync(batch.ProductId, cancellationToken);
            var unit = product?.Units.SingleOrDefault(u => u.Id == request.ProductUnitId);
            if (product is null || unit is null)
            {
                return Failure(InventoryErrors.UnknownUnit);
            }

            var quantity = ToBaseQuantity(request.Quantity, unit.Factor);
            if (quantity.IsFailure)
            {
                return Result.Failure(quantity.Error!);
            }

            var info = new MovementInfo(request.ReasonCode, request.Note?.Trim(), OperationId: request.OperationId);
            var staged = ledger.Stage(operation(batch, quantity.Value, ledger.ContextFor(info)));
            if (staged.IsFailure)
            {
                return staged;
            }

            auditLog.Record(auditAction, product.DisplayName(arabic: false), new
            {
                Batch = batch.BatchNumber,
                Expiry = batch.ExpiryDate,
                BaseQuantity = quantity.Value,
                Reason = request.ReasonCode,
                request.Note,
            });
            return Result.Success();
        }, Result.Failure);
    }

    private Task<TResult> RunAsync<TResult>(Func<Task<TResult>> work, Func<Error, TResult> failure)
        where TResult : Result =>
        db.RunGuardedAsync(work, failure);

    private static Error? ValidateReason(string? reasonCode, string? note, IReadOnlyList<string> allowed, bool required)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return required ? new Error(InventoryErrors.ReasonRequired) : null;
        }

        if (!allowed.Contains(reasonCode, StringComparer.Ordinal))
        {
            return new Error(InventoryErrors.UnknownReason);
        }

        if (reasonCode == StockReasons.Other && string.IsNullOrWhiteSpace(note))
        {
            return new Error(InventoryErrors.NoteRequired);
        }

        return note?.Trim().Length > StockReasons.NoteMaxLength
            ? new Error(InventoryErrors.NoteTooLong, StockReasons.NoteMaxLength)
            : null;
    }

    /// <summary>Converts a quantity of a packaging unit to base units, rejecting zero, negatives, and overflow.</summary>
    private static Result<int> ToBaseQuantity(int quantity, int factor)
    {
        if (quantity <= 0)
        {
            return Failure<int>(InventoryErrors.InvalidQuantity);
        }

        var baseQuantity = (long)quantity * factor;
        return baseQuantity > Batch.MaxOperationQuantity
            ? Result.Failure<int>(new Error(InventoryErrors.QuantityTooLarge, Batch.MaxOperationQuantity))
            : Result.Success((int)baseQuantity);
    }

    private Task<Product?> LoadProductAsync(int productId, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking().Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

    private static Result<T> Failure<T>(string code) => Result.Failure<T>(new Error(code));

    private static Result Failure(string code) => Result.Failure(new Error(code));
}
