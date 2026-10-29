using MudBlazor;
using Pharmacy.Application.Reports;

namespace Pharmacy.Web.Reports;

/// <summary>The icon and color tone each report and report group use on the reports pages.</summary>
public static class ReportStyle
{
    public static string IconOf(ReportKey key) => key switch
    {
        ReportKey.SalesDaily or ReportKey.PurchasesDaily => Icons.Material.Filled.CalendarToday,
        ReportKey.SalesByProduct => Icons.Material.Filled.Medication,
        ReportKey.BelowCostSales => Icons.Material.Filled.MoneyOff,
        ReportKey.SalesReturns => Icons.Material.Filled.AssignmentReturn,
        ReportKey.PurchasesBySupplier => Icons.Material.Filled.LocalShipping,
        ReportKey.PurchaseReturns => Icons.Material.Filled.Undo,
        ReportKey.StockByBatch => Icons.Material.Filled.Inventory2,
        ReportKey.ExpiryStatus => Icons.Material.Filled.EventBusy,
        ReportKey.WriteOffsAndAdjustments => Icons.Material.Filled.DeleteSweep,
        ReportKey.CustomerBalances => Icons.Material.Filled.People,
        ReportKey.SupplierBalances => Icons.Material.Filled.Business,
        ReportKey.Collections => Icons.Material.Filled.Payments,
        ReportKey.CustomerStatement or ReportKey.SupplierStatement => Icons.Material.Filled.ReceiptLong,
        ReportKey.ExpensesByCategory => Icons.Material.Filled.PieChart,
        ReportKey.ExpensesDetail => Icons.Material.Filled.Receipt,
        ReportKey.CashShifts => Icons.Material.Filled.AccountBalanceWallet,
        ReportKey.Profit => Icons.Material.Filled.Savings,
        _ => Icons.Material.Filled.Assessment,
    };

    public static string IconOf(ReportGroup group) => group switch
    {
        ReportGroup.Sales => Icons.Material.Filled.PointOfSale,
        ReportGroup.Purchases => Icons.Material.Filled.ShoppingCart,
        ReportGroup.Inventory => Icons.Material.Filled.Inventory,
        ReportGroup.Accounts => Icons.Material.Filled.AccountBalance,
        _ => Icons.Material.Filled.Paid,
    };

    /// <summary>The CSS tone class (see app.css) that colors a group's icons.</summary>
    public static string ToneOf(ReportGroup group) => group switch
    {
        ReportGroup.Sales => "tone-primary",
        ReportGroup.Purchases => "tone-info",
        ReportGroup.Inventory => "tone-success",
        ReportGroup.Accounts => "tone-warning",
        _ => "tone-secondary",
    };
}
