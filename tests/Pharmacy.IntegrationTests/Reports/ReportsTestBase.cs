using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Expenses;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Reports;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.IntegrationTests.Sales;

namespace Pharmacy.IntegrationTests.Reports;

/// <summary>
/// The sales test database (one expiry-tracked product: strip, box of 3 strips; customers; a shift) plus a signed-in user
/// who may record sales, returns, purchases, expenses and stock actions and run, cost-view and export every report.
/// </summary>
public abstract class ReportsTestBase : ReturnsTestBase
{
    protected const int RentCategory = 1;

    protected static readonly string[] ReportReader =
    [
        .. Returner, .. Buyer,
        Permissions.ReportsView, Permissions.ReportsViewCostAndProfit, Permissions.ReportsExport,
        Permissions.InventoryAdjust, Permissions.InventoryWriteOff,
        Permissions.ExpensesView, Permissions.ExpensesCreate, Permissions.ExpensesReverse,
        Permissions.SuppliersView, Permissions.SuppliersManage, Permissions.PurchasingView, Permissions.PurchasingCreate, Permissions.PurchasingReturn,
        Permissions.SalesGrantHighDiscount, Permissions.SalesSellBelowCost, Permissions.SalesViewCost, Permissions.ReconciliationRun,
    ];

    protected override string[] InitialPermissions => ReportReader;

    protected Task<Result<ReportTable>> RunReportAsync(
        ReportKey key, DateOnly? from = null, DateOnly? to = null, int? customerId = null, int? supplierId = null, int? categoryId = null, string? text = null) =>
        Services.InScopeAsync(s => s.GetRequiredService<ReportService>()
            .RunAsync(new ReportRequest(key, from ?? Today, to ?? Today, customerId, supplierId, categoryId, text)));

    protected async Task<ReportTable> ReportAsync(
        ReportKey key, DateOnly? from = null, DateOnly? to = null, int? customerId = null, int? supplierId = null, int? categoryId = null, string? text = null)
    {
        var result = await RunReportAsync(key, from, to, customerId, supplierId, categoryId, text);
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected Task<Result<ReportTable>> ExportReportAsync(ReportKey key) =>
        Services.InScopeAsync(s => s.GetRequiredService<ReportService>().RunForExportAsync(new ReportRequest(key, Today, Today)));

    protected async Task<int> RecordExpenseAsync(decimal amount, int category = RentCategory, DateOnly? date = null, string description = "monthly rent")
    {
        var result = await Services.InScopeAsync(s => s.GetRequiredService<ExpenseService>()
            .RecordAsync(new RecordExpenseRequest(Guid.NewGuid(), category, amount, date ?? Today, description, null)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected Task<Result> WriteOffAsync(int batchId, int strips, string reason = StockReasons.Damaged) =>
        Inventory(s => s.WriteOffAsync(false, new StockActionRequest(Guid.NewGuid(), batchId, StripId, strips, reason, null)));

    protected Task<Result> AdjustAsync(AdjustmentDirection direction, int batchId, int strips) =>
        Inventory(s => s.AdjustAsync(direction, new StockActionRequest(Guid.NewGuid(), batchId, StripId, strips, StockReasons.CountDifference, null)));

    protected static T Cell<T>(ReportTable table, int row, int column) => (T)table.Rows[row][column]!;

    protected static string[] ColumnNames(ReportTable table) => [.. table.Columns.Select(c => c.Key)];
}
