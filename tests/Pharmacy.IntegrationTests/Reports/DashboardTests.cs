using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Dashboard;
using Pharmacy.Application.Reports;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class DashboardTests : ReportsTestBase
{
    private Task<DashboardData> DashboardAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<DashboardService>().GetAsync());

    [Fact]
    public async Task TheFigures_AreWorkedOutFromTheData_AndMatchTheProfitReport()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var old = await SellAsync([Strips(5, 10m)], paid: 50m);
        await AgeInvoiceAsync(old, 3);
        var sale = await SellAsync([Strips(10, 10m)], paid: 100m);
        var saleLine = await LineIdAsync(sale);
        (await Returns(s => s.CreateAsync(Return(sale, [Back(saleLine, 2)])))).IsSuccess.ShouldBeTrue();
        var customer = await CreateCustomerAsync();
        (await Sales(s => s.CreateAsync(Sale([Strips(4, 10m)], customer, paid: 10m)))).IsSuccess.ShouldBeTrue();
        await BuyAsync([BoxLine(10, 30m, "B2")], await CreateSupplierAsync(), paidCash: 100m);
        await RecordExpenseAsync(25m);

        var data = await DashboardAsync();
        var profit = await ReportAsync(ReportKey.Profit);

        data.SalesToday.ShouldBe(new SalesFigures(120m, 2));          // 100 + 40 - 20 returned
        data.LastDays!.Count.ShouldBe(DashboardService.Days);
        data.LastDays[^1].ShouldBe(new DailySales(Today, 120m));
        data.LastDays[^4].ShouldBe(new DailySales(Today.AddDays(-3), 50m));
        data.SalesMonth!.NetSales.ShouldBe(data.SalesToday!.NetSales + (Today.Day > 3 ? 50m : 0m));
        data.PurchasesToday.ShouldBe(300m);
        data.GrossProfitToday.ShouldBe(Cell<decimal>(profit, 4, 1));
        data.GrossProfitToday.ShouldBe(60m);                           // 120 net sales - (70 cost - 10 returned)
        data.CustomerDebts.ShouldBe(30m);
        data.SupplierDebts.ShouldBe(200m);
        data.ExpensesMonth.ShouldBe(25m);
        data.Shifts!.OpenShifts.ShouldBe(1);
        data.StockValue.ShouldBe((100 - 5 - 10 + 2 - 4) * 5m + 30 * 10m);
        data.NoOpenShift.ShouldBeFalse();
    }

    [Fact]
    public async Task EachFigure_FollowsItsPermission()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(10, 5m);

        SignIn(Permissions.SalesView);
        var seller = await DashboardAsync();
        SignIn(Permissions.InventoryView);
        var keeper = await DashboardAsync();
        SignIn();
        var nobody = await DashboardAsync();

        seller.SalesToday.ShouldNotBeNull();
        seller.GrossProfitToday.ShouldBeNull();
        seller.StockValue.ShouldBeNull();
        seller.PurchasesToday.ShouldBeNull();
        seller.CustomerDebts.ShouldBeNull();
        seller.LowStock.ShouldBeNull();
        keeper.SalesToday.ShouldBeNull();
        keeper.StockValue.ShouldBeNull();
        keeper.LowStock.ShouldNotBeNull();
        nobody.ShouldBe(new DashboardData(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false));
    }

    [Fact]
    public async Task StockAlerts_CountLowNearExpiryAndExpiredProducts_AndListAtMostFive()
    {
        var near = await StockAsync(10, 5m, "NEAR", Today.AddDays(10));
        var old = await StockAsync(10, 5m, "OLD", InFuture);
        await ExpireBatchAsync(old);
        for (var i = 0; i < 7; i++)
        {
            var (id, strip, _) = await CreateProductAsync($"Low {i}", tracksExpiry: false, minimumStock: 5);
            (await OpeningAsync(2, 1m, null, null, id, strip)).IsSuccess.ShouldBeTrue();
        }

        var data = await DashboardAsync();

        near.ShouldBeGreaterThan(0);
        data.NearExpiry!.Count.ShouldBe(1);
        data.Expired!.Count.ShouldBe(1);
        data.LowStock!.Count.ShouldBe(7);
        data.LowStock.Items.Count.ShouldBe(DashboardService.AlertItems);
    }

    [Fact]
    public async Task TheNoShiftAlert_ShowsOnlyToWhoCanOpenOne_AndOnlyWhileNoneIsOpen()
    {
        var withoutShift = await DashboardAsync();
        await OpenShiftAsync(100m);
        var withShift = await DashboardAsync();
        SignIn(Permissions.CashView);
        var viewer = await DashboardAsync();

        withoutShift.NoOpenShift.ShouldBeTrue();
        withShift.NoOpenShift.ShouldBeFalse();
        withShift.Shifts.ShouldBe(new OpenShiftsSummary(1, 100m));
        viewer.NoOpenShift.ShouldBeFalse();
        viewer.Shifts.ShouldNotBeNull();
    }
}
