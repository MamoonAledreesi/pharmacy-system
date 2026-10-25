namespace Pharmacy.Application.Authorization;

/// <summary>
/// Fine-grained permissions, stored as role claims of type <see cref="ClaimType"/>.
/// Each value is "Module.Action"; the module prefix groups them in the role editor, and
/// "Permissions.{value}" is the localization key of its label.
/// Add a permission here together with the feature that checks it.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AuditLogView = "AuditLog.View";
    public const string CatalogView = "Catalog.View";
    public const string CatalogManage = "Catalog.Manage";
    public const string InventoryView = "Inventory.View";
    public const string InventoryViewCost = "Inventory.ViewCost";
    public const string InventoryOpeningStock = "Inventory.OpeningStock";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryWriteOff = "Inventory.WriteOff";
    public const string CashView = "Cash.View";
    public const string CashOpenShift = "Cash.OpenShift";
    public const string CashManualMovement = "Cash.ManualMovement";
    public const string CashForceClose = "Cash.ForceClose";
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string PurchasingView = "Purchasing.View";
    public const string PurchasingCreate = "Purchasing.Create";
    public const string PurchasingReturn = "Purchasing.Return";
    public const string ExpensesView = "Expenses.View";
    public const string ExpensesCreate = "Expenses.Create";
    public const string ExpensesReverse = "Expenses.Reverse";
    public const string ExpensesManage = "Expenses.Manage";
    public const string ReportsView = "Reports.View";
    public const string ReportsViewCostAndProfit = "Reports.ViewCostAndProfit";
    public const string ReportsExport = "Reports.Export";
    public const string PayablesPay = "Payables.Pay";
    public const string PayablesReverse = "Payables.Reverse";
    public const string PayablesSetOpeningBalance = "Payables.SetOpeningBalance";
    public const string CustomersView = "Customers.View";
    public const string CustomersManage = "Customers.Manage";
    public const string ReceivablesCollect = "Receivables.Collect";
    public const string ReceivablesReverse = "Receivables.Reverse";
    public const string ReceivablesSetOpeningBalance = "Receivables.SetOpeningBalance";
    public const string SalesView = "Sales.View";
    public const string SalesCreate = "Sales.Create";
    public const string SalesViewCost = "Sales.ViewCost";
    public const string SalesGrantHighDiscount = "Sales.GrantHighDiscount";
    public const string SalesSellBelowCost = "Sales.SellBelowCost";
    public const string SalesReturn = "Sales.Return";
    public const string SalesLateReturn = "Sales.LateReturn";

    public static IReadOnlyList<string> All { get; } =
        [
            CatalogView, CatalogManage,
            InventoryView, InventoryViewCost, InventoryOpeningStock, InventoryAdjust, InventoryWriteOff,
            CashView, CashOpenShift, CashManualMovement, CashForceClose,
            SuppliersView, SuppliersManage, PurchasingView, PurchasingCreate, PurchasingReturn,
            ExpensesView, ExpensesCreate, ExpensesReverse, ExpensesManage,
            ReportsView, ReportsViewCostAndProfit, ReportsExport,
            PayablesPay, PayablesReverse, PayablesSetOpeningBalance,
            CustomersView, CustomersManage, ReceivablesCollect, ReceivablesReverse, ReceivablesSetOpeningBalance,
            SalesView, SalesCreate, SalesViewCost, SalesGrantHighDiscount, SalesSellBelowCost, SalesReturn, SalesLateReturn,
            UsersManage, SettingsManage, AuditLogView,
        ];

    public static bool IsDefined(string permission) => All.Contains(permission, StringComparer.Ordinal);

    public static string ModuleOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
