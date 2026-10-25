using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Purchasing;

/// <summary>Purchase invoice lists and details (Purchasing.View), and the product facts a purchase line needs (Purchasing.Create).</summary>
public sealed class PurchaseQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<PurchaseListItem>>> SearchAsync(PurchaseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.PurchasingView))
        {
            return Result.Failure<PagedResult<PurchaseListItem>>(_forbidden);
        }

        var invoices = db.PurchaseInvoices.AsNoTracking();
        if (query.SupplierId is { } supplierId)
        {
            invoices = invoices.Where(i => i.SupplierId == supplierId);
        }

        if (query.From is { } from)
        {
            invoices = invoices.Where(i => i.InvoiceDate >= from);
        }

        if (query.To is { } to)
        {
            invoices = invoices.Where(i => i.InvoiceDate <= to);
        }

        var text = query.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            invoices = invoices.Where(i =>
                i.Number.Contains(text)
                || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(text))
                || (i.SupplierName != null && i.SupplierName.Contains(text))
                || db.Suppliers.Any(s => s.Id == i.SupplierId && s.Name.Contains(text)));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await invoices.CountAsync(cancellationToken);
        var items = await invoices
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(i => new PurchaseListItem(
                i.Id,
                i.Number,
                i.InvoiceDate,
                i.SupplierId == null ? i.SupplierName : db.Suppliers.Where(s => s.Id == i.SupplierId).Select(s => s.Name).FirstOrDefault(),
                i.Total,
                i.PaidCash,
                i.OnCredit,
                i.Lines.Count))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<PurchaseListItem>(items, total));
    }

    public async Task<Result<PurchaseDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.PurchasingView))
        {
            return Result.Failure<PurchaseDetail>(_forbidden);
        }

        var invoice = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<PurchaseDetail>(new Error(CommonErrors.NotFound));
        }

        var supplierName = invoice.SupplierId is { } supplierId
            ? await db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Name).SingleOrDefaultAsync(cancellationToken)
            : invoice.SupplierName;

        var productIds = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unitNames = await UnitNamesAsync(invoice.Lines.Select(l => l.ProductUnitId).ToList(), cancellationToken);

        var lines = invoice.Lines
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                return new PurchaseLineDetail(
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    l.BaseQuantity,
                    l.CostPerUnit,
                    l.LineTotal,
                    l.BatchNumber,
                    l.ExpiryDate);
            })
            .ToList();

        return Result.Success(new PurchaseDetail(
            invoice.Id, invoice.Number, invoice.InvoiceDate, invoice.SupplierId, supplierName, invoice.SupplierInvoiceNumber,
            invoice.Total, invoice.PaidCash, invoice.OnCredit, invoice.Note, invoice.CreatedAtUtc, invoice.UserName, lines));
    }

    /// <summary>A product's units, whether it expires, and the cost of its latest batch (per base unit), for a purchase line.</summary>
    public async Task<Result<PurchaseProduct>> GetProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.PurchasingCreate))
        {
            return Result.Failure<PurchaseProduct>(_forbidden);
        }

        var product = await db.Products.AsNoTracking().Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<PurchaseProduct>(new Error(CommonErrors.NotFound));
        }

        var unitNames = await UnitNamesAsync(product.Units.Select(u => u.Id).ToList(), cancellationToken);
        IReadOnlyList<ProductUnitSummary> units =
        [
            .. product.Units
                .OrderBy(u => u.Factor)
                .Select(u => new ProductUnitSummary(u.Id, unitNames[u.Id], u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode)),
        ];

        var lastCost = await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId)
            .OrderByDescending(b => b.ReceivedAtUtc)
            .ThenByDescending(b => b.Id)
            .Select(b => (decimal?)b.UnitCost)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(new PurchaseProduct(
            product.Id, product.TradeNameArabic, product.TradeNameEnglish, product.Strength, product.TracksExpiry, units, lastCost));
    }

    private async Task<Dictionary<int, LocalizedName>> UnitNamesAsync(IReadOnlyCollection<int> productUnitIds, CancellationToken cancellationToken) =>
        await (from unit in db.Set<Pharmacy.Domain.Catalog.ProductUnit>().AsNoTracking()
               join measure in db.UnitsOfMeasure on unit.UnitOfMeasureId equals measure.Id
               where productUnitIds.Contains(unit.Id)
               select new { unit.Id, measure.NameArabic, measure.NameEnglish })
            .ToDictionaryAsync(x => x.Id, x => new LocalizedName(x.NameArabic, x.NameEnglish), cancellationToken);
}
