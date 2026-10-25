using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Inventory;

/// <summary>A migrated throwaway database, a signed-in store keeper, and one expiry-tracked product (strip, box of 3 strips).</summary>
public abstract class InventoryTestBase : IAsyncLifetime
{
    // Ids seeded by the catalog migration.
    protected const int GeneralCategory = 1;
    protected const int StripMeasure = 3;
    protected const int BoxMeasure = 4;

    protected static readonly string[] StoreKeeper =
    [
        Permissions.CatalogView, Permissions.CatalogManage, Permissions.InventoryView, Permissions.InventoryViewCost,
        Permissions.InventoryOpeningStock, Permissions.InventoryAdjust, Permissions.InventoryWriteOff, Permissions.SettingsManage,
    ];

    /// <summary>What the signed-in user may do at the start of each test; derived bases add the permissions of their module.</summary>
    protected virtual string[] InitialPermissions => StoreKeeper;

    protected TestServices Services { get; } = new();

    protected int ProductId { get; private set; }

    /// <summary>The product's unit ids (ProductUnit.Id, not the measure ids).</summary>
    protected int StripId { get; private set; }

    protected int BoxId { get; private set; }

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public async Task InitializeAsync()
    {
        await Services.InitializeAsync();
        SignIn(InitialPermissions);
        (ProductId, StripId, BoxId) = await CreateProductAsync("Panadol", tracksExpiry: true);
    }

    public Task DisposeAsync() => Services.DisposeAsync();

    protected void SignIn(params string[] permissions) => Services.CurrentUser.SignInAs(7, "keeper", isAdministrator: false, permissions);

    protected async Task<(int ProductId, int StripId, int BoxId)> CreateProductAsync(
        string name, bool tracksExpiry, int? minimumStock = null, int? nearExpiryDays = null)
    {
        var details = new ProductDetails(null, name, null, null, null, null, GeneralCategory, null, tracksExpiry, minimumStock, nearExpiryDays);
        ProductUnitData[] units = [new(null, StripMeasure, true, 1, 15m, null), new(null, BoxMeasure, false, 3, 40m, null)];
        var id = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().CreateAsync(details, units))).Value;
        var saved = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(id))).Value;
        return (id, saved.Units.Single(u => u.IsBase).Id!.Value, saved.Units.Single(u => !u.IsBase).Id!.Value);
    }

    protected Task<Result<int>> OpeningAsync(
        int quantity, decimal cost, string? batchNumber, DateOnly? expiry, int? productId = null, int? unitId = null, Guid? operationId = null) =>
        Inventory(s => s.ReceiveOpeningStockAsync(new OpeningStockRequest(
            operationId ?? Guid.NewGuid(), productId ?? ProductId, unitId ?? StripId, quantity, cost, batchNumber, expiry)));

    protected Task<T> Inventory<T>(Func<InventoryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<InventoryService>()));

    protected Task<T> Query<T>(Func<InventoryQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<InventoryQueryService>()));

    protected Task<List<Batch>> BatchesAsync(int? productId = null) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Batches.AsNoTracking()
            .Where(b => b.ProductId == (productId ?? ProductId)).OrderBy(b => b.Id).ToListAsync());

    protected Task<List<StockMovement>> MovementsAsync(int? productId = null) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().StockMovements.AsNoTracking()
            .Where(m => m.ProductId == (productId ?? ProductId)).OrderBy(m => m.Id).ToListAsync());

    protected Task<List<string>> AuditActionsAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.OrderBy(e => e.Id).Select(e => e.Action).ToListAsync());

    /// <summary>Sells through the stock engine exactly as the future sales feature will: allocate FEFO and deduct, in one transaction.</summary>
    protected Task<Result<IReadOnlyList<StockAllocation>>> ConsumeAsync(int quantity, int? productId = null) =>
        Services.InScopeAsync(s =>
        {
            var db = s.GetRequiredService<IPharmacyDbContext>();
            var ledger = s.GetRequiredService<InventoryLedger>();
            return db.ExecuteInTransactionAsync(() => ledger.ConsumeFefoAsync(
                productId ?? ProductId, quantity, StockMovementType.Sale, new MovementInfo(ReferenceType: "Test")));
        });

    /// <summary>The ledger invariant: every batch balance equals the sum of that batch's movements.</summary>
    protected async Task AssertLedgerMatchesBatchesAsync()
    {
        var (batches, movements) = await Services.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<PharmacyDbContext>();
            return (await db.Batches.AsNoTracking().ToListAsync(), await db.StockMovements.AsNoTracking().ToListAsync());
        });

        foreach (var batch in batches)
        {
            var own = movements.Where(m => m.BatchId == batch.Id).OrderBy(m => m.Id).ToList();
            batch.QuantityAvailable.ShouldBe(own.Sum(m => m.AvailableDelta), $"available of batch {batch.Id}");
            batch.QuantityQuarantined.ShouldBe(own.Sum(m => m.QuarantineDelta), $"quarantined of batch {batch.Id}");
            batch.QuantityAvailable.ShouldBeGreaterThanOrEqualTo(0);
            batch.QuantityQuarantined.ShouldBeGreaterThanOrEqualTo(0);

            if (own.Count > 0)
            {
                own[^1].AvailableAfter.ShouldBe(batch.QuantityAvailable);
                own[^1].QuarantineAfter.ShouldBe(batch.QuantityQuarantined);
            }
        }
    }
}
