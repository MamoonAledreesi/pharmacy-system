using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Inventory;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Inventory;

/// <summary>
/// Stock must stay correct when several people work at once. These tests run real parallel
/// transactions against SQL Server: row locks serialize competing sales, and the database
/// constraints are the last line of defence.
/// </summary>
public sealed class InventoryConcurrencyTests : InventoryTestBase
{
    private const int Competitors = 8;

    [Fact]
    public async Task EightSalesCompetingForFiveUnits_SucceedExactlyFive_AndStockNeverGoesNegative()
    {
        await OpeningAsync(5, 10m, "ONLY", Today.AddDays(300));

        var results = await Task.WhenAll(Enumerable.Range(0, Competitors).Select(_ => Task.Run(() => ConsumeAsync(1))));

        results.Count(r => r.IsSuccess).ShouldBe(5);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error!.Code == InventoryErrors.InsufficientStock);
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(0);
        (await MovementsAsync()).Where(m => m.Type == StockMovementType.Sale).Sum(m => m.AvailableDelta).ShouldBe(-5);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task ParallelSalesAcrossSeveralBatches_NeverOversellAnyBatch()
    {
        await OpeningAsync(10, 10m, "A", Today.AddDays(50));
        await OpeningAsync(10, 11m, "B", Today.AddDays(100));
        await OpeningAsync(10, 12m, "C", Today.AddDays(150));

        // 12 sales of 3 units want 36 units; only 30 exist.
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => ConsumeAsync(3))));

        results.Count(r => r.IsSuccess).ShouldBe(10);
        (await BatchesAsync()).Sum(b => b.QuantityAvailable).ShouldBe(0);
        (await BatchesAsync()).ShouldAllBe(b => b.QuantityAvailable >= 0);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task ASaleAndAnAdjustmentOnTheSameBatchAtTheSameMoment_BothAreAppliedConsistently()
    {
        var batchId = (await OpeningAsync(10, 10m, "X", Today.AddDays(300))).Value;

        var tasks = new List<Task<Domain.Common.Result>>();
        for (var i = 0; i < 6; i++)
        {
            tasks.Add(Task.Run(async () => (Domain.Common.Result)await ConsumeAsync(1)));
            tasks.Add(Task.Run(() => Inventory(s => s.AdjustAsync(
                AdjustmentDirection.Increase, new StockActionRequest(Guid.NewGuid(), batchId, StripId, 1, StockReasons.CountDifference, null)))));
        }

        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(r => r.IsSuccess || r.Error!.Code == CommonErrors.ConcurrencyConflict);
        await AssertLedgerMatchesBatchesAsync();
        var movements = await MovementsAsync();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(movements.Sum(m => m.AvailableDelta));
    }

    [Fact]
    public async Task ParallelFirstReceipts_OfTheSameNewBatch_NeverCreateDuplicates()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
            OpeningAsync(10, 5m, "NEW", Today.AddDays(300)))));

        // Nothing can be locked yet for a batch that does not exist, so racing creators are told to retry
        // by the unique index instead of silently creating a second row.
        results.ShouldAllBe(r => r.IsSuccess || r.Error!.Code == CommonErrors.ConcurrencyConflict);
        results.Count(r => r.IsSuccess).ShouldBeGreaterThanOrEqualTo(1);
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(results.Count(r => r.IsSuccess) * 10);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task TheDatabaseItselfRefusesNegativeStock_EvenForSqlThatBypassesTheApplication()
    {
        await OpeningAsync(5, 10m, "B", Today.AddDays(300));

        var negativeAvailable = () => Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE inventory.Batches SET QuantityAvailable = -1"));
        var negativeQuarantine = () => Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE inventory.Batches SET QuantityQuarantined = -1"));

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(negativeAvailable);
        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(negativeQuarantine);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(5);
    }

    [Fact]
    public async Task TheDatabaseRefusesTwoBatchesWithTheSameIdentity_IncludingProductsWithoutExpiry()
    {
        var (productId, _, _) = await CreateProductAsync("NoExpiry", tracksExpiry: false);
        Task InsertDuplicate() => Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database.ExecuteSqlAsync(
            $"INSERT INTO inventory.Batches (ProductId, BatchNumber, ExpiryDate, UnitCost, QuantityAvailable, QuantityQuarantined, ReceivedAtUtc) VALUES ({productId}, N'DUP', NULL, 1.0000, 0, 0, SYSUTCDATETIME())"));

        await InsertDuplicate();

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(InsertDuplicate);
    }

    [Fact]
    public async Task TheDatabaseRefusesToDeleteABatchThatHasHistory()
    {
        await OpeningAsync(5, 10m, "B", Today.AddDays(300));

        var delete = () => Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("DELETE FROM inventory.Batches"));

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(delete);
        (await BatchesAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ALongRandomSequenceOfOperations_KeepsTheLedgerAndBatchesInAgreement()
    {
        var random = new Random(2026);
        var batchIds = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            batchIds.Add((await OpeningAsync(40, 5m + i, $"R{i}", Today.AddDays(30 + (i * 100)))).Value);
        }

        for (var step = 0; step < 60; step++)
        {
            var batchId = batchIds[random.Next(batchIds.Count)];
            var quantity = random.Next(1, 15);
            var request = new StockActionRequest(Guid.NewGuid(), batchId, StripId, quantity, StockReasons.Damaged, null);
            _ = random.Next(6) switch
            {
                0 => await Inventory(s => s.AdjustAsync(AdjustmentDirection.Increase, request)),
                1 => await Inventory(s => s.AdjustAsync(AdjustmentDirection.Decrease, request)),
                2 => await Inventory(s => s.QuarantineAsync(request)),
                3 => await Inventory(s => s.ReleaseFromQuarantineAsync(request)),
                4 => await Inventory(s => s.WriteOffAsync(random.Next(2) == 0, request)),
                _ => await ConsumeAsync(quantity),
            };
        }

        await AssertLedgerMatchesBatchesAsync();
        (await MovementsAsync()).Count.ShouldBeGreaterThan(20);
    }
}
