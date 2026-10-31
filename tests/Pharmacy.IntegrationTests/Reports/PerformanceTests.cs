using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Dashboard;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Reconciliation;
using Pharmacy.Application.Reports;
using Xunit.Abstractions;

namespace Pharmacy.IntegrationTests.Reports;

/// <summary>
/// Timings on a database with realistic volume. It takes minutes to fill, so it runs only when PHARMACY_PERF=1
/// (optionally PHARMACY_PERF_PRODUCTS, default 1500). The numbers are written to the output and to perf-results.txt.
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output) : ReportsTestBase
{
    private static readonly bool _enabled = Environment.GetEnvironmentVariable("PHARMACY_PERF") == "1";

    private static int Products => int.TryParse(Environment.GetEnvironmentVariable("PHARMACY_PERF_PRODUCTS"), out var n) ? n : 1500;

    private readonly List<string> _lines = [];

    private async Task<double> TimeAsync(string name, int runs, Func<Task> action)
    {
        await action();   // warm up the plan cache
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < runs; i++)
        {
            await action();
        }

        var average = watch.Elapsed.TotalMilliseconds / runs;
        var line = $"{name}: {average:F0} ms (average of {runs})";
        _lines.Add(line);
        output.WriteLine(line);
        return average;
    }

    [Fact]
    public async Task AnImportOfThreeThousandProducts_FinishesInReasonableTime()
    {
        if (!_enabled)
        {
            return;
        }

        var future = DateOnly.FromDateTime(DateTime.Now).AddDays(400);
        var rows = Enumerable.Range(0, 3000).Select(i => new Pharmacy.Application.Import.ProductImportRow(
            i + 2, $"دواء {i}", $"Drug {i}", null, $"{i} mg", null, "General medicines", null, "Strip", "Box", 4, 5m, $"B{i:D8}", null, null, true,
            $"L{i}", future, 10 + i % 50, "Box", 20m, [])).ToArray();

        var watch = Stopwatch.StartNew();
        var preview = await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Import.ProductImportService>().PreviewAsync(rows));
        var previewMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var result = await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Import.ProductImportService>().ImportAsync(rows, Guid.NewGuid(), false));
        var importMs = watch.Elapsed.TotalMilliseconds;

        output.WriteLine($"Import preview of 3000 rows: {previewMs:F0} ms; import: {importMs:F0} ms");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "perf-import.txt"), [$"Import preview of 3000 rows: {previewMs:F0} ms", $"Import of 3000 products with a batch each: {importMs:F0} ms"]);
        preview.Value.ErrorCount.ShouldBe(0);
        result.Value.ProductsCreated.ShouldBe(3000);
        previewMs.ShouldBeLessThan(10_000);
        importMs.ShouldBeLessThan(240_000);
    }

    [Fact]
    public async Task TheMainOperations_StayFastOnARealisticVolume()
    {
        if (!_enabled)
        {
            return;
        }

        await OpenShiftAsync(100_000m);
        var batchId = await StockAsync(1_000_000, 5m, "BIG");
        var fill = Stopwatch.StartNew();
        for (var i = 0; i < Products; i++)
        {
            var (id, strip, _) = await CreateProductAsync($"Drug {i:D5} Tablets", tracksExpiry: true, minimumStock: 10);
            (await OpeningAsync(50, 2m, $"L{i}", InFuture, id, strip)).IsSuccess.ShouldBeTrue();
        }

        var sales = 0;
        for (; sales < Products / 2; sales++)
        {
            await SellAsync([Strips(1 + sales % 5, 12m)], paid: (1 + sales % 5) * 12m);
        }

        _lines.Add($"Volume: {Products + 1} products, {Products + 1} batches, {sales} sales invoices (filled in {fill.Elapsed.TotalSeconds:F0} s)");

        var search = await TimeAsync("Product search by name", 20, async () =>
            (await Services.InScopeAsync(s => s.GetRequiredService<ProductQueryService>().QuickSearchAsync("Drug 007"))).IsSuccess.ShouldBeTrue());
        var stock = await TimeAsync("Stock list (first page, filtered)", 10, async () =>
            (await Query(s => s.SearchStockAsync(new StockQuery("Drug", StockFilter.LowStock, 0, 25)))).IsSuccess.ShouldBeTrue());
        var sale = await TimeAsync("Post one sale", 10, async () => await SellAsync([Strips(1, 12m)], paid: 12m));
        var dashboard = await TimeAsync("Dashboard", 5, async () => await Services.InScopeAsync(s => s.GetRequiredService<DashboardService>().GetAsync()));
        var profit = await TimeAsync("Profit report (this month)", 5, async () => (await ReportAsync(ReportKey.Profit)).Rows.Count.ShouldBeGreaterThan(0));
        var daily = await TimeAsync("Daily sales report", 5, async () => (await ReportAsync(ReportKey.SalesDaily)).Rows.Count.ShouldBeGreaterThan(0));
        var byProduct = await TimeAsync("Sales by product report", 5, async () => (await ReportAsync(ReportKey.SalesByProduct)).Rows.Count.ShouldBeGreaterThan(0));
        var batches = await TimeAsync("Stock by batch report", 3, async () => (await ReportAsync(ReportKey.StockByBatch)).Rows.Count.ShouldBeGreaterThan(0));
        var reconcile = await TimeAsync("Reconciliation", 3, async () =>
            (await Services.InScopeAsync(s => s.GetRequiredService<ReconciliationService>().RunAsync())).Value.Passed.ShouldBeTrue());

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "perf-results.txt"), _lines);
        batchId.ShouldBeGreaterThan(0);
        search.ShouldBeLessThan(300);
        stock.ShouldBeLessThan(500);
        sale.ShouldBeLessThan(1000);
        dashboard.ShouldBeLessThan(2000);
        new[] { profit, daily, byProduct, batches }.ShouldAllBe(ms => ms < 3000);
        reconcile.ShouldBeLessThan(10_000);
    }
}
