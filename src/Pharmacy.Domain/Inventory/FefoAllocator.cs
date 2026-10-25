using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Inventory;

/// <summary>The stock of one batch as the allocator sees it.</summary>
public sealed record BatchStock(int BatchId, DateOnly? ExpiryDate, DateTime ReceivedAtUtc, int Available, decimal UnitCost);

/// <summary>Part of a requested quantity taken from one batch, with that batch's actual cost.</summary>
public sealed record StockAllocation(int BatchId, int Quantity, decimal UnitCost, DateOnly? ExpiryDate, bool IsNearExpiry);

/// <summary>
/// First-Expired-First-Out allocation: stock is taken from the batch that expires first. Expired stock is never
/// allocated, and neither is quarantined stock (it is not part of the available quantity). Batches without an
/// expiry date come after dated ones, oldest received first. The result is deterministic: ties are broken
/// by receipt time and then by batch id.
/// </summary>
public static class FefoAllocator
{
    public static Result<IReadOnlyList<StockAllocation>> Allocate(
        IEnumerable<BatchStock> batches, int quantity, DateOnly today, int nearExpiryDays)
    {
        ArgumentNullException.ThrowIfNull(batches);

        if (quantity <= 0)
        {
            return Result.Failure<IReadOnlyList<StockAllocation>>(new Error(InventoryErrors.InvalidQuantity));
        }

        var sellable = Sellable(batches, today).ToList();
        var sellableQuantity = SumQuantity(sellable);
        if (sellableQuantity < quantity)
        {
            return Result.Failure<IReadOnlyList<StockAllocation>>(new Error(InventoryErrors.InsufficientStock, sellableQuantity));
        }

        var allocations = new List<StockAllocation>();
        var remaining = quantity;
        foreach (var batch in sellable)
        {
            if (remaining == 0)
            {
                break;
            }

            var taken = Math.Min(batch.Available, remaining);
            allocations.Add(new StockAllocation(
                batch.BatchId, taken, batch.UnitCost, batch.ExpiryDate, ExpiryRules.IsNearExpiry(batch.ExpiryDate, today, nearExpiryDays)));
            remaining -= taken;
        }

        return Result.Success<IReadOnlyList<StockAllocation>>(allocations);
    }

    /// <summary>The quantity that can be sold today: available stock of batches that have not expired.</summary>
    public static int SellableQuantity(IEnumerable<BatchStock> batches, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(batches);
        return SumQuantity(Sellable(batches, today));
    }

    private static IEnumerable<BatchStock> Sellable(IEnumerable<BatchStock> batches, DateOnly today) =>
        batches
            .Where(b => b.Available > 0 && !ExpiryRules.IsExpired(b.ExpiryDate, today))
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.ReceivedAtUtc)
            .ThenBy(b => b.BatchId);

    // long: the total of many large batches must not overflow before it is compared with the request.
    private static int SumQuantity(IEnumerable<BatchStock> batches) =>
        (int)Math.Min(batches.Sum(b => (long)b.Available), int.MaxValue);
}
