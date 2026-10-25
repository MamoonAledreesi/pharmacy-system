using Pharmacy.Domain.Inventory;

namespace Pharmacy.Domain.Tests.Inventory;

public class FefoAllocatorTests
{
    private static readonly DateOnly _today = new(2026, 10, 24);
    private static readonly DateTime _received = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int NearDays = 90;

    private static BatchStock Batch(int id, DateOnly? expiry, int available, decimal cost = 10m, int receivedDayOffset = 0) =>
        new(id, expiry, _received.AddDays(receivedDayOffset), available, cost);

    [Fact]
    public void TakesFromTheBatchThatExpiresFirst_NotTheOldestOrFirstListed()
    {
        BatchStock[] batches =
        [
            Batch(1, new DateOnly(2028, 1, 31), 10),
            Batch(2, new DateOnly(2027, 3, 31), 10),
            Batch(3, new DateOnly(2027, 12, 31), 10),
        ];

        var result = FefoAllocator.Allocate(batches, 4, _today, NearDays).Value;

        result.Select(a => (a.BatchId, a.Quantity)).ShouldBe([(2, 4)]);
    }

    [Fact]
    public void SpillsOverToTheNextBatchWithEachBatchesOwnCost()
    {
        BatchStock[] batches =
        [
            Batch(1, new DateOnly(2027, 3, 31), 3, cost: 10m),
            Batch(2, new DateOnly(2027, 6, 30), 10, cost: 12m),
        ];

        var result = FefoAllocator.Allocate(batches, 5, _today, NearDays).Value;

        result.Select(a => (a.BatchId, a.Quantity, a.UnitCost)).ShouldBe([(1, 3, 10m), (2, 2, 12m)]);
    }

    [Fact]
    public void NeverAllocatesExpiredStock()
    {
        BatchStock[] batches =
        [
            Batch(1, new DateOnly(2026, 10, 23), 50),
            Batch(2, new DateOnly(2027, 1, 31), 5),
        ];

        var result = FefoAllocator.Allocate(batches, 5, _today, NearDays).Value;

        result.Select(a => a.BatchId).ShouldBe([2]);
    }

    [Fact]
    public void AllocationFailsRatherThanTouchingExpiredStock()
    {
        BatchStock[] batches =
        [
            Batch(1, new DateOnly(2026, 10, 23), 50),
            Batch(2, new DateOnly(2027, 1, 31), 5),
        ];

        var result = FefoAllocator.Allocate(batches, 6, _today, NearDays);

        result.Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        result.Error.Arguments.ShouldBe([5]);
    }

    [Fact]
    public void ABatchIsStillSellableOnItsExpiryDate()
    {
        BatchStock[] batches = [Batch(1, _today, 5)];

        FefoAllocator.Allocate(batches, 5, _today, NearDays).IsSuccess.ShouldBeTrue();
        FefoAllocator.Allocate(batches, 5, _today.AddDays(1), NearDays).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void QuarantinedStockIsNotPartOfTheAvailableQuantity()
    {
        // The allocator only sees available quantities; a batch with everything quarantined has none.
        BatchStock[] batches = [Batch(1, new DateOnly(2027, 1, 31), 0), Batch(2, new DateOnly(2027, 6, 30), 4)];

        FefoAllocator.Allocate(batches, 4, _today, NearDays).Value.Select(a => a.BatchId).ShouldBe([2]);
    }

    [Fact]
    public void BatchesWithoutExpiryComeAfterDatedOnes_OldestReceivedFirst()
    {
        BatchStock[] batches =
        [
            Batch(1, null, 5, receivedDayOffset: 30),
            Batch(2, null, 5, receivedDayOffset: 10),
            Batch(3, new DateOnly(2030, 1, 1), 5),
        ];

        var result = FefoAllocator.Allocate(batches, 12, _today, NearDays).Value;

        result.Select(a => (a.BatchId, a.Quantity)).ShouldBe([(3, 5), (2, 5), (1, 2)]);
    }

    [Fact]
    public void TiesAreBrokenByReceiptTimeThenBatchId_SoTheResultIsDeterministic()
    {
        var sameDay = new DateOnly(2027, 5, 31);
        BatchStock[] batches =
        [
            Batch(7, sameDay, 5, receivedDayOffset: 2),
            Batch(5, sameDay, 5, receivedDayOffset: 1),
            Batch(4, sameDay, 5, receivedDayOffset: 1),
        ];

        FefoAllocator.Allocate(batches, 15, _today, NearDays).Value.Select(a => a.BatchId).ShouldBe([4, 5, 7]);
        FefoAllocator.Allocate(batches.Reverse(), 15, _today, NearDays).Value.Select(a => a.BatchId).ShouldBe([4, 5, 7]);
    }

    [Fact]
    public void MarksAllocationsFromNearExpiryBatches()
    {
        BatchStock[] batches =
        [
            Batch(1, _today.AddDays(NearDays), 1),
            Batch(2, _today.AddDays(NearDays + 1), 1),
        ];

        var result = FefoAllocator.Allocate(batches, 2, _today, NearDays).Value;

        result.Select(a => (a.BatchId, a.IsNearExpiry)).ShouldBe([(1, true), (2, false)]);
    }

    [Fact]
    public void ExactlyTheAvailableQuantity_ConsumesEverything()
    {
        BatchStock[] batches = [Batch(1, new DateOnly(2027, 1, 31), 3), Batch(2, new DateOnly(2027, 2, 28), 4)];

        FefoAllocator.Allocate(batches, 7, _today, NearDays).Value.Sum(a => a.Quantity).ShouldBe(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RejectsNonPositiveQuantities(int quantity)
    {
        FefoAllocator.Allocate([Batch(1, null, 5)], quantity, _today, NearDays).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
    }

    [Fact]
    public void NoBatches_IsAnInsufficientStockFailureWithZeroAvailable()
    {
        var result = FefoAllocator.Allocate([], 1, _today, NearDays);

        result.Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        result.Error.Arguments.ShouldBe([0]);
    }

    [Fact]
    public void LargeBatchesDoNotOverflowTheTotal()
    {
        BatchStock[] batches = [Batch(1, null, int.MaxValue), Batch(2, null, int.MaxValue)];

        FefoAllocator.Allocate(batches, 5, _today, NearDays).Value.Single().Quantity.ShouldBe(5);
        FefoAllocator.SellableQuantity(batches, _today).ShouldBe(int.MaxValue);
    }

    [Fact]
    public void SellableQuantity_ExcludesExpiredBatches()
    {
        BatchStock[] batches = [Batch(1, new DateOnly(2026, 1, 1), 100), Batch(2, new DateOnly(2027, 1, 1), 7), Batch(3, null, 3)];

        FefoAllocator.SellableQuantity(batches, _today).ShouldBe(10);
    }
}
