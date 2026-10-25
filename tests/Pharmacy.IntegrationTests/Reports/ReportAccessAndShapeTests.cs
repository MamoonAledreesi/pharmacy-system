using System.Xml.Linq;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Reports;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class ReportAccessAndShapeTests : ReportsTestBase
{
    private static readonly ReportKey[] _costReports = [ReportKey.BelowCostSales, ReportKey.Profit];

    [Fact]
    public async Task WithoutReportsView_NoReportRuns()
    {
        SignIn(Permissions.SalesView, Permissions.ReportsExport, Permissions.ReportsViewCostAndProfit);

        foreach (var definition in ReportDefinitions.All)
        {
            var result = await RunReportAsync(definition.Key);

            result.Error!.Code.ShouldBe(CommonErrors.Forbidden, definition.Key.ToString());
        }
    }

    [Fact]
    public async Task CostAndProfitReports_NeedTheirOwnPermission()
    {
        SignIn(Permissions.ReportsView);

        foreach (var key in _costReports)
        {
            (await RunReportAsync(key)).Error!.Code.ShouldBe(CommonErrors.Forbidden, key.ToString());
        }

        (await RunReportAsync(ReportKey.SalesDaily)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Exporting_NeedsReportsExport_OnTopOfRunning()
    {
        SignIn(Permissions.ReportsView);
        var withoutExport = await ExportReportAsync(ReportKey.SalesDaily);
        SignIn(Permissions.ReportsView, Permissions.ReportsExport);
        var withExport = await ExportReportAsync(ReportKey.SalesDaily);
        var costWithoutCostPermission = await ExportReportAsync(ReportKey.Profit);

        withoutExport.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        withExport.IsSuccess.ShouldBeTrue();
        costWithoutCostPermission.Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    [Fact]
    public async Task AnUnknownReport_IsNotFound()
    {
        var result = await RunReportAsync((ReportKey)999);

        result.Error!.Code.ShouldBe(CommonErrors.NotFound);
    }

    [Fact]
    public async Task EveryReport_ReturnsAlignedRows_AndEveryTextHasATranslation()
    {
        await OpenShiftAsync(5000m);
        var batch = await StockAsync(100, 5m);
        await ExpireBatchAsync(batch);
        await StockAsync(50, 5m, "B2", Today.AddDays(20));
        var customer = await CreateCustomerAsync();
        var supplier = await CreateSupplierAsync();
        await BuyAsync([BoxLine(2, 30m, "B3")], supplier, paidCash: 10m);
        (await Sales(s => s.CreateAsync(Sale([Strips(5, 10m)], customer, paid: 10m)))).IsSuccess.ShouldBeTrue();
        await RecordExpenseAsync(10m);
        (await WriteOffAsync((await BatchesAsync()).Last().Id, 1)).IsSuccess.ShouldBeTrue();

        var english = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Pharmacy.Web", "Resources", "SharedResource.en.resx"))
            .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var definition in ReportDefinitions.All)
        {
            var table = await ReportAsync(definition.Key, customerId: customer, supplierId: supplier);

            table.Rows.ShouldAllBe(row => row.Count == table.Columns.Count, definition.Key.ToString());
            (table.Totals is null || table.Totals.Count == table.Columns.Count).ShouldBeTrue(definition.Key.ToString());
            missing.AddRange(new[] { $"Reports.Title.{definition.Key}", $"Reports.Description.{definition.Key}", $"Reports.Group.{definition.Group}" }
                .Concat(table.Columns.Select(c => c.Key))
                .Concat(table.Filters.Select(f => f.LabelKey))
                .Concat(table.Rows.Concat(table.Totals is null ? [] : [table.Totals]).SelectMany(r => r).OfType<ReportText>().Select(t => t.Key))
                .Where(key => !english.Contains(key)));
        }

        missing.Distinct().ShouldBeEmpty("report texts without a translation");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PharmacySystem.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
