using Pharmacy.Application.Authorization;
using Pharmacy.Application.Sales;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.IntegrationTests.Sales;

public sealed class SalesQueryTests : SalesTestBase
{
    [Fact]
    public async Task Preview_ReportsStockNearExpiryAndBelowCostPerLine_WithoutChangingAnything()
    {
        await StockAsync(5, costPerStrip: 10m, batch: "SOON", expiry: Today.AddDays(30));

        var preview = (await SalesQuery(s => s.PreviewAsync(
            new SalePreviewRequest([Strips(2, 8m), Strips(10, 20m)], null, null)))).Value;

        preview.Lines[0].InStock.ShouldBeTrue();
        preview.Lines[0].HasNearExpiry.ShouldBeTrue();
        preview.Lines[0].BelowCost.ShouldBeTrue();
        // The second line asks for more than what the first left: 3 of the 5 strips are still sellable.
        preview.Lines[1].InStock.ShouldBeFalse();
        preview.Lines[1].SellableBaseQuantity.ShouldBe(3);
        preview.AllInStock.ShouldBeFalse();
        preview.AnyBelowCost.ShouldBeTrue();
        preview.Subtotal.ShouldBe(216m);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(5);
    }

    [Fact]
    public async Task Preview_FlagsADiscountAboveTheLimit_ForASellerWhoMayNotGiveIt()
    {
        await StockAsync(10);

        var seller = (await SalesQuery(s => s.PreviewAsync(new SalePreviewRequest([Strips(10, 10m)], null, 20m)))).Value;
        SignIn(Manager);
        var manager = (await SalesQuery(s => s.PreviewAsync(new SalePreviewRequest([Strips(10, 10m)], null, 20m)))).Value;

        seller.DiscountNeedsApproval.ShouldBeTrue();
        seller.MaxDiscountPercent.ShouldBe(10m);
        seller.Total.ShouldBe(80m);
        manager.DiscountNeedsApproval.ShouldBeFalse();
    }

    [Fact]
    public async Task Preview_RejectsAnInvalidDiscount()
    {
        await StockAsync(10);

        var result = await SalesQuery(s => s.PreviewAsync(new SalePreviewRequest([Strips(1, 10m)], 5m, 5m)));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Search_FiltersByCustomerNumberAndDate_NewestFirst()
    {
        await StockAsync(20);
        await OpenShiftAsync(0m);
        var customer = await CreateCustomerAsync("Sara Mohamed");
        var first = await SellAsync([Strips(1, 20m)], 20m);
        var second = (await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], customerId: customer, paid: 0m)))).Value;

        var all = (await SalesQuery(s => s.SearchAsync(new SaleQuery(null, null, null, null, 0, 25)))).Value;
        var byCustomer = (await SalesQuery(s => s.SearchAsync(new SaleQuery("Sara", null, null, null, 0, 25)))).Value;
        var byNumber = (await SalesQuery(s => s.SearchAsync(new SaleQuery("000001", null, null, null, 0, 25)))).Value;
        var today = (await SalesQuery(s => s.SearchAsync(new SaleQuery(null, null, Today, Today, 0, 25)))).Value;
        var yesterday = (await SalesQuery(s => s.SearchAsync(new SaleQuery(null, null, Today.AddDays(-2), Today.AddDays(-1), 0, 25)))).Value;

        all.Items.Select(i => i.Id).ShouldBe([second, first]);
        byCustomer.Items.Select(i => i.Id).ShouldBe([second]);
        byNumber.Items.Select(i => i.Id).ShouldBe([first]);
        today.TotalCount.ShouldBe(2);
        yesterday.TotalCount.ShouldBe(0);
        all.Items.Last().LineCount.ShouldBe(1);
    }

    [Fact]
    public async Task Details_ShowTheBatchesSold_AndTheCostOnlyToThoseWhoMaySeeIt()
    {
        await StockAsync(10, costPerStrip: 10m, batch: "B7");
        await OpenShiftAsync(0m);
        var id = await SellAsync([Strips(3, 15m)], 45m);

        var withoutCost = (await SalesQuery(s => s.GetAsync(id))).Value;
        SignIn(Manager);
        var withCost = (await SalesQuery(s => s.GetAsync(id))).Value;

        withoutCost.CostVisible.ShouldBeFalse();
        withoutCost.Lines.Single().CostTotal.ShouldBeNull();
        withCost.CostVisible.ShouldBeTrue();
        withCost.Lines.Single().CostTotal.ShouldBe(30m);
        withCost.Lines.Single().Batches.Single().BatchNumber.ShouldBe("B7");
        withCost.Lines.Single().Product.English.ShouldBe("Panadol");
        withCost.Total.ShouldBe(45m);
        withCost.CustomerId.ShouldBe(DefaultCustomerId);
    }

    [Fact]
    public async Task Queries_WithoutPermission_AreForbidden()
    {
        SignIn();

        (await SalesQuery(s => s.SearchAsync(new SaleQuery(null, null, null, null, 0, 25)))).IsFailure.ShouldBeTrue();
        (await SalesQuery(s => s.GetAsync(1))).IsFailure.ShouldBeTrue();
        SignIn(Permissions.SalesView);
        (await SalesQuery(s => s.PreviewAsync(new SalePreviewRequest([Strips(1, 10m)], null, null)))).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Stock_AfterSales_MatchesTheLedger()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        await SellAsync([Strips(3, 15m)], 45m);
        await SellAsync([Strips(2, 15m)], 30m);

        var movements = await MovementsAsync();
        movements.Where(m => m.Type == StockMovementType.Sale).Sum(m => m.AvailableDelta).ShouldBe(-5);
        (await BatchesAsync()).Sum(b => b.QuantityAvailable).ShouldBe(movements.Sum(m => m.AvailableDelta));
    }
}
