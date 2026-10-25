using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Inventory;

/// <summary>Why a movement happened and which document caused it; the ledger adds who and when.</summary>
public sealed record MovementInfo(
    string? ReasonCode = null,
    string? Note = null,
    string? ReferenceType = null,
    long? ReferenceId = null,
    Guid? OperationId = null);

/// <summary>
/// The stock engine used by every feature that changes stock (opening stock and adjustments now; purchases,
/// sales, and returns later). It stages batches and ledger movements on the current unit of work and never
/// commits: the caller owns the transaction, so a document and its stock changes succeed or fail together.
/// <para>
/// Every method that changes stock first locks the product's batches (see
/// <see cref="IPharmacyDbContext.LoadBatchesForUpdateAsync"/>), so two operations on the same product's
/// stock run one after the other. Callers must run inside <see cref="IPharmacyDbContext.ExecuteInTransactionAsync{TResult}"/>.
/// </para>
/// </summary>
public sealed partial class InventoryLedger(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const string AutoPrefix = "AUTO-";

    /// <summary>Today in the pharmacy's local time; expiry is judged against it.</summary>
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public Task<bool> OperationExistsAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        db.StockMovements.AnyAsync(m => m.OperationId == operationId, cancellationToken);

    /// <summary>The batch a previously applied operation changed (used to answer a repeated submission).</summary>
    public Task<int?> BatchOfOperationAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        db.StockMovements
            .Where(m => m.OperationId == operationId)
            .OrderBy(m => m.Id)
            .Select(m => (int?)m.BatchId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The near-expiry threshold for a product: its own setting, otherwise the pharmacy-wide one.</summary>
    public async Task<int> NearExpiryDaysAsync(int productId, CancellationToken cancellationToken = default)
    {
        var own = await db.Products.Where(p => p.Id == productId).Select(p => p.NearExpiryDays).SingleOrDefaultAsync(cancellationToken);
        return own ?? await db.PharmacySettings.Select(s => s.NearExpiryDays).SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Adds stock to the batch identified by product, batch number, expiry, and unit cost, creating the batch
    /// when it does not exist yet. A blank batch number joins an existing automatic batch with the same
    /// expiry and cost, otherwise gets the next automatic number (AUTO-0001, ...).
    /// </summary>
    public async Task<Result<Batch>> ReceiveAsync(
        StockMovementType type, int productId, string? batchNumber, DateOnly? expiryDate, decimal unitCost,
        int quantity, MovementInfo info, CancellationToken cancellationToken = default)
    {
        var batches = await db.LoadBatchesForUpdateAsync(productId, cancellationToken);
        var number = batchNumber?.Trim() ?? string.Empty;

        var batch = FindBatch(batches, number, expiryDate, unitCost);
        var isNew = batch is null;
        if (isNew)
        {
            var created = Batch.Create(
                productId, number.Length == 0 ? NextAutoNumber(batches) : number, expiryDate, unitCost, clock.GetUtcNow().UtcDateTime);
            if (created.IsFailure)
            {
                return created;
            }

            batch = created.Value;
        }

        var movement = batch!.Receive(type, quantity, ContextFor(info));
        if (movement.IsFailure)
        {
            return Result.Failure<Batch>(movement.Error!);
        }

        if (isNew)
        {
            db.Batches.Add(batch);
        }

        db.StockMovements.Add(movement.Value);
        return Result.Success(batch);
    }

    /// <summary>Loads (and locks) a batch together with all other batches of its product.</summary>
    public async Task<Batch?> LockBatchAsync(int batchId, CancellationToken cancellationToken = default)
    {
        var productId = await db.Batches.AsNoTracking()
            .Where(b => b.Id == batchId)
            .Select(b => (int?)b.ProductId)
            .SingleOrDefaultAsync(cancellationToken);

        return productId is null
            ? null
            : (await db.LoadBatchesForUpdateAsync(productId.Value, cancellationToken)).SingleOrDefault(b => b.Id == batchId);
    }

    /// <summary>Applies a movement produced by a <see cref="Batch"/> operation to the unit of work.</summary>
    public Result Stage(Result<StockMovement> movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        if (movement.IsFailure)
        {
            return Result.Failure(movement.Error!);
        }

        db.StockMovements.Add(movement.Value);
        return Result.Success();
    }

    public MovementContext ContextFor(MovementInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new MovementContext(
            clock.GetUtcNow().UtcDateTime,
            currentUser.UserId,
            currentUser.UserName ?? string.Empty,
            info.ReasonCode,
            info.Note,
            info.ReferenceType,
            info.ReferenceId,
            info.OperationId);
    }

    /// <summary>
    /// Chooses the batches to sell from, first expiring first, without changing anything. The product's batches
    /// stay locked until the transaction ends, so the allocation is still valid when the caller applies it.
    /// </summary>
    public async Task<Result<IReadOnlyList<StockAllocation>>> AllocateFefoAsync(
        int productId, int quantity, CancellationToken cancellationToken = default)
    {
        var batches = await db.LoadBatchesForUpdateAsync(productId, cancellationToken);
        return FefoAllocator.Allocate(
            batches.Select(ToStock), quantity, Today, await NearExpiryDaysAsync(productId, cancellationToken));
    }

    /// <summary>Takes <paramref name="quantity"/> base units from the product's batches, first expiring first.</summary>
    public async Task<Result<IReadOnlyList<StockAllocation>>> ConsumeFefoAsync(
        int productId, int quantity, StockMovementType type, MovementInfo info, CancellationToken cancellationToken = default)
    {
        var batches = await db.LoadBatchesForUpdateAsync(productId, cancellationToken);
        var allocated = FefoAllocator.Allocate(
            batches.Select(ToStock), quantity, Today, await NearExpiryDaysAsync(productId, cancellationToken));
        if (allocated.IsFailure)
        {
            return allocated;
        }

        foreach (var allocation in allocated.Value)
        {
            var batch = batches.Single(b => b.Id == allocation.BatchId);
            var staged = Stage(batch.Deduct(type, allocation.Quantity, ContextFor(info)));
            if (staged.IsFailure)
            {
                return Result.Failure<IReadOnlyList<StockAllocation>>(staged.Error!);
            }
        }

        return allocated;
    }

    private static BatchStock ToStock(Batch batch) =>
        new(batch.Id, batch.ExpiryDate, batch.ReceivedAtUtc, batch.QuantityAvailable, batch.UnitCost);

    private static Batch? FindBatch(List<Batch> batches, string number, DateOnly? expiryDate, decimal unitCost) =>
        batches.FirstOrDefault(b =>
            b.ExpiryDate == expiryDate
            && b.UnitCost == unitCost
            && (number.Length == 0
                ? b.BatchNumber.StartsWith(AutoPrefix, StringComparison.Ordinal)
                : string.Equals(b.BatchNumber, number, StringComparison.OrdinalIgnoreCase)));

    private static string NextAutoNumber(List<Batch> batches)
    {
        var highest = batches
            .Select(b => AutoNumberPattern().Match(b.BatchNumber))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();

        return $"{AutoPrefix}{highest + 1:D4}";
    }

    [GeneratedRegex(@"^AUTO-(\d{1,9})$")]
    private static partial Regex AutoNumberPattern();
}
