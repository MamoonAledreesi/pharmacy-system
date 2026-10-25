using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Expenses;
using Pharmacy.Application.Receivables;
using Pharmacy.Application.Reports;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class InventoryAndAccountReportTests : ReportsTestBase
{
    [Fact]
    public async Task StockByBatch_ListsBatchesWithStock_AndShowsCostOnlyToThoseWhoMaySeeIt()
    {
        await StockAsync(40, 5m, "B1");
        await StockAsync(10, 6m, "B2", InFuture.AddDays(30));

        var full = await ReportAsync(ReportKey.StockByBatch);
        SignIn([.. ReportReader.Where(p => p != Permissions.ReportsViewCostAndProfit)]);
        var noCost = await ReportAsync(ReportKey.StockByBatch);
        var filtered = await ReportAsync(ReportKey.StockByBatch, text: "no such product");

        full.Rows.Select(r => (string)r[1]!).ShouldBe(["B1", "B2"]);
        full.Columns.Select(c => c.Key).ShouldContain("Reports.Col.StockValue");
        Cell<decimal>(full, 0, 8).ShouldBe(200m);
        full.Totals![8].ShouldBe(260m);
        noCost.Columns.Select(c => c.Key).ShouldNotContain("Reports.Col.StockValue");
        noCost.Columns.Select(c => c.Key).ShouldNotContain("Reports.Col.UnitCost");
        noCost.Totals![5].ShouldBe(50);
        filtered.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpiryStatus_ListsExpiredAndNearExpiryBatches_SoonestFirst()
    {
        await StockAsync(10, 5m, "OK", InFuture);
        var near = await StockAsync(10, 5m, "NEAR", Today.AddDays(10));
        var expired = await StockAsync(10, 5m, "OLD", InFuture);
        await ExpireBatchAsync(expired);

        var report = await ReportAsync(ReportKey.ExpiryStatus);

        near.ShouldBeGreaterThan(0);
        report.Rows.Select(r => (string)r[1]!).ShouldBe(["OLD", "NEAR"]);
        ((ReportText)report.Rows[0][3]!).Key.ShouldBe("Expiry.Expired");
        Cell<int>(report, 0, 4).ShouldBe(-1);
        ((ReportText)report.Rows[1][3]!).Key.ShouldBe("Expiry.NearExpiry");
        Cell<int>(report, 1, 4).ShouldBe(10);
    }

    [Fact]
    public async Task WriteOffsAndAdjustments_ShowSignedQuantitiesAndValue()
    {
        var batch = await StockAsync(50, 5m);
        (await WriteOffAsync(batch, 4)).IsSuccess.ShouldBeTrue();
        (await AdjustAsync(Pharmacy.Application.Inventory.AdjustmentDirection.Increase, batch, 2)).IsSuccess.ShouldBeTrue();

        var report = await ReportAsync(ReportKey.WriteOffsAndAdjustments);

        report.Rows.Count.ShouldBe(2);
        ((ReportText)report.Rows[0][1]!).Key.ShouldBe("StockMovementTypes.WriteOff");
        Cell<int>(report, 0, 4).ShouldBe(-4);
        Cell<decimal>(report, 0, 8).ShouldBe(-20m);
        ((ReportText)report.Rows[0][5]!).Key.ShouldBe("StockReasons.Damaged");
        Cell<int>(report, 1, 4).ShouldBe(2);
        report.Totals![4].ShouldBe(-2);
        report.Totals[8].ShouldBe(-10m);
    }

    [Fact]
    public async Task Balances_ListOnlyAccountsThatOweSomething()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var owing = await CreateCustomerAsync("Debtor");
        await CreateCustomerAsync("Settled");
        (await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], owing, paid: 30m)))).IsSuccess.ShouldBeTrue();
        await BuyAsync([BoxLine(10, 30m)], await CreateSupplierAsync("Al-Noor"), paidCash: 100m);

        var customers = await ReportAsync(ReportKey.CustomerBalances);
        var suppliers = await ReportAsync(ReportKey.SupplierBalances);

        customers.Rows.Select(r => (string)r[0]!).ShouldBe(["Debtor"]);
        Cell<decimal>(customers, 0, 2).ShouldBe(70m);
        suppliers.Rows.Select(r => (string)r[0]!).ShouldBe(["Al-Noor"]);
        Cell<decimal>(suppliers, 0, 2).ShouldBe(200m);
    }

    [Fact]
    public async Task CustomerStatement_HasTheBroughtForwardBalanceAndRunningBalance()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var customer = await CreateCustomerAsync();
        (await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], customer, paid: 0m)))).IsSuccess.ShouldBeTrue();
        (await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .CollectAsync(new CollectDebtRequest(Guid.NewGuid(), customer, 40m, null)))).IsSuccess.ShouldBeTrue();

        var today = await ReportAsync(ReportKey.CustomerStatement, customerId: customer);
        var later = await ReportAsync(ReportKey.CustomerStatement, Today.AddDays(1), Today.AddDays(2), customer);

        today.Rows.Count.ShouldBe(3);
        ((ReportText)today.Rows[0][1]!).Key.ShouldBe("Reports.BroughtForward");
        Cell<decimal>(today, 0, 5).ShouldBe(0m);
        ((ReportText)today.Rows[1][1]!).Key.ShouldBe("CustomerEntryTypes.Sale");
        Cell<decimal>(today, 1, 3).ShouldBe(100m);
        today.Rows[1][4].ShouldBeNull();
        Cell<decimal>(today, 2, 4).ShouldBe(40m);
        Cell<decimal>(today, 2, 5).ShouldBe(60m);
        today.Totals![3].ShouldBe(100m);
        today.Totals[4].ShouldBe(40m);
        today.Totals[5].ShouldBe(60m);
        later.Rows.Count.ShouldBe(1);
        Cell<decimal>(later, 0, 5).ShouldBe(60m);
        later.Totals![5].ShouldBe(60m);
    }

    [Fact]
    public async Task Statements_NeedTheirAccountToBeChosen()
    {
        var customer = await RunReportAsync(ReportKey.CustomerStatement);
        var supplier = await RunReportAsync(ReportKey.SupplierStatement);

        customer.Error!.Code.ShouldBe(ReportErrors.CustomerRequired);
        supplier.Error!.Code.ShouldBe(ReportErrors.SupplierRequired);
    }

    [Fact]
    public async Task SupplierStatement_ShowsInvoicesAndPayments()
    {
        await OpenShiftAsync(1000m);
        var supplier = await CreateSupplierAsync("Al-Noor");
        await BuyAsync([BoxLine(10, 30m)], supplier, paidCash: 100m);

        var report = await ReportAsync(ReportKey.SupplierStatement, supplierId: supplier);

        report.Rows.Count.ShouldBe(2);
        ((ReportText)report.Rows[1][1]!).Key.ShouldBe("SupplierEntryTypes.Invoice");
        Cell<decimal>(report, 1, 3).ShouldBe(200m);
        Cell<decimal>(report, 1, 5).ShouldBe(200m);
    }

    [Fact]
    public async Task Collections_ListReversedOnesButDoNotCountThem()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(100, 5m);
        var customer = await CreateCustomerAsync();
        (await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], customer, paid: 0m)))).IsSuccess.ShouldBeTrue();
        var kept = await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .CollectAsync(new CollectDebtRequest(Guid.NewGuid(), customer, 30m, null)));
        var wrong = await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .CollectAsync(new CollectDebtRequest(Guid.NewGuid(), customer, 20m, null)));
        SignIn([.. ReportReader, Permissions.ReceivablesReverse]);
        (await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), wrong.Value, "typo")))).IsSuccess.ShouldBeTrue();

        var report = await ReportAsync(ReportKey.Collections);

        kept.IsSuccess.ShouldBeTrue();
        report.Rows.Count.ShouldBe(2);
        ((ReportText)report.Rows[1][6]!).Key.ShouldBe("Reports.Status.Reversed");
        report.Totals![3].ShouldBe(30m);
    }

    [Fact]
    public async Task Expenses_AreSummedByCategory_AndListedWithReversedOnesMarked()
    {
        await OpenShiftAsync(5000m);
        await RecordExpenseAsync(300m, RentCategory);
        await RecordExpenseAsync(100m, 2, description: "power");
        var wrong = await RecordExpenseAsync(900m, RentCategory);
        (await Services.InScopeAsync(s => s.GetRequiredService<ExpenseService>()
            .ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), wrong, "typo")))).IsSuccess.ShouldBeTrue();

        var byCategory = await ReportAsync(ReportKey.ExpensesByCategory);
        var detail = await ReportAsync(ReportKey.ExpensesDetail);
        var rentOnly = await ReportAsync(ReportKey.ExpensesDetail, categoryId: RentCategory);

        ((LocalizedText)byCategory.Rows[0][0]!).English.ShouldBe("Rent");
        Cell<decimal>(byCategory, 0, 2).ShouldBe(300m);
        Cell<decimal>(byCategory, 0, 3).ShouldBe(75m);
        byCategory.Totals![2].ShouldBe(400m);
        detail.Rows.Count.ShouldBe(3);
        detail.Totals![5].ShouldBe(400m);
        detail.Rows.Count(r => ((ReportText)r[7]!).Key == "Reports.Status.Reversed").ShouldBe(1);
        rentOnly.Rows.Count.ShouldBe(2);
        rentOnly.Filters.Select(f => f.LabelKey).ShouldContain("Reports.Filter.Category");
    }

    [Fact]
    public async Task CashShifts_ShowOpeningExpectedCountedAndDifference()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(10, 5m);
        await SellAsync([Strips(2, 10m)], paid: 20m);

        var report = await ReportAsync(ReportKey.CashShifts);

        report.Rows.Count.ShouldBe(1);
        Cell<decimal>(report, 0, 3).ShouldBe(1000m);
        Cell<decimal>(report, 0, 4).ShouldBe(1020m);
        report.Rows[0][1].ShouldBeNull();
        ((ReportText)report.Rows[0][7]!).Key.ShouldBe("Reports.Status.Open");
    }
}
