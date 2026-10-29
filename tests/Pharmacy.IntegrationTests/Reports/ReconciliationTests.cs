using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Reconciliation;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class ReconciliationTests : ReportsTestBase
{
    private Task<Pharmacy.Domain.Common.Result<ReconciliationReport>> RunAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<ReconciliationService>().RunAsync());

    private Task TamperAsync(FormattableString sql) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task BusyDayAsync()
    {
        await OpenShiftAsync(2000m);
        var batch = await StockAsync(100, 5m);
        var customer = await CreateCustomerAsync();
        var sale = await SellAsync([Strips(10, 12m)], paid: 120m);
        (await Sales(s => s.CreateAsync(Sale([Strips(5, 12m)], customer, paid: 20m)))).IsSuccess.ShouldBeTrue();
        var line = await LineIdAsync(sale);
        (await Returns(s => s.CreateAsync(Return(sale, [Back(line, 2)])))).IsSuccess.ShouldBeTrue();
        await BuyAsync([BoxLine(5, 30m, "B2")], await CreateSupplierAsync(), paidCash: 50m);
        await RecordExpenseAsync(30m);
        (await WriteOffAsync(batch, 3)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AfterARealBusyDay_EveryCheckPasses_AndComparesRecords()
    {
        await BusyDayAsync();

        var report = (await RunAsync()).Value;

        report.Passed.ShouldBeTrue(string.Join("; ", report.Checks.Where(c => !c.Passed).Select(c => c.Key)));
        report.Checks.Count.ShouldBe(7);
        report.Checks.Single(c => c.Key == ReconciliationKeys.SalesInvoices).Checked.ShouldBe(2);
        report.Checks.Single(c => c.Key == ReconciliationKeys.PurchaseInvoices).Checked.ShouldBe(1);
        report.Checks.Single(c => c.Key == ReconciliationKeys.BatchAvailable).Checked.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task DataChangedOutsideTheApplication_IsFoundWhereItHappened()
    {
        await BusyDayAsync();
        await TamperAsync($"UPDATE inventory.Batches SET QuantityAvailable = QuantityAvailable + 1 WHERE Id = (SELECT MIN(Id) FROM inventory.Batches)");
        await TamperAsync($"UPDATE receivables.Customers SET CurrentBalance = CurrentBalance + 7 WHERE Name = 'Ahmed Ali'");
        await TamperAsync($"UPDATE cash.CashShifts SET CurrentBalance = CurrentBalance + 1");
        // The database refuses an inconsistent invoice, so the guard is lifted for this one change.
        await TamperAsync($"ALTER TABLE sales.SalesInvoices NOCHECK CONSTRAINT CK_SalesInvoices_Amounts");
        await TamperAsync($"UPDATE sales.SalesInvoices SET Total = Total + 1 WHERE Id = (SELECT MIN(Id) FROM sales.SalesInvoices)");

        var report = (await RunAsync()).Value;

        report.Passed.ShouldBeFalse();
        var failed = report.Checks.Where(c => !c.Passed).Select(c => c.Key).Order().ToList();
        failed.ShouldBe(new[]
        {
            ReconciliationKeys.BatchAvailable, ReconciliationKeys.CashShifts, ReconciliationKeys.CustomerAccounts, ReconciliationKeys.SalesInvoices,
        }.Order());
        var customer = report.Checks.Single(c => c.Key == ReconciliationKeys.CustomerAccounts).Discrepancies.Single();
        customer.Reference.ShouldBe("Ahmed Ali");
        (customer.Actual - customer.Expected).ShouldBe(7m);
        var batch = report.Checks.Single(c => c.Key == ReconciliationKeys.BatchAvailable).Discrepancies.Single();
        (batch.Actual - batch.Expected).ShouldBe(1m);
    }

    [Fact]
    public async Task ItNeedsItsOwnPermission_AndIsReadOnly()
    {
        await BusyDayAsync();
        SignIn(Permissions.ReportsView);
        var denied = await RunAsync();
        SignIn(Permissions.ReconciliationRun);
        var before = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.CountAsync());
        var allowed = await RunAsync();
        var after = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.CountAsync());

        denied.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        allowed.IsSuccess.ShouldBeTrue();
        after.ShouldBe(before);
    }

    [Fact]
    public async Task AnEmptySystem_PassesWithNothingToCompare()
    {
        var report = (await RunAsync()).Value;

        report.Passed.ShouldBeTrue();
        report.Checks.Single(c => c.Key == ReconciliationKeys.SalesInvoices).Checked.ShouldBe(0);
    }
}
