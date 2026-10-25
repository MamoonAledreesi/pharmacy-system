using Microsoft.EntityFrameworkCore;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Reports;

public sealed partial class ReportService
{
    private async Task<Result<ReportTable>> PurchasesDailyAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var invoices = await db.PurchaseInvoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= period.From && i.InvoiceDate <= period.To)
            .GroupBy(i => i.InvoiceDate)
            .OrderBy(g => g.Key)
            .Select(g => new { Day = g.Key, Count = g.Count(), Total = g.Sum(i => i.Total), Cash = g.Sum(i => i.PaidCash), Credit = g.Sum(i => i.OnCredit) })
            .ToListAsync(cancellationToken);

        var rows = invoices
            .Select(g => (IReadOnlyList<object?>)new object?[] { g.Day, g.Count, g.Total, g.Cash, g.Credit })
            .ToList();

        return Done(ReportKey.PurchasesDaily, PeriodFilters(period),
            [Col("Date", ReportColumnKind.Date), Col("Invoices", ReportColumnKind.Integer), Col("Total", ReportColumnKind.Money),
             Col("PaidCash", ReportColumnKind.Money), Col("OnCredit", ReportColumnKind.Money)],
            rows, 1, 2, 3, 4);
    }

    private async Task<Result<ReportTable>> PurchasesBySupplierAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var invoices = await (
                from i in db.PurchaseInvoices.AsNoTracking().Where(i => i.InvoiceDate >= period.From && i.InvoiceDate <= period.To)
                join s in db.Suppliers on i.SupplierId equals s.Id into suppliers
                from s in suppliers.DefaultIfEmpty()
                group i by new { i.SupplierId, Name = s != null ? s.Name : i.SupplierName } into g
                orderby g.Sum(i => i.Total) descending
                select new
                {
                    g.Key.Name, Count = g.Count(), Total = g.Sum(i => i.Total), Cash = g.Sum(i => i.PaidCash), Credit = g.Sum(i => i.OnCredit),
                })
            .ToListAsync(cancellationToken);

        var rows = invoices
            .Select(g => (IReadOnlyList<object?>)new object?[] { g.Name, g.Count, g.Total, g.Cash, g.Credit })
            .ToList();

        return Done(ReportKey.PurchasesBySupplier, PeriodFilters(period),
            [Col("Supplier"), Col("Invoices", ReportColumnKind.Integer), Col("Total", ReportColumnKind.Money),
             Col("PaidCash", ReportColumnKind.Money), Col("OnCredit", ReportColumnKind.Money)],
            rows, 1, 2, 3, 4);
    }

    private async Task<Result<ReportTable>> PurchaseReturnsAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var returns = await (
                from r in db.PurchaseReturns.AsNoTracking().Where(r => r.CreatedAtUtc >= period.FromUtc && r.CreatedAtUtc < period.ToUtc)
                join i in db.PurchaseInvoices on r.InvoiceId equals i.Id
                join s in db.Suppliers on r.SupplierId equals s.Id into suppliers
                from s in suppliers.DefaultIfEmpty()
                orderby r.CreatedAtUtc, r.Id
                select new
                {
                    r.Number, r.CreatedAtUtc, InvoiceNumber = i.Number, Supplier = s != null ? s.Name : i.SupplierName, r.Total,
                    r.DebtReduction, r.CashRefund, r.Reason, r.UserName,
                })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var rows = returns
            .Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Number, Local(r.CreatedAtUtc), r.InvoiceNumber, r.Supplier, r.Total, r.DebtReduction, r.CashRefund, r.Reason, r.UserName,
            })
            .ToList();

        return Done(ReportKey.PurchaseReturns, PeriodFilters(period),
            [Col("Number"), Col("DateTime", ReportColumnKind.DateTime), Col("Invoice"), Col("Supplier"), Col("Total", ReportColumnKind.Money),
             Col("DebtReduction", ReportColumnKind.Money), Col("CashRefund", ReportColumnKind.Money), Col("Reason"), Col("User")],
            rows, 4, 5, 6);
    }
}
