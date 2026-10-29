using Microsoft.EntityFrameworkCore;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reports;

public sealed partial class ReportService
{
    private async Task<Result<ReportTable>> ExpensesByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var categories = await (
                from e in db.Expenses.AsNoTracking().Where(e => !e.IsReversed && e.ExpenseDate >= period.From && e.ExpenseDate <= period.To)
                join c in db.ExpenseCategories on e.CategoryId equals c.Id
                group e by new { c.Id, c.NameArabic, c.NameEnglish } into g
                orderby g.Sum(x => x.Amount) descending
                select new { g.Key.NameArabic, g.Key.NameEnglish, Count = g.Count(), Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var grandTotal = categories.Sum(c => c.Total);
        var rows = categories
            .Select(c => (IReadOnlyList<object?>)new object?[]
            {
                new LocalizedText(c.NameArabic, c.NameEnglish), c.Count, c.Total, grandTotal == 0 ? 0m : Math.Round(c.Total / grandTotal * 100m, 1),
            })
            .ToList();

        return Done(ReportKey.ExpensesByCategory, PeriodFilters(period),
            [Col("Category"), Col("Count", ReportColumnKind.Integer), Col("Amount", ReportColumnKind.Money), Col("Share", ReportColumnKind.Percent)],
            rows, 1, 2);
    }

    // Reversed expenses are listed but do not count in the total.
    private async Task<Result<ReportTable>> ExpensesDetailAsync(ReportPeriod period, int? categoryId, CancellationToken cancellationToken)
    {
        var expenses = await (
                from e in db.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= period.From && e.ExpenseDate <= period.To)
                where categoryId == null || e.CategoryId == categoryId
                join c in db.ExpenseCategories on e.CategoryId equals c.Id
                orderby e.ExpenseDate, e.Id
                select new { e.Number, e.ExpenseDate, c.NameArabic, c.NameEnglish, e.Description, e.Payee, e.Amount, e.UserName, e.IsReversed })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var filters = PeriodFilters(period);
        if (categoryId is { } id)
        {
            var category = await db.ExpenseCategories.AsNoTracking().Where(c => c.Id == id).Select(c => new LocalizedText(c.NameArabic, c.NameEnglish))
                .SingleOrDefaultAsync(cancellationToken);
            if (category is not null)
            {
                filters.Add(new("Reports.Filter.Category", category));
            }
        }

        var rows = expenses
            .Select(e => (IReadOnlyList<object?>)new object?[]
            {
                e.Number, e.ExpenseDate, new LocalizedText(e.NameArabic, e.NameEnglish), e.Description, e.Payee, e.Amount, e.UserName,
                new ReportText(e.IsReversed ? "Reports.Status.Reversed" : "Reports.Status.Active"),
            })
            .ToList();

        var result = Done(ReportKey.ExpensesDetail, filters,
            [Col("Number"), Col("Date", ReportColumnKind.Date), Col("Category"), Col("Description"), Col("Payee"),
             Col("Amount", ReportColumnKind.Money), Col("User"), Col("Status")],
            rows);
        return result.IsFailure
            ? result
            : Result.Success(result.Value with
            {
                Totals = [new ReportText("Reports.TotalRow"), null, null, null, null, expenses.Where(e => !e.IsReversed).Sum(e => e.Amount), null, null],
            });
    }

    private async Task<Result<ReportTable>> CashShiftsAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var shifts = await db.CashShifts.AsNoTracking()
            .Where(s => s.OpenedAtUtc >= period.FromUtc && s.OpenedAtUtc < period.ToUtc)
            .OrderBy(s => s.OpenedAtUtc).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.OpenedAtUtc, s.ClosedAtUtc, s.OpenedByUserName, s.OpeningBalance, s.CurrentBalance, s.CountedBalance, s.Difference, s.IsOpen, s.WasForceClosed,
            })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = shifts
            .Select(s => (IReadOnlyList<object?>)new object?[]
            {
                Local(s.OpenedAtUtc), s.ClosedAtUtc is { } closed ? Local(closed) : null, s.OpenedByUserName, s.OpeningBalance, s.CurrentBalance,
                s.CountedBalance, s.Difference,
                new ReportText(s.IsOpen ? "Reports.Status.Open" : s.WasForceClosed ? "Reports.Status.ForceClosed" : "Reports.Status.Closed"),
            })
            .ToList();

        return Done(ReportKey.CashShifts, PeriodFilters(period),
            [Col("OpenedAt", ReportColumnKind.DateTime), Col("ClosedAt", ReportColumnKind.DateTime), Col("Cashier"),
             Col("OpeningBalance", ReportColumnKind.Money), Col("ExpectedBalance", ReportColumnKind.Money),
             Col("CountedBalance", ReportColumnKind.Money), Col("Difference", ReportColumnKind.Money), Col("Status")],
            rows, 6);
    }

    private async Task<Result<ReportTable>> ProfitAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var figures = await profit.ComputeAsync(period.FromUtc, period.ToUtc, period.From, period.To, cancellationToken);
        var netSales = figures.NetSales;

        decimal? Share(decimal amount) => netSales == 0 ? null : Math.Round(amount / netSales * 100m, 1);
        IReadOnlyList<object?> Line(string key, decimal amount) => new object?[] { new ReportText($"Reports.Profit.{key}"), amount, Share(amount) };

        List<IReadOnlyList<object?>> rows =
        [
            Line("Sales", figures.Sales),
            Line("SalesReturns", -figures.Returned),
            Line("NetSales", netSales),
            Line("CostOfGoods", -figures.CostOfGoods),
            Line("GrossProfit", figures.GrossProfit),
            Line("Expenses", -figures.Expenses),
            Line("WriteOffLosses", -figures.WriteOffLoss),
            Line("AdjustmentShortfall", -figures.AdjustmentShortfall),
            Line("OperatingProfit", figures.OperatingProfit),
        ];

        return Done(ReportKey.Profit, PeriodFilters(period),
            [Col("Item"), Col("Amount", ReportColumnKind.Money), Col("Share", ReportColumnKind.Percent)], rows);
    }
}
