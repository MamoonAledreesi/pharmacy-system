using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reports;

public static class ReportErrors
{
    public const string InvalidPeriod = "Reports.Error.InvalidPeriod";
    public const string TooLarge = "Reports.Error.TooLarge";
    public const string CustomerRequired = "Reports.Error.CustomerRequired";
    public const string SupplierRequired = "Reports.Error.SupplierRequired";
}

/// <summary>
/// Runs the reports. Every report is a read-only query that ends in a <see cref="ReportTable"/>, so the screen and
/// both export formats share one result. Needs Reports.View; reports that show cost or profit also need
/// Reports.ViewCostAndProfit, and exporting needs Reports.Export.
/// </summary>
public sealed partial class ReportService(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock, ProfitCalculator profit)
{
    /// <summary>A report with more rows than this is refused; the user is asked to narrow the period.</summary>
    public const int MaxRows = 50_000;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Runs a report for display.</summary>
    public async Task<Result<ReportTable>> RunAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReportDefinitions.Find(request.Key) is not { } definition)
        {
            return Result.Failure<ReportTable>(new Error(CommonErrors.NotFound));
        }

        if (!currentUser.HasPermission(Permissions.ReportsView)
            || (definition.NeedsCostAndProfit && !currentUser.HasPermission(Permissions.ReportsViewCostAndProfit)))
        {
            return Result.Failure<ReportTable>(_forbidden);
        }

        var period = ResolvePeriod(request);
        if (period is null)
        {
            return Result.Failure<ReportTable>(new Error(ReportErrors.InvalidPeriod));
        }

        return request.Key switch
        {
            ReportKey.SalesDaily => await SalesDailyAsync(period, cancellationToken),
            ReportKey.SalesByProduct => await SalesByProductAsync(period, cancellationToken),
            ReportKey.BelowCostSales => await BelowCostSalesAsync(period, cancellationToken),
            ReportKey.SalesReturns => await SalesReturnsAsync(period, cancellationToken),
            ReportKey.PurchasesDaily => await PurchasesDailyAsync(period, cancellationToken),
            ReportKey.PurchasesBySupplier => await PurchasesBySupplierAsync(period, cancellationToken),
            ReportKey.PurchaseReturns => await PurchaseReturnsAsync(period, cancellationToken),
            ReportKey.StockByBatch => await StockByBatchAsync(request.Text, cancellationToken),
            ReportKey.ExpiryStatus => await ExpiryStatusAsync(request.Text, cancellationToken),
            ReportKey.WriteOffsAndAdjustments => await WriteOffsAndAdjustmentsAsync(period, cancellationToken),
            ReportKey.CustomerBalances => await CustomerBalancesAsync(cancellationToken),
            ReportKey.SupplierBalances => await SupplierBalancesAsync(cancellationToken),
            ReportKey.Collections => await CollectionsAsync(period, cancellationToken),
            ReportKey.CustomerStatement => await CustomerStatementAsync(period, request.CustomerId, cancellationToken),
            ReportKey.SupplierStatement => await SupplierStatementAsync(period, request.SupplierId, cancellationToken),
            ReportKey.ExpensesByCategory => await ExpensesByCategoryAsync(period, cancellationToken),
            ReportKey.ExpensesDetail => await ExpensesDetailAsync(period, request.CategoryId, cancellationToken),
            ReportKey.CashShifts => await CashShiftsAsync(period, cancellationToken),
            ReportKey.Profit => await ProfitAsync(period, cancellationToken),
            _ => Result.Failure<ReportTable>(new Error(CommonErrors.NotFound)),
        };
    }

    /// <summary>Runs a report to be written to a file; the user must also be allowed to export.</summary>
    public Task<Result<ReportTable>> RunForExportAsync(ReportRequest request, CancellationToken cancellationToken = default) =>
        currentUser.HasPermission(Permissions.ReportsExport)
            ? RunAsync(request, cancellationToken)
            : Task.FromResult(Result.Failure<ReportTable>(_forbidden));

    /// <summary>The period a request asks for; the current month up to today when it names none. Null when it is backwards.</summary>
    private ReportPeriod? ResolvePeriod(ReportRequest request)
    {
        var today = Today;
        var to = request.To ?? today;
        var from = request.From ?? new DateOnly(to.Year, to.Month, 1);
        return from > to ? null : new ReportPeriod(from, to, LocalDayStartUtc(from), LocalDayStartUtc(to.AddDays(1)));
    }

    // The start of a local calendar day expressed in UTC, so periods follow the pharmacy's clock.
    private DateTime LocalDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.LocalTimeZone);

    private DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone);

    private DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(Local(utc));

    private static List<ReportFilter> PeriodFilters(ReportPeriod period) =>
    [
        new("Reports.Filter.From", period.From),
        new("Reports.Filter.To", period.To),
    ];

    private static ReportColumn Col(string name, ReportColumnKind kind = ReportColumnKind.Text) => new($"Reports.Col.{name}", kind);

    private static LocalizedText ProductName(string? arabic, string? english) =>
        new(arabic ?? english ?? string.Empty, english ?? arabic ?? string.Empty);

    private static Result<ReportTable> Done(
        ReportKey key, List<ReportFilter> filters, ReportColumn[] columns, List<IReadOnlyList<object?>> rows, params int[] sumColumns)
    {
        if (rows.Count > MaxRows)
        {
            return Result.Failure<ReportTable>(new Error(ReportErrors.TooLarge));
        }

        return Result.Success(new ReportTable(key, filters, columns, rows, sumColumns.Length == 0 ? null : TotalsOf(columns, rows, sumColumns)));
    }

    // A totals row aligned with the columns: the label first, the sums under the given columns, nothing elsewhere.
    private static object?[] TotalsOf(ReportColumn[] columns, List<IReadOnlyList<object?>> rows, int[] sumColumns)
    {
        var totals = new object?[columns.Length];
        totals[0] = new ReportText("Reports.TotalRow");
        foreach (var index in sumColumns)
        {
            totals[index] = columns[index].Kind == ReportColumnKind.Integer
                ? rows.Sum(r => r[index] is int value ? value : 0)
                : rows.Sum(r => r[index] is decimal value ? value : 0m);
        }

        return totals;
    }

    private sealed record ReportPeriod(DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtc);
}
