using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Purchasing;

/// <summary>Return lists and details (Purchasing.View) and what an invoice still lets the user send back (Purchasing.Return).</summary>
public sealed class PurchaseReturnQueryService(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    /// <summary>The invoice with, for each line, what was received, what went back already, and what its batch holds now.</summary>
    public async Task<Result<PurchaseReturnableInvoice>> GetReturnableAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.PurchasingReturn))
        {
            return Result.Failure<PurchaseReturnableInvoice>(_forbidden);
        }

        var invoice = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<PurchaseReturnableInvoice>(new Error(CommonErrors.NotFound));
        }

        var supplierName = invoice.SupplierId is { } supplierId
            ? await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken)
            : invoice.SupplierName;

        var returned = await db.PurchaseReturns.AsNoTracking()
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
        var batchIds = invoice.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var lines = invoice.Lines
            .Where(l => l.BatchId is not null)
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                var batch = batches[l.BatchId!.Value];
                var already = returned.GetValueOrDefault(l.Id);
                return new PurchaseReturnableLine(
                    l.Id,
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    l.BaseQuantity,
                    already,
                    l.BaseQuantity - already,
                    l.LineTotal,
                    batch.BatchNumber,
                    batch.ExpiryDate,
                    batch.QuantityAvailable,
                    batch.QuantityQuarantined,
                    [.. product.Units.OrderBy(u => u.Factor).Select(u => new ProductUnitSummary(u.Id, unitNames[u.Id], u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode))]);
            })
            .ToList();

        return Result.Success(new PurchaseReturnableInvoice(
            invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.SupplierId, supplierName, invoice.Total, invoice.OnCredit, lines));
    }

    public async Task<Result<PagedResult<PurchaseReturnListItem>>> SearchAsync(PurchaseReturnQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.PurchasingView))
        {
            return Result.Failure<PagedResult<PurchaseReturnListItem>>(_forbidden);
        }

        var returns = db.PurchaseReturns.AsNoTracking();
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
                || db.PurchaseInvoices.Any(i => i.Id == r.InvoiceId && (i.Number.Contains(text) || (i.SupplierName != null && i.SupplierName.Contains(text))))
                || db.Suppliers.Any(s => s.Id == r.SupplierId && s.Name.Contains(text)));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await returns.CountAsync(cancellationToken);
        var items = await returns
            .OrderByDescending(r => r.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(r => new PurchaseReturnListItem(
                r.Id,
                r.Number,
                r.CreatedAtUtc,
                r.InvoiceId,
                db.PurchaseInvoices.Where(i => i.Id == r.InvoiceId).Select(i => i.Number).First(),
                r.SupplierId == null
                    ? db.PurchaseInvoices.Where(i => i.Id == r.InvoiceId).Select(i => i.SupplierName).First()
                    : db.Suppliers.Where(s => s.Id == r.SupplierId).Select(s => s.Name).First(),
                r.Total,
                r.DebtReduction,
                r.CashRefund))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<PurchaseReturnListItem>(items, total));
    }

    public async Task<Result<PurchaseReturnDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.PurchasingView))
        {
            return Result.Failure<PurchaseReturnDetail>(_forbidden);
        }

        var purchaseReturn = await db.PurchaseReturns.AsNoTracking().Include(r => r.Lines).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (purchaseReturn is null)
        {
            return Result.Failure<PurchaseReturnDetail>(new Error(CommonErrors.NotFound));
        }

        var invoice = await db.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Id == purchaseReturn.InvoiceId)
            .Select(i => new { i.Number, i.SupplierName })
            .SingleAsync(cancellationToken);
        var supplierName = purchaseReturn.SupplierId is { } supplierId
            ? await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken)
            : invoice.SupplierName;

        var productIds = purchaseReturn.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unitNames = await UnitNamesAsync(purchaseReturn.Lines.Select(l => l.ProductUnitId).ToList(), cancellationToken);
        var batchIds = purchaseReturn.Lines.Select(l => l.BatchId).Distinct().ToList();
        var batchNumbers = await db.Batches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.BatchNumber, cancellationToken);

        var lines = purchaseReturn.Lines
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                return new PurchaseReturnLineDetail(
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    batchNumbers[l.BatchId],
                    l.FromQuarantine,
                    l.Value);
            })
            .ToList();

        return Result.Success(new PurchaseReturnDetail(
            purchaseReturn.Id, purchaseReturn.Number, purchaseReturn.CreatedAtUtc, purchaseReturn.InvoiceId, invoice.Number,
            purchaseReturn.SupplierId, supplierName, purchaseReturn.Total, purchaseReturn.DebtReduction, purchaseReturn.CashRefund,
            purchaseReturn.Reason, purchaseReturn.UserName, lines));
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
