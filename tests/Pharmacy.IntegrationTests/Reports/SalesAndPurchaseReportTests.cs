using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Purchasing;
using Pharmacy.Application.Reports;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class SalesAndPurchaseReportTests : ReportsTestBase
{
    [Fact]
    public async Task DailySales_GroupsByLocalDay_WithReturnsAndTotals()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var yesterday = await SellAsync([Strips(4, 10m)], paid: 40m);
        await AgeInvoiceAsync(yesterday, 1);
        var first = await SellAsync([Strips(10, 10m)], paid: 100m);
        var credit = await CreateCustomerAsync();
        (await Sales(s => s.CreateAsync(Sale([Strips(5, 10m)], credit, paid: 20m)))).IsSuccess.ShouldBeTrue();
        var firstLine = await LineIdAsync(first);
        (await Returns(s => s.CreateAsync(Return(first, [Back(firstLine, 1)])))).IsSuccess.ShouldBeTrue();

        var report = await ReportAsync(ReportKey.SalesDaily, Today.AddDays(-1), Today);

        report.Rows.Count.ShouldBe(2);
        Cell<DateOnly>(report, 0, 0).ShouldBe(Today.AddDays(-1));
        Cell<int>(report, 0, 1).ShouldBe(1);
        Cell<decimal>(report, 0, 4).ShouldBe(40m);
        Cell<DateOnly>(report, 1, 0).ShouldBe(Today);
        Cell<int>(report, 1, 1).ShouldBe(2);
        Cell<decimal>(report, 1, 2).ShouldBe(150m);   // before discount
        Cell<decimal>(report, 1, 4).ShouldBe(150m);
        Cell<decimal>(report, 1, 5).ShouldBe(120m);   // cash
        Cell<decimal>(report, 1, 6).ShouldBe(30m);    // credit
        Cell<decimal>(report, 1, 7).ShouldBe(10m);    // returned
        Cell<decimal>(report, 1, 8).ShouldBe(140m);
        report.Totals![1].ShouldBe(3);
        report.Totals[4].ShouldBe(190m);
        report.Totals[8].ShouldBe(180m);
    }

    [Fact]
    public async Task SalesByProduct_ShowsSoldAndReturnedQuantitiesAndValue()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var sale = await SellAsync([Strips(10, 10m)], paid: 100m);
        var saleLine = await LineIdAsync(sale);
        (await Returns(s => s.CreateAsync(Return(sale, [Back(saleLine, 3)])))).IsSuccess.ShouldBeTrue();

        var report = await ReportAsync(ReportKey.SalesByProduct);

        report.Rows.Count.ShouldBe(1);
        var name = (LocalizedText)report.Rows[0][0]!;
        name.English.ShouldBe("Panadol");
        Cell<int>(report, 0, 1).ShouldBe(10);
        Cell<decimal>(report, 0, 4).ShouldBe(100m);
        Cell<int>(report, 0, 5).ShouldBe(3);
        Cell<decimal>(report, 0, 6).ShouldBe(30m);
        Cell<decimal>(report, 0, 7).ShouldBe(70m);
    }

    [Fact]
    public async Task BelowCostSales_ListsOnlyThoseLines_WithTheLossAndWhoApproved()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        await SellAsync([Strips(2, 10m)], paid: 20m);
        var below = await Sales(s => s.CreateAsync(Sale([Strips(4, 3m)], paid: 12m, belowCostReason: "clearance")));
        below.IsSuccess.ShouldBeTrue(below.Error?.Code);

        var report = await ReportAsync(ReportKey.BelowCostSales);

        report.Rows.Count.ShouldBe(1);
        Cell<int>(report, 0, 3).ShouldBe(4);
        Cell<decimal>(report, 0, 4).ShouldBe(12m);
        Cell<decimal>(report, 0, 5).ShouldBe(20m);
        Cell<decimal>(report, 0, 6).ShouldBe(8m);
        Cell<string>(report, 0, 8).ShouldBe("clearance");
    }

    [Fact]
    public async Task SalesReturns_ShowTheDebtReducedAndTheCashRefunded()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var customer = await CreateCustomerAsync();
        var sale = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], customer, paid: 40m)));
        var saleLine = await LineIdAsync(sale.Value);
        (await Returns(s => s.CreateAsync(Return(sale.Value, [Back(saleLine, 8)], reason: "wrong item")))).IsSuccess.ShouldBeTrue();

        var report = await ReportAsync(ReportKey.SalesReturns);

        report.Rows.Count.ShouldBe(1);
        Cell<string>(report, 0, 3).ShouldBe("Ahmed Ali");
        Cell<decimal>(report, 0, 4).ShouldBe(80m);
        Cell<decimal>(report, 0, 5).ShouldBe(60m);   // the whole credit part
        Cell<decimal>(report, 0, 6).ShouldBe(20m);
        ((ReportText)report.Rows[0][7]!).Key.ShouldBe("Reports.No");
        Cell<string>(report, 0, 8).ShouldBe("wrong item");
    }

    [Fact]
    public async Task Purchases_AreGroupedByDayAndBySupplier()
    {
        await OpenShiftAsync(1000m);
        var noor = await CreateSupplierAsync("Al-Noor");
        var salam = await CreateSupplierAsync("Salam");
        await BuyAsync([BoxLine(10, 30m, "A")], noor, paidCash: 100m);
        await BuyAsync([BoxLine(1, 30m, "B")], noor, paidCash: 30m);
        await BuyAsync([BoxLine(2, 30m, "C")], salam);

        var daily = await ReportAsync(ReportKey.PurchasesDaily);
        var bySupplier = await ReportAsync(ReportKey.PurchasesBySupplier);

        daily.Rows.Count.ShouldBe(1);
        Cell<int>(daily, 0, 1).ShouldBe(3);
        Cell<decimal>(daily, 0, 2).ShouldBe(390m);
        Cell<decimal>(daily, 0, 3).ShouldBe(130m);
        Cell<decimal>(daily, 0, 4).ShouldBe(260m);
        bySupplier.Rows.Select(r => (string)r[0]!).ShouldBe(["Al-Noor", "Salam"]);
        Cell<decimal>(bySupplier, 0, 2).ShouldBe(330m);
        Cell<int>(bySupplier, 0, 1).ShouldBe(2);
        bySupplier.Totals![2].ShouldBe(390m);
    }

    [Fact]
    public async Task PurchaseReturns_ShowWhatWentBackToTheSupplier()
    {
        await OpenShiftAsync(1000m);
        var supplier = await CreateSupplierAsync("Al-Noor");
        var invoice = await BuyAsync([BoxLine(10, 30m, "A")], supplier, paidCash: 100m);
        var line = (await InvoicesAsync()).Single(i => i.Id == invoice).Lines.Single();
        var returned = await Services.InScopeAsync(s => s.GetRequiredService<PurchaseReturnService>().CreateAsync(
            new PurchaseReturnRequest(Guid.NewGuid(), invoice, [new PurchaseReturnLineRequest(line.Id, BoxId, 2, false)], "damaged")));
        returned.IsSuccess.ShouldBeTrue(returned.Error?.Code);

        var report = await ReportAsync(ReportKey.PurchaseReturns);

        report.Rows.Count.ShouldBe(1);
        Cell<string>(report, 0, 3).ShouldBe("Al-Noor");
        Cell<decimal>(report, 0, 4).ShouldBe(60m);
        Cell<decimal>(report, 0, 5).ShouldBe(60m);
        Cell<decimal>(report, 0, 6).ShouldBe(0m);
        Cell<string>(report, 0, 7).ShouldBe("damaged");
    }
}
