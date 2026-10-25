using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Sales;

/// <summary>Return lists and details (Sales.View) and what an invoice still lets the user return (Sales.Return).</summary>
public sealed class SalesReturnQueryService(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>The invoice with, for each line, what was sold, what was already returned, and what can still come back.</summary>
    public async Task<Result<ReturnableInvoice>> GetReturnableAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SalesReturn))
        {
            return Result.Failure<ReturnableInvoice>(_forbidden);
        }

        var invoice = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<ReturnableInvoice>(new Error(CommonErrors.NotFound));
        }

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => new { c.Name, c.IsDefaultCash })
            .SingleAsync(cancellationToken);
        var window = await db.PharmacySettings.Select(s => s.ReturnWindowDays).SingleAsync(cancellationToken);
        var days = (clock.GetLocalNow().Date - TimeZoneInfo.ConvertTimeFromUtc(invoice.CreatedAtUtc, clock.LocalTimeZone).Date).Days;

        var returned = await db.SalesReturns.AsNoTracking()
            .Where(r => r.InvoiceId == invoiceId)
            .SelectMany(r => r.Lines)
            .GroupBy(l => l.InvoiceLineId)
            .Select(g => new { LineId = g.Key, Base = g.Sum(l => l.BaseQuantity) })
            .ToDictionaryAsync(x => x.LineId, x => x.Base, cancellationToken);

        var productIds = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Include(p => p.Units)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unitNames = await UnitNamesAsync(products.Values.SelectMany(p => p.Units).Select(u => u.Id).ToList(), cancellationToken);

        var lines = invoice.Lines
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                var already = returned.GetValueOrDefault(l.Id);
                return new ReturnableLine(
                    l.Id,
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    l.BaseQuantity,
                    already,
                    l.BaseQuantity - already,
                    l.NetTotal,
                    [.. product.Units.OrderBy(u => u.Factor).Select(u => new ProductUnitSummary(u.Id, unitNames[u.Id], u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode))]);
            })
            .ToList();

        return Result.Success(new ReturnableInvoice(
            invoice.Id, invoice.Number, invoice.CreatedAtUtc, invoice.CustomerId, customer.Name, customer.IsDefaultCash,
            invoice.Total, invoice.OnCredit, window, days, days > window, currentUser.HasPermission(Permissions.SalesLateReturn), lines));
    }

    public async Task<Result<PagedResult<ReturnListItem>>> SearchAsync(ReturnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.SalesView))
        {
            return Result.Failure<PagedResult<ReturnListItem>>(_forbidden);
        }

        var returns = db.SalesReturns.AsNoTracking();
        if (query.InvoiceId is { } invoiceId)
        {
            returns = returns.Where(r => r.InvoiceId == invoiceId);
        }

        if (query.From is { } from)
        {
            var fromUtc = LocalDayStartUtc(from);
            returns = returns.Where(r => r.CreatedAtUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            var beforeUtc = LocalDayStartUtc(to.AddDays(1));
            returns = returns.Where(r => r.CreatedAtUtc < beforeUtc);
        }

        var text = query.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            returns = returns.Where(r =>
                r.Number.Contains(text)
                || db.SalesInvoices.Any(i => i.Id == r.InvoiceId && i.Number.Contains(text))
                || db.Customers.Any(c => c.Id == r.CustomerId && c.Name.Contains(text)));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await returns.CountAsync(cancellationToken);
        var items = await returns
            .OrderByDescending(r => r.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(r => new ReturnListItem(
                r.Id,
                r.Number,
                r.CreatedAtUtc,
                r.InvoiceId,
                db.SalesInvoices.Where(i => i.Id == r.InvoiceId).Select(i => i.Number).First(),
                db.Customers.Where(c => c.Id == r.CustomerId).Select(c => c.Name).First(),
                r.Total,
                r.DebtReduction,
                r.CashRefund,
                r.IsLate))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<ReturnListItem>(items, total));
    }

    public async Task<Result<ReturnDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SalesView))
        {
            return Result.Failure<ReturnDetail>(_forbidden);
        }

        var salesReturn = await db.SalesReturns.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Allocations)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (salesReturn is null)
        {
            return Result.Failure<ReturnDetail>(new Error(CommonErrors.NotFound));
        }

        var invoiceNumber = await db.SalesInvoices.Where(i => i.Id == salesReturn.InvoiceId).Select(i => i.Number).SingleAsync(cancellationToken);
        var customerName = await db.Customers.Where(c => c.Id == salesReturn.CustomerId).Select(c => c.Name).SingleAsync(cancellationToken);
        var productIds = salesReturn.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unitNames = await UnitNamesAsync(salesReturn.Lines.Select(l => l.ProductUnitId).ToList(), cancellationToken);

        var lines = salesReturn.Lines
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                return new ReturnLineDetail(
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    l.Value,
                    l.Allocations.Any(a => a.Quarantined),
                    l.Allocations.Any(a => !a.Quarantined));
            })
            .ToList();

        return Result.Success(new ReturnDetail(
            salesReturn.Id, salesReturn.Number, salesReturn.CreatedAtUtc, salesReturn.InvoiceId, invoiceNumber, salesReturn.CustomerId,
            customerName, salesReturn.Total, salesReturn.DebtReduction, salesReturn.CashRefund, salesReturn.Reason, salesReturn.IsLate,
            salesReturn.UserName, lines));
    }

    private DateTime LocalDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.LocalTimeZone);

    private async Task<Dictionary<int, LocalizedName>> UnitNamesAsync(IReadOnlyCollection<int> productUnitIds, CancellationToken cancellationToken) =>
        await (from unit in db.Set<ProductUnit>().AsNoTracking()
               join measure in db.UnitsOfMeasure on unit.UnitOfMeasureId equals measure.Id
               where productUnitIds.Contains(unit.Id)
               select new { unit.Id, measure.NameArabic, measure.NameEnglish })
            .ToDictionaryAsync(x => x.Id, x => new LocalizedName(x.NameArabic, x.NameEnglish), cancellationToken);
}
