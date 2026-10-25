using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Settings;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.IntegrationTests.Inventory;

public sealed class InventoryQueryTests : InventoryTestBase
{
    private async Task<StockListItem> StockOfAsync(StockFilter filter = StockFilter.All, string? text = null, int? productId = null)
    {
        var page = (await Query(s => s.SearchStockAsync(new StockQuery(text, filter, 0, 50)))).Value;
        return page.Items.Single(i => i.ProductId == (productId ?? ProductId));
    }

    private async Task SeedThreeBatchesAsync()
    {
        await OpeningAsync(4, 10m, "EXPIRED", Today.AddDays(-10));
        await OpeningAsync(6, 10m, "NEAR", Today.AddDays(30));
        await OpeningAsync(10, 10m, "FAR", Today.AddDays(400));
    }

    [Fact]
    public async Task StockList_TotalsEachProduct_AndSeparatesExpiredAndNearExpiry()
    {
        await SeedThreeBatchesAsync();

        var item = await StockOfAsync();

        (item.Available, item.Quarantined, item.Expired, item.NearExpiry, item.Sellable).ShouldBe((20, 0, 4, 6, 16));
        item.StockValue.ShouldBe(200m);
        item.Units.Select(u => (u.Factor, u.IsBase)).ShouldBe([(1, true), (3, false)]);
    }

    [Fact]
    public async Task QuarantinedStock_IsCountedSeparately_AndIsPartOfTheStockValue()
    {
        var batchId = (await OpeningAsync(10, 10m, "B", Today.AddDays(400))).Value;
        await Inventory(s => s.QuarantineAsync(new StockActionRequest(Guid.NewGuid(), batchId, StripId, 4, StockReasons.Damaged, null)));

        var item = await StockOfAsync();

        (item.Available, item.Quarantined, item.StockValue).ShouldBe((6, 4, 100m));
        (await StockOfAsync(StockFilter.HasQuarantined)).ProductId.ShouldBe(ProductId);
    }

    [Fact]
    public async Task StockList_ShowsProductsWithoutAnyBatchAsEmptyStock()
    {
        var item = await StockOfAsync();

        (item.Available, item.Quarantined, item.Expired, item.NearExpiry, item.StockValue).ShouldBe((0, 0, 0, 0, 0m));
        (await StockOfAsync(StockFilter.OutOfStock)).ProductId.ShouldBe(ProductId);
    }

    [Fact]
    public async Task TheNearExpiryThreshold_ComesFromTheSettings_AndAProductCanOverrideIt()
    {
        await SeedThreeBatchesAsync();
        (await StockOfAsync()).NearExpiry.ShouldBe(6);

        await Services.InScopeAsync(async s =>
        {
            var settings = s.GetRequiredService<PharmacySettingsService>();
            var edit = (await settings.GetForEditAsync()).Value;
            (await settings.UpdateAsync(edit.Values with { NearExpiryDays = 20 }, edit.RowVersion)).IsSuccess.ShouldBeTrue();
        });
        (await StockOfAsync()).NearExpiry.ShouldBe(0);

        // This product alone is watched for 45 days; the pharmacy-wide setting stays at 20.
        var (productId, stripId, _) = await CreateProductAsync("Strict", tracksExpiry: true, nearExpiryDays: 45);
        await OpeningAsync(8, 3m, "N", Today.AddDays(30), productId, stripId);
        (await StockOfAsync(productId: productId)).NearExpiry.ShouldBe(8);
        (await StockOfAsync()).NearExpiry.ShouldBe(0);
    }

    [Fact]
    public async Task Filters_NarrowTheListToWhatNeedsAttention()
    {
        await SeedThreeBatchesAsync();
        var (lowId, lowStrip, _) = await CreateProductAsync("Low", tracksExpiry: true, minimumStock: 5);
        await OpeningAsync(5, 2m, "L", Today.AddDays(400), lowId, lowStrip);
        var (emptyId, _, _) = await CreateProductAsync("Empty", tracksExpiry: true);

        async Task<int[]> IdsAsync(StockFilter filter) =>
            [.. (await Query(s => s.SearchStockAsync(new StockQuery(null, filter, 0, 50)))).Value.Items.Select(i => i.ProductId).Order()];

        (await IdsAsync(StockFilter.All)).ShouldBe([ProductId, lowId, emptyId]);
        (await IdsAsync(StockFilter.InStock)).ShouldBe([ProductId, lowId]);
        (await IdsAsync(StockFilter.OutOfStock)).ShouldBe([emptyId]);
        (await IdsAsync(StockFilter.LowStock)).ShouldBe([lowId]);
        (await IdsAsync(StockFilter.HasExpired)).ShouldBe([ProductId]);
        (await IdsAsync(StockFilter.NearExpiry)).ShouldBe([ProductId]);
    }

    [Fact]
    public async Task StockList_UsesTheSameArabicAwareSearchAsTheProductList()
    {
        var (arabicId, arabicStrip, _) = await CreateArabicProductAsync();
        await OpeningAsync(3, 2m, "A", Today.AddDays(100), arabicId, arabicStrip);

        var page = (await Query(s => s.SearchStockAsync(new StockQuery("اموكسيسيلين", StockFilter.All, 0, 50)))).Value;

        page.Items.Single().ProductId.ShouldBe(arabicId);
    }

    [Fact]
    public async Task ADeactivatedProduct_StaysListedOnlyWhileItStillHasStock()
    {
        var (productId, stripId, _) = await CreateProductAsync("Retired", tracksExpiry: true);
        var batchId = (await OpeningAsync(5, 2m, "R", Today.AddDays(100), productId, stripId)).Value;
        await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().SetActiveAsync(productId, false));

        (await StockOfAsync(productId: productId)).IsActive.ShouldBeFalse();

        await Inventory(s => s.WriteOffAsync(false, new StockActionRequest(Guid.NewGuid(), batchId, stripId, 5, StockReasons.Other, "retired")));
        var ids = (await Query(s => s.SearchStockAsync(new StockQuery(null, StockFilter.All, 0, 50)))).Value.Items.Select(i => i.ProductId);
        ids.ShouldNotContain(productId);
    }

    [Fact]
    public async Task StockList_Pages()
    {
        for (var i = 0; i < 5; i++)
        {
            await CreateProductAsync($"Item {i}", tracksExpiry: true);
        }

        var page = (await Query(s => s.SearchStockAsync(new StockQuery(null, StockFilter.All, PageIndex: 1, PageSize: 4)))).Value;

        page.TotalCount.ShouldBe(6);
        page.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CostsAndStockValue_AreHiddenFromUsersWithoutTheViewCostPermission()
    {
        await SeedThreeBatchesAsync();
        SignIn(Permissions.InventoryView);

        (await StockOfAsync()).StockValue.ShouldBeNull();
        var detail = (await Query(s => s.GetProductStockAsync(ProductId))).Value;
        detail.Batches.ShouldAllBe(b => b.UnitCost == null);
        var movements = (await Query(s => s.SearchMovementsAsync(new MovementQuery(ProductId, null, null, null, null, 0, 50)))).Value;
        movements.Items.ShouldAllBe(m => m.UnitCost == null);
    }

    [Fact]
    public async Task ProductStock_ListsBatchesInTheOrderTheyWouldBeSold_WithExpiryStatus()
    {
        await OpeningAsync(5, 10m, "FAR", Today.AddDays(400));
        await OpeningAsync(5, 11m, "NEAR", Today.AddDays(30));
        await OpeningAsync(5, 12m, "EXPIRED", Today.AddDays(-1));

        var detail = (await Query(s => s.GetProductStockAsync(ProductId))).Value;

        detail.NearExpiryDays.ShouldBe(90);
        detail.Batches.Select(b => (b.BatchNumber, b.Status, b.UnitCost)).ShouldBe(
        [
            ("EXPIRED", ExpiryStatus.Expired, 12m),
            ("NEAR", ExpiryStatus.NearExpiry, 11m),
            ("FAR", ExpiryStatus.Ok, 10m),
        ]);
    }

    [Fact]
    public async Task MovementHistory_ShowsTheLedgerNewestFirst_WithRunningBalances_AndFilters()
    {
        var batchId = (await OpeningAsync(10, 5m, "B1", Today.AddDays(100))).Value;
        await OpeningAsync(4, 6m, "B2", Today.AddDays(200));
        await Inventory(s => s.AdjustAsync(AdjustmentDirection.Decrease, new StockActionRequest(Guid.NewGuid(), batchId, StripId, 3, StockReasons.Lost, null)));
        await Inventory(s => s.QuarantineAsync(new StockActionRequest(Guid.NewGuid(), batchId, StripId, 2, StockReasons.Damaged, "cracked")));

        var all = (await Query(s => s.SearchMovementsAsync(new MovementQuery(ProductId, null, null, null, null, 0, 50)))).Value;
        var ofBatch = (await Query(s => s.SearchMovementsAsync(new MovementQuery(ProductId, batchId, null, null, null, 0, 50)))).Value;
        var quarantines = (await Query(s => s.SearchMovementsAsync(new MovementQuery(ProductId, null, StockMovementType.QuarantineIn, null, null, 0, 50)))).Value;

        all.TotalCount.ShouldBe(4);
        all.Items.Select(m => m.Type).ShouldBe(
            [StockMovementType.QuarantineIn, StockMovementType.AdjustmentDecrease, StockMovementType.OpeningStock, StockMovementType.OpeningStock]);
        ofBatch.Items.Select(m => (m.AvailableDelta, m.AvailableAfter, m.QuarantineAfter)).ShouldBe([(-2, 5, 2), (-3, 7, 0), (10, 10, 0)]);
        quarantines.Items.Single().Note.ShouldBe("cracked");
        quarantines.Items.Single().BatchNumber.ShouldBe("B1");
    }

    [Fact]
    public async Task Queries_RequireInventoryView()
    {
        SignIn(Permissions.CatalogView);

        (await Query(s => s.SearchStockAsync(new StockQuery(null, StockFilter.All, 0, 10)))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await Query(s => s.GetProductStockAsync(ProductId))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await Query(s => s.SearchMovementsAsync(new MovementQuery(ProductId, null, null, null, null, 0, 10)))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    [Fact]
    public async Task ProductStock_OfAnUnknownProduct_IsNotFound()
    {
        (await Query(s => s.GetProductStockAsync(999))).Error!.Code.ShouldBe(CommonErrors.NotFound);
    }

    private async Task<(int ProductId, int StripId, int BoxId)> CreateArabicProductAsync()
    {
        var details = new ProductDetails("أموكسيسيلين", null, null, null, null, null, GeneralCategory, null, true, null, null);
        ProductUnitData[] units = [new(null, StripMeasure, true, 1, null, null), new(null, BoxMeasure, false, 3, null, null)];
        var id = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().CreateAsync(details, units))).Value;
        var saved = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(id))).Value;
        return (id, saved.Units.Single(u => u.IsBase).Id!.Value, saved.Units.Single(u => !u.IsBase).Id!.Value);
    }
}
