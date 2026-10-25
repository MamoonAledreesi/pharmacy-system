using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Expenses;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Reports;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class ProfitReportTests : ReportsTestBase
{
    private static decimal Amount(ReportTable table, int row) => Cell<decimal>(table, row, 1);

    private static decimal? Share(ReportTable table, int row) => (decimal?)table.Rows[row][2];

    [Fact]
    public async Task AFullScenario_IsComputedLineByLine_AndPurchasesAndReversedExpensesDoNotMatter()
    {
        await OpenShiftAsync(5000m);
        var batch = await StockAsync(100, 5m);

        // One sale of 80 and one of 72 (an 8 discount), 50 of cost each; two strips of the second come back (7.20 each, 10 of cost).
        var invoice = await SellAsync([Strips(10, 8m)], paid: 80m);
        var discounted = await Sales(s => s.CreateAsync(Sale([Strips(10, 8m)], paid: 72m, discountAmount: 8m)));
        discounted.IsSuccess.ShouldBeTrue(discounted.Error?.Code);
        var discountedLine = await LineIdAsync(discounted.Value);
        (await Returns(s => s.CreateAsync(Return(discounted.Value, [Back(discountedLine, 2)])))).IsSuccess.ShouldBeTrue();

        await RecordExpenseAsync(20m);
        var reversed = await RecordExpenseAsync(500m);
        (await Services.InScopeAsync(s => s.GetRequiredService<ExpenseService>()
            .ReverseAsync(new ReverseExpenseRequest(Guid.NewGuid(), reversed, "typo")))).IsSuccess.ShouldBeTrue();

        (await WriteOffAsync(batch, 3)).IsSuccess.ShouldBeTrue();
        (await AdjustAsync(AdjustmentDirection.Decrease, batch, 2)).IsSuccess.ShouldBeTrue();
        (await AdjustAsync(AdjustmentDirection.Increase, batch, 1)).IsSuccess.ShouldBeTrue();

        // Buying stock (cash and credit) and returning it never changes profit.
        await BuyAsync([BoxLine(10, 30m, "B2")], await CreateSupplierAsync(), paidCash: 100m);

        var profit = await ReportAsync(ReportKey.Profit);

        invoice.ShouldBeGreaterThan(0);
        Amount(profit, 0).ShouldBe(152m);       // sales: 80 + 72
        Amount(profit, 1).ShouldBe(-14.4m);     // returns
        Amount(profit, 2).ShouldBe(137.6m);     // net sales
        Amount(profit, 3).ShouldBe(-90m);       // cost: 50 + 50 - 10
        Amount(profit, 4).ShouldBe(47.6m);      // gross profit
        Amount(profit, 5).ShouldBe(-20m);       // expenses (the reversed 500 is left out)
        Amount(profit, 6).ShouldBe(-15m);       // write-off: 3 strips at 5
        Amount(profit, 7).ShouldBe(-5m);        // adjustments: 2 lost (10) minus 1 found (5)
        Amount(profit, 8).ShouldBe(7.6m);       // operating profit
        Share(profit, 2).ShouldBe(100m);
        Share(profit, 4).ShouldBe(34.6m);       // 47.6 / 137.6
        profit.Totals.ShouldBeNull();
    }

    [Fact]
    public async Task OnlyTheDaysInThePeriodCount_AndAnEmptyPeriodIsAllZero()
    {
        await OpenShiftAsync(1000m);
        await StockAsync(50, 5m);
        var old = await SellAsync([Strips(5, 10m)], paid: 50m);
        await AgeInvoiceAsync(old, 3);
        await SellAsync([Strips(2, 10m)], paid: 20m);

        var today = await ReportAsync(ReportKey.Profit, Today, Today);
        var week = await ReportAsync(ReportKey.Profit, Today.AddDays(-7), Today);
        var future = await ReportAsync(ReportKey.Profit, Today.AddDays(10), Today.AddDays(12));

        Amount(today, 0).ShouldBe(20m);
        Amount(today, 4).ShouldBe(10m);   // 20 - 2 strips at 5
        Amount(week, 0).ShouldBe(70m);
        Amount(week, 4).ShouldBe(35m);
        future.Rows.ShouldAllBe(row => (decimal)row[1]! == 0m);
        Share(future, 0).ShouldBeNull();
    }

    [Fact]
    public async Task APeriodThatEndsBeforeItStarts_IsRejected()
    {
        var result = await RunReportAsync(ReportKey.Profit, Today, Today.AddDays(-1));

        result.Error!.Code.ShouldBe(ReportErrors.InvalidPeriod);
    }
}
