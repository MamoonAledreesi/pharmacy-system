namespace Pharmacy.Application.Reports;

/// <summary>Every report the pharmacy can run. The name is also its address (<c>/reports/{Key}</c>) and its localization key.</summary>
public enum ReportKey
{
    SalesDaily,
    SalesByProduct,
    BelowCostSales,
    SalesReturns,
    PurchasesDaily,
    PurchasesBySupplier,
    PurchaseReturns,
    StockByBatch,
    ExpiryStatus,
    WriteOffsAndAdjustments,
    CustomerBalances,
    SupplierBalances,
    Collections,
    CustomerStatement,
    SupplierStatement,
    ExpensesByCategory,
    ExpensesDetail,
    CashShifts,
    Profit,
}

public enum ReportGroup
{
    Sales,
    Purchases,
    Inventory,
    Accounts,
    Finance,
}

/// <summary>Which filters a report offers. The page shows only these.</summary>
[Flags]
public enum ReportFilters
{
    None = 0,
    Period = 1,
    Customer = 2,
    Supplier = 4,
    ExpenseCategory = 8,
    Text = 16,
}

/// <summary>
/// What a report is and who may run it. <c>NeedsCostAndProfit</c> reports show costs or profit, so they need
/// Reports.ViewCostAndProfit on top of Reports.View.
/// </summary>
public sealed record ReportDefinition(ReportKey Key, ReportGroup Group, ReportFilters Filters, bool NeedsCostAndProfit);

public static class ReportDefinitions
{
    public static IReadOnlyList<ReportDefinition> All { get; } =
    [
        new(ReportKey.SalesDaily, ReportGroup.Sales, ReportFilters.Period, false),
        new(ReportKey.SalesByProduct, ReportGroup.Sales, ReportFilters.Period, false),
        new(ReportKey.BelowCostSales, ReportGroup.Sales, ReportFilters.Period, true),
        new(ReportKey.SalesReturns, ReportGroup.Sales, ReportFilters.Period, false),
        new(ReportKey.PurchasesDaily, ReportGroup.Purchases, ReportFilters.Period, false),
        new(ReportKey.PurchasesBySupplier, ReportGroup.Purchases, ReportFilters.Period, false),
        new(ReportKey.PurchaseReturns, ReportGroup.Purchases, ReportFilters.Period, false),
        new(ReportKey.StockByBatch, ReportGroup.Inventory, ReportFilters.Text, false),
        new(ReportKey.ExpiryStatus, ReportGroup.Inventory, ReportFilters.Text, false),
        new(ReportKey.WriteOffsAndAdjustments, ReportGroup.Inventory, ReportFilters.Period, false),
        new(ReportKey.CustomerBalances, ReportGroup.Accounts, ReportFilters.None, false),
        new(ReportKey.SupplierBalances, ReportGroup.Accounts, ReportFilters.None, false),
        new(ReportKey.Collections, ReportGroup.Accounts, ReportFilters.Period, false),
        new(ReportKey.CustomerStatement, ReportGroup.Accounts, ReportFilters.Period | ReportFilters.Customer, false),
        new(ReportKey.SupplierStatement, ReportGroup.Accounts, ReportFilters.Period | ReportFilters.Supplier, false),
        new(ReportKey.ExpensesByCategory, ReportGroup.Finance, ReportFilters.Period, false),
        new(ReportKey.ExpensesDetail, ReportGroup.Finance, ReportFilters.Period | ReportFilters.ExpenseCategory, false),
        new(ReportKey.CashShifts, ReportGroup.Finance, ReportFilters.Period, false),
        new(ReportKey.Profit, ReportGroup.Finance, ReportFilters.Period, true),
    ];

    public static ReportDefinition? Find(ReportKey key) => All.FirstOrDefault(d => d.Key == key);
}

/// <summary>What the user asked for. Filters a report does not offer are ignored.</summary>
public sealed record ReportRequest(
    ReportKey Key,
    DateOnly? From = null,
    DateOnly? To = null,
    int? CustomerId = null,
    int? SupplierId = null,
    int? CategoryId = null,
    string? Text = null);

public enum ReportColumnKind
{
    Text,
    Integer,
    Money,
    Percent,
    Date,
    DateTime,
}

public sealed record ReportColumn(string Key, ReportColumnKind Kind);

/// <summary>A filter as shown above a report: a label key and its value (a string, a date, or a localized text).</summary>
public sealed record ReportFilter(string LabelKey, object Value);

/// <summary>A cell that is a localization key, translated when the report is shown or exported.</summary>
public sealed record ReportText(string Key);

/// <summary>A cell that is a name stored in both languages; the Arabic or the English one is chosen when shown.</summary>
public sealed record LocalizedText(string Arabic, string English);

/// <summary>
/// A finished report before translation. Cells hold typed values: <c>string</c>, <c>int</c>, <c>decimal</c>,
/// <see cref="DateOnly"/>, <see cref="DateTime"/> (already local time), <see cref="ReportText"/>,
/// <see cref="LocalizedText"/>, or <c>null</c>. <c>Totals</c> is a row aligned with the columns, when the report adds up.
/// </summary>
public sealed record ReportTable(
    ReportKey Key,
    IReadOnlyList<ReportFilter> Filters,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals);

public sealed record ReportDocumentFilter(string Label, string Value);

public sealed record ReportDocumentColumn(string Header, ReportColumnKind Kind);

/// <summary>
/// A report ready to display or export: every text is already translated and every cell is a typed value
/// (<c>string</c>, <c>int</c>, <c>decimal</c>, <see cref="DateOnly"/>, <see cref="DateTime"/>, or <c>null</c>).
/// </summary>
public sealed record ReportDocument(
    string Title,
    string PharmacyName,
    string GeneratedAt,
    bool IsRightToLeft,
    IReadOnlyList<ReportDocumentFilter> Filters,
    IReadOnlyList<ReportDocumentColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals,
    string PageLabel);

/// <summary>Writes a <see cref="ReportDocument"/> to a file format. The implementations live in Infrastructure.</summary>
public interface IReportExporter
{
    byte[] ToExcel(ReportDocument document);

    byte[] ToPdf(ReportDocument document);
}
