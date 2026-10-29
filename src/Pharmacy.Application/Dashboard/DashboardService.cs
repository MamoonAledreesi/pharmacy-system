using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Reports;

namespace Pharmacy.Application.Dashboard;

/// <summary>Net sales (after returns) and the number of invoices of one period.</summary>
public sealed record SalesFigures(decimal NetSales, int Invoices);

/// <summary>Net sales of one local day, for the last-seven-days chart.</summary>
public sealed record DailySales(DateOnly Day, decimal NetSales);

/// <summary>The open cash shifts: how many and the cash they hold together.</summary>
public sealed record OpenShiftsSummary(int OpenShifts, decimal Cash);

/// <summary>A stock alert: how many products it concerns and the first few of them.</summary>
public sealed record StockAlert(int Count, IReadOnlyList<StockListItem> Items);

/// <summary>
/// Everything the dashboard shows. A figure is null when the user may not see it. <c>NoOpenShift</c> is set only for
/// users who can open a shift and none is open.
/// </summary>
public sealed record DashboardData(
    SalesFigures? SalesToday,
    SalesFigures? SalesMonth,
    IReadOnlyList<DailySales>? LastDays,
    decimal? PurchasesToday,
    decimal? PurchasesMonth,
    decimal? GrossProfitToday,
    decimal? GrossProfitMonth,
    decimal? StockValue,
    decimal? CustomerDebts,
    decimal? SupplierDebts,
    OpenShiftsSummary? Shifts,
    decimal? ExpensesMonth,
    StockAlert? LowStock,
    StockAlert? NearExpiry,
    StockAlert? Expired,
    bool NoOpenShift);

/// <summary>
/// The figures and alerts of the home page, worked out when the page is opened. Each figure follows the permission that
/// protects the same data elsewhere, so the dashboard never shows what a user could not open.
/// </summary>
public sealed class DashboardService(
    IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock, ProfitCalculator profit, InventoryQueryService stock)
{
    /// <summary>How many products an alert lists; the page links to the full list.</summary>
    public const int AlertItems = 5;

    /// <summary>How many days the sales chart covers.</summary>
    public const int Days = 7;

    public async Task<DashboardData> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var (todayFrom, tomorrow) = (LocalDayStartUtc(today), LocalDayStartUtc(today.AddDays(1)));
        var monthFrom = LocalDayStartUtc(monthStart);

        SalesFigures? salesToday = null, salesMonth = null;
        if (currentUser.HasPermission(Permissions.SalesView))
        {
            salesToday = await SalesAsync(todayFrom, tomorrow, cancellationToken);
            salesMonth = await SalesAsync(monthFrom, tomorrow, cancellationToken);
        }

        IReadOnlyList<DailySales>? lastDays = null;
        if (salesToday is not null)
        {
            lastDays = await LastDaysAsync(today, cancellationToken);
        }

        decimal? purchasesToday = null, purchasesMonth = null;
        if (currentUser.HasPermission(Permissions.PurchasingView))
        {
            purchasesToday = await PurchasesAsync(today, today, cancellationToken);
            purchasesMonth = await PurchasesAsync(monthStart, today, cancellationToken);
        }

        decimal? grossToday = null, grossMonth = null;
        if (currentUser.HasPermission(Permissions.ReportsViewCostAndProfit))
        {
            grossToday = (await profit.ComputeAsync(todayFrom, tomorrow, today, today, cancellationToken)).GrossProfit;
            grossMonth = (await profit.ComputeAsync(monthFrom, tomorrow, monthStart, today, cancellationToken)).GrossProfit;
        }

        decimal? stockValue = null;
        if (currentUser.HasPermission(Permissions.InventoryViewCost))
        {
            stockValue = await db.Batches.AsNoTracking().SumAsync(b => (decimal?)((b.QuantityAvailable + b.QuantityQuarantined) * b.UnitCost), cancellationToken) ?? 0m;
        }

        decimal? customerDebts = null;
        if (currentUser.HasPermission(Permissions.CustomersView))
        {
            customerDebts = await db.Customers.AsNoTracking().SumAsync(c => (decimal?)c.CurrentBalance, cancellationToken) ?? 0m;
        }

        decimal? supplierDebts = null;
        if (currentUser.HasPermission(Permissions.SuppliersView))
        {
            supplierDebts = await db.Suppliers.AsNoTracking().SumAsync(s => (decimal?)s.CurrentBalance, cancellationToken) ?? 0m;
        }

        OpenShiftsSummary? shifts = null;
        var noOpenShift = false;
        if (currentUser.HasPermission(Permissions.CashView) || currentUser.HasPermission(Permissions.CashOpenShift))
        {
            var open = await db.CashShifts.AsNoTracking().Where(s => s.IsOpen).GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Cash = g.Sum(s => s.CurrentBalance) }).SingleOrDefaultAsync(cancellationToken);
            shifts = currentUser.HasPermission(Permissions.CashView) ? new OpenShiftsSummary(open?.Count ?? 0, open?.Cash ?? 0m) : null;
            noOpenShift = open is null && currentUser.HasPermission(Permissions.CashOpenShift);
        }

        decimal? expensesMonth = null;
        if (currentUser.HasPermission(Permissions.ExpensesView))
        {
            expensesMonth = await db.Expenses.AsNoTracking()
                .Where(e => !e.IsReversed && e.ExpenseDate >= monthStart && e.ExpenseDate <= today)
                .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        }

        StockAlert? low = null, near = null, expired = null;
        if (currentUser.HasPermission(Permissions.InventoryView))
        {
            low = await AlertAsync(StockFilter.LowStock, cancellationToken);
            near = await AlertAsync(StockFilter.NearExpiry, cancellationToken);
            expired = await AlertAsync(StockFilter.HasExpired, cancellationToken);
        }

        return new DashboardData(
            salesToday, salesMonth, lastDays, purchasesToday, purchasesMonth, grossToday, grossMonth, stockValue, customerDebts, supplierDebts,
            shifts, expensesMonth, low, near, expired, noOpenShift);
    }

    // Net of returns: what was invoiced in the period minus what came back in it.
    private async Task<SalesFigures> SalesAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var invoices = db.SalesInvoices.AsNoTracking().Where(i => i.CreatedAtUtc >= fromUtc && i.CreatedAtUtc < toUtc);
        var sold = await invoices.SumAsync(i => (decimal?)i.Total, cancellationToken) ?? 0m;
        var count = await invoices.CountAsync(cancellationToken);
        var returned = await db.SalesReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
            .SumAsync(r => (decimal?)r.Total, cancellationToken) ?? 0m;
        return new SalesFigures(sold - returned, count);
    }

    // The last seven local days, today included, each with net sales (invoiced minus returned); days without sales show zero.
    private async Task<IReadOnlyList<DailySales>> LastDaysAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var first = today.AddDays(-(Days - 1));
        var from = LocalDayStartUtc(first);
        var to = LocalDayStartUtc(today.AddDays(1));
        var invoices = await db.SalesInvoices.AsNoTracking().Where(i => i.CreatedAtUtc >= from && i.CreatedAtUtc < to)
            .Select(i => new { i.CreatedAtUtc, i.Total }).ToListAsync(cancellationToken);
        var returns = await db.SalesReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to)
            .Select(r => new { r.CreatedAtUtc, r.Total }).ToListAsync(cancellationToken);

        DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.LocalTimeZone));
        var sold = invoices.GroupBy(i => DayOf(i.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Sum(i => i.Total));
        var back = returns.GroupBy(r => DayOf(r.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
        return [.. Enumerable.Range(0, Days).Select(n => first.AddDays(n)).Select(day => new DailySales(day, sold.GetValueOrDefault(day) - back.GetValueOrDefault(day)))];
    }

    private async Task<decimal> PurchasesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.PurchaseInvoices.AsNoTracking().Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .SumAsync(i => (decimal?)i.Total, cancellationToken) ?? 0m;

    private async Task<StockAlert> AlertAsync(StockFilter filter, CancellationToken cancellationToken)
    {
        var result = await stock.SearchStockAsync(new StockQuery(null, filter, 0, AlertItems), cancellationToken);
        return result.IsSuccess ? new StockAlert(result.Value.TotalCount, result.Value.Items) : new StockAlert(0, []);
    }

    private DateTime LocalDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.LocalTimeZone);
}
