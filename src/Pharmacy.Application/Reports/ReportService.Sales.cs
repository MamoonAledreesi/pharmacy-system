using Microsoft.EntityFrameworkCore;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reports;

public sealed partial class ReportService
{
    private async Task<Result<ReportTable>> SalesDailyAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var invoices = await db.SalesInvoices.AsNoTracking()
            .Where(i => i.CreatedAtUtc >= period.FromUtc && i.CreatedAtUtc < period.ToUtc)
            .Select(i => new { i.CreatedAtUtc, i.Subtotal, i.DiscountAmount, i.Total, i.PaidCash, i.OnCredit })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var returns = await db.SalesReturns.AsNoTracking()
            .Where(r => r.CreatedAtUtc >= period.FromUtc && r.CreatedAtUtc < period.ToUtc)
            .Select(r => new { r.CreatedAtUtc, r.Total })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        if (invoices.Count > MaxRows || returns.Count > MaxRows)
        {
            return Result.Failure<ReportTable>(new Error(ReportErrors.TooLarge));
        }

        var invoicesByDay = invoices.ToLookup(i => LocalDay(i.CreatedAtUtc));
        var returnsByDay = returns.ToLookup(r => LocalDay(r.CreatedAtUtc));
        var rows = invoicesByDay.Select(g => g.Key).Union(returnsByDay.Select(g => g.Key)).Order()
            .Select(day =>
            {
                var sold = invoicesByDay[day].Sum(i => i.Total);
                var returned = returnsByDay[day].Sum(r => r.Total);
                return (IReadOnlyList<object?>)new object?[]
                {
                    day, invoicesByDay[day].Count(), invoicesByDay[day].Sum(i => i.Subtotal), invoicesByDay[day].Sum(i => i.DiscountAmount),
                    sold, invoicesByDay[day].Sum(i => i.PaidCash), invoicesByDay[day].Sum(i => i.OnCredit), returned, sold - returned,
                };
            })
            .ToList();

        return Done(ReportKey.SalesDaily, PeriodFilters(period),
            [Col("Date", ReportColumnKind.Date), Col("Invoices", ReportColumnKind.Integer), Col("Subtotal", ReportColumnKind.Money),
             Col("Discount", ReportColumnKind.Money), Col("Total", ReportColumnKind.Money), Col("PaidCash", ReportColumnKind.Money),
             Col("OnCredit", ReportColumnKind.Money), Col("Returns", ReportColumnKind.Money), Col("NetSales", ReportColumnKind.Money)],
            rows, 1, 2, 3, 4, 5, 6, 7, 8);
    }

    private async Task<Result<ReportTable>> SalesByProductAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var sold = await (
                from i in db.SalesInvoices.AsNoTracking().Where(i => i.CreatedAtUtc >= period.FromUtc && i.CreatedAtUtc < period.ToUtc)
                from l in i.Lines
                group l by l.ProductId into g
                select new
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(l => l.BaseQuantity),
                    Gross = g.Sum(l => l.LineSubtotal),
                    Discount = g.Sum(l => l.DiscountShare),
                    Net = g.Sum(l => l.NetTotal),
                })
            .ToListAsync(cancellationToken);
        var returned = await (
                from r in db.SalesReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= period.FromUtc && r.CreatedAtUtc < period.ToUtc)
                from l in r.Lines
                group l by l.ProductId into g
                select new { ProductId = g.Key, Quantity = g.Sum(l => l.BaseQuantity), Value = g.Sum(l => l.Value) })
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        var productIds = sold.Select(s => s.ProductId).Union(returned.Keys).ToList();
        var names = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var rows = productIds
            .Select(id =>
            {
                var s = sold.FirstOrDefault(x => x.ProductId == id);
                var r = returned.GetValueOrDefault(id);
                var net = (s?.Net ?? 0m) - (r?.Value ?? 0m);
                return (Net: net, Row: (IReadOnlyList<object?>)new object?[]
                {
                    ProductName(names[id].TradeNameArabic, names[id].TradeNameEnglish), s?.Quantity ?? 0, s?.Gross ?? 0m, s?.Discount ?? 0m,
                    s?.Net ?? 0m, r?.Quantity ?? 0, r?.Value ?? 0m, net,
                });
            })
            .OrderByDescending(x => x.Net)
            .Select(x => x.Row)
            .ToList();

        return Done(ReportKey.SalesByProduct, PeriodFilters(period),
            [Col("Product"), Col("QuantitySold", ReportColumnKind.Integer), Col("Subtotal", ReportColumnKind.Money),
             Col("Discount", ReportColumnKind.Money), Col("Total", ReportColumnKind.Money), Col("QuantityReturned", ReportColumnKind.Integer),
             Col("Returns", ReportColumnKind.Money), Col("NetSales", ReportColumnKind.Money)],
            rows, 1, 2, 3, 4, 5, 6, 7);
    }

    private async Task<Result<ReportTable>> BelowCostSalesAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var lines = await (
                from i in db.SalesInvoices.AsNoTracking().Where(i => i.CreatedAtUtc >= period.FromUtc && i.CreatedAtUtc < period.ToUtc)
                from l in i.Lines
                where l.NetTotal < l.CostTotal
                join p in db.Products on l.ProductId equals p.Id
                orderby i.CreatedAtUtc, i.Id
                select new
                {
                    i.Number, i.CreatedAtUtc, p.TradeNameArabic, p.TradeNameEnglish, l.BaseQuantity, l.NetTotal, l.CostTotal,
                    i.BelowCostApprovedBy, i.BelowCostReason,
                })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = lines
            .Select(l => (IReadOnlyList<object?>)new object?[]
            {
                l.Number, Local(l.CreatedAtUtc), ProductName(l.TradeNameArabic, l.TradeNameEnglish), l.BaseQuantity, l.NetTotal, l.CostTotal,
                l.CostTotal - l.NetTotal, l.BelowCostApprovedBy, l.BelowCostReason,
            })
            .ToList();

        return Done(ReportKey.BelowCostSales, PeriodFilters(period),
            [Col("Number"), Col("DateTime", ReportColumnKind.DateTime), Col("Product"), Col("Quantity", ReportColumnKind.Integer),
             Col("NetPrice", ReportColumnKind.Money), Col("Cost", ReportColumnKind.Money), Col("Loss", ReportColumnKind.Money),
             Col("ApprovedBy"), Col("Reason")],
            rows, 3, 4, 5, 6);
    }

    private async Task<Result<ReportTable>> SalesReturnsAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var returns = await (
                from r in db.SalesReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= period.FromUtc && r.CreatedAtUtc < period.ToUtc)
                join i in db.SalesInvoices on r.InvoiceId equals i.Id
                join c in db.Customers on r.CustomerId equals c.Id
                orderby r.CreatedAtUtc, r.Id
                select new
                {
                    r.Number, r.CreatedAtUtc, InvoiceNumber = i.Number, Customer = c.Name, r.Total, r.DebtReduction, r.CashRefund,
                    r.IsLate, r.Reason, r.UserName,
                })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = returns
            .Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Number, Local(r.CreatedAtUtc), r.InvoiceNumber, r.Customer, r.Total, r.DebtReduction, r.CashRefund,
                new ReportText(r.IsLate ? "Reports.Yes" : "Reports.No"), r.Reason, r.UserName,
            })
            .ToList();

        return Done(ReportKey.SalesReturns, PeriodFilters(period),
            [Col("Number"), Col("DateTime", ReportColumnKind.DateTime), Col("Invoice"), Col("Customer"), Col("Total", ReportColumnKind.Money),
             Col("DebtReduction", ReportColumnKind.Money), Col("CashRefund", ReportColumnKind.Money), Col("Late"), Col("Reason"), Col("User")],
            rows, 4, 5, 6);
    }
}
