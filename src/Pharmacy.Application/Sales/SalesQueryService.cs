using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Application.Sales;

/// <summary>Sales lists and details (Sales.View) and the pre-posting preview (Sales.Create).</summary>
public sealed class SalesQueryService(IPharmacyDbContext db, InventoryLedger inventoryLedger, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<SaleListItem>>> SearchAsync(SaleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.SalesView))
        {
            return Result.Failure<PagedResult<SaleListItem>>(_forbidden);
        }

        var invoices = db.SalesInvoices.AsNoTracking();
        if (query.CustomerId is { } customerId)
        {
            invoices = invoices.Where(i => i.CustomerId == customerId);
        }

        if (query.From is { } from)
        {
            var fromUtc = LocalDayStartUtc(from);
            invoices = invoices.Where(i => i.CreatedAtUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            var beforeUtc = LocalDayStartUtc(to.AddDays(1));
            invoices = invoices.Where(i => i.CreatedAtUtc < beforeUtc);
        }

        var text = query.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            invoices = invoices.Where(i =>
                i.Number.Contains(text) || db.Customers.Any(c => c.Id == i.CustomerId && c.Name.Contains(text)));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await invoices.CountAsync(cancellationToken);
        var items = await invoices
            .OrderByDescending(i => i.Id)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .Select(i => new SaleListItem(
                i.Id,
                i.Number,
                i.CreatedAtUtc,
                db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).First(),
                i.Total,
                i.PaidCash,
                i.OnCredit,
                i.Lines.Count))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<SaleListItem>(items, total));
    }

    public async Task<Result<SaleDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SalesView))
        {
            return Result.Failure<SaleDetail>(_forbidden);
        }

        var invoice = await db.SalesInvoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Allocations)
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return Result.Failure<SaleDetail>(new Error(CommonErrors.NotFound));
        }

        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => new { c.Name, c.Phone })
            .SingleAsync(cancellationToken);

        var productIds = invoice.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var unitNames = await UnitNamesAsync(invoice.Lines.Select(l => l.ProductUnitId).ToList(), cancellationToken);
        var batchIds = invoice.Lines.SelectMany(l => l.Allocations).Select(a => a.BatchId).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.BatchNumber, b.ExpiryDate })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var costVisible = currentUser.HasPermission(Permissions.SalesViewCost);
        var lines = invoice.Lines
            .OrderBy(l => l.Id)
            .Select(l =>
            {
                var product = products[l.ProductId];
                return new SaleLineDetail(
                    new LocalizedName(product.TradeNameArabic ?? product.TradeNameEnglish ?? string.Empty, product.TradeNameEnglish ?? product.TradeNameArabic ?? string.Empty),
                    product.Strength,
                    unitNames[l.ProductUnitId],
                    l.Quantity,
                    l.UnitPrice,
                    l.LineSubtotal,
                    l.DiscountShare,
                    l.NetTotal,
                    costVisible ? l.CostTotal : null,
                    l.IsBelowCost,
                    [.. l.Allocations.OrderBy(a => a.Id).Select(a => new SaleAllocationDetail(
                        batches[a.BatchId].BatchNumber, batches[a.BatchId].ExpiryDate, a.BaseQuantity, a.IsNearExpiry))]);
            })
            .ToList();

        return Result.Success(new SaleDetail(
            invoice.Id, invoice.Number, invoice.CreatedAtUtc, invoice.CustomerId, customer.Name, customer.Phone,
            invoice.Subtotal, invoice.DiscountAmount, invoice.DiscountPercent, invoice.Total, invoice.PaidCash, invoice.OnCredit,
            invoice.Note, invoice.UserName, invoice.BelowCostApprovedBy, invoice.BelowCostReason, invoice.DiscountApprovedBy,
            costVisible, lines));
    }

    /// <summary>
    /// Checks a sale without posting it: stock per line (first expiring first, lines of the same product take turns),
    /// near-expiry and below-cost warnings, and the invoice totals. Nothing is locked or changed.
    /// </summary>
    public async Task<Result<SalePreview>> PreviewAsync(SalePreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!currentUser.HasPermission(Permissions.SalesCreate))
        {
            return Result.Failure<SalePreview>(_forbidden);
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var units = await db.Set<ProductUnit>().AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var factors = new List<int>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (!units.TryGetValue(line.ProductUnitId, out var unit) || unit.ProductId != line.ProductId)
            {
                return Result.Failure<SalePreview>(new Error(InventoryErrors.UnknownUnit));
            }

            factors.Add(unit.Factor);
        }

        var priced = SalePricing.Price(
            request.Lines.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice)).ToList(),
            new InvoiceDiscount(request.DiscountAmount, request.DiscountPercent));
        if (priced.IsFailure)
        {
            return Result.Failure<SalePreview>(priced.Error!);
        }

        var today = inventoryLedger.Today;
        var stock = new Dictionary<int, List<BatchStock>>();
        var nearDays = new Dictionary<int, int>();
        foreach (var productId in productIds)
        {
            stock[productId] = await db.Batches.AsNoTracking()
                .Where(b => b.ProductId == productId)
                .Select(b => new BatchStock(b.Id, b.ExpiryDate, b.ReceivedAtUtc, b.QuantityAvailable, b.UnitCost))
                .ToListAsync(cancellationToken);
            nearDays[productId] = await inventoryLedger.NearExpiryDaysAsync(productId, cancellationToken);
        }

        var lines = new List<SalePreviewLine>(request.Lines.Count);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var baseQuantity = (long)line.Quantity * factors[index];
            var batches = stock[line.ProductId];
            var sellable = FefoAllocator.SellableQuantity(batches, today);

            var allocation = baseQuantity <= int.MaxValue
                ? FefoAllocator.Allocate(batches, (int)baseQuantity, today, nearDays[line.ProductId])
                : Result.Failure<IReadOnlyList<StockAllocation>>(new Error(InventoryErrors.QuantityTooLarge));
            if (allocation.IsFailure)
            {
                lines.Add(new SalePreviewLine(false, sellable, false, false, priced.Value.DiscountShares[index], priced.Value.LineNets[index]));
                continue;
            }

            foreach (var taken in allocation.Value)
            {
                var position = batches.FindIndex(b => b.BatchId == taken.BatchId);
                batches[position] = batches[position] with { Available = batches[position].Available - taken.Quantity };
            }

            var cost = decimal.Round(allocation.Value.Sum(a => a.Quantity * a.UnitCost), SalePricing.Decimals, MidpointRounding.AwayFromZero);
            lines.Add(new SalePreviewLine(
                true, sellable, allocation.Value.Any(a => a.IsNearExpiry), priced.Value.LineNets[index] < cost,
                priced.Value.DiscountShares[index], priced.Value.LineNets[index]));
        }

        var limit = await db.PharmacySettings.Select(s => s.MaxDiscountPercent).SingleAsync(cancellationToken);
        return Result.Success(new SalePreview(
            lines, priced.Value.Subtotal, priced.Value.Discount, priced.Value.DiscountPercent, priced.Value.Net, limit,
            priced.Value.DiscountPercent > limit && !currentUser.HasPermission(Permissions.SalesGrantHighDiscount),
            lines.Any(l => l.BelowCost), lines.All(l => l.InStock)));
    }

    // The start of a local calendar day expressed in UTC, so "from" and "to" filters follow the pharmacy's clock.
    private DateTime LocalDayStartUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), clock.LocalTimeZone);

    private async Task<Dictionary<int, LocalizedName>> UnitNamesAsync(IReadOnlyCollection<int> productUnitIds, CancellationToken cancellationToken) =>
        await (from unit in db.Set<ProductUnit>().AsNoTracking()
               join measure in db.UnitsOfMeasure on unit.UnitOfMeasureId equals measure.Id
               where productUnitIds.Contains(unit.Id)
               select new { unit.Id, measure.NameArabic, measure.NameEnglish })
            .ToDictionaryAsync(x => x.Id, x => new LocalizedName(x.NameArabic, x.NameEnglish), cancellationToken);
}
