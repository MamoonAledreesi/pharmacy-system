using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Application.Inventory;

/// <summary>Stock lists, batches, and movement history. Requires Inventory.View; costs also need Inventory.ViewCost.</summary>
public sealed class InventoryQueryService(
    IPharmacyDbContext db, ICurrentUser currentUser, InventoryLedger ledger)
{
    public const int MaxPageSize = 100;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<PagedResult<StockListItem>>> SearchStockAsync(StockQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.InventoryView))
        {
            return Result.Failure<PagedResult<StockListItem>>(_forbidden);
        }

        var today = ledger.Today;
        var defaultNearDays = await db.PharmacySettings.Select(s => s.NearExpiryDays).SingleAsync(cancellationToken);

        // Per-product totals as correlated subqueries (one index seek on Batches per product). A product's own
        // near-expiry threshold overrides the pharmacy-wide one.
        var rows =
            from p in ProductSearchFilter.Apply(db.Products.AsNoTracking(), query.Text, includeInactive: true)
            let nearDays = p.NearExpiryDays ?? defaultNearDays
            select new
            {
                ProductId = p.Id,
                p.IsActive,
                p.MinimumStock,
                Name = p.TradeNameEnglish ?? p.TradeNameArabic,
                Available = db.Batches.Where(b => b.ProductId == p.Id).Sum(b => (int?)b.QuantityAvailable) ?? 0,
                Quarantined = db.Batches.Where(b => b.ProductId == p.Id).Sum(b => (int?)b.QuantityQuarantined) ?? 0,
                Expired = db.Batches.Where(b => b.ProductId == p.Id && b.ExpiryDate != null && b.ExpiryDate < today).Sum(b => (int?)b.QuantityAvailable) ?? 0,
                NearExpiry = db.Batches
                    .Where(b => b.ProductId == p.Id && b.ExpiryDate != null && b.ExpiryDate >= today && b.ExpiryDate <= today.AddDays(nearDays))
                    .Sum(b => (int?)b.QuantityAvailable) ?? 0,
                Value = db.Batches.Where(b => b.ProductId == p.Id).Sum(b => (decimal?)((b.QuantityAvailable + b.QuantityQuarantined) * b.UnitCost)) ?? 0m,
            };

        // A deactivated product stays listed only while it still has stock to deal with.
        rows = rows.Where(r => r.IsActive || r.Available + r.Quarantined > 0);
        rows = query.Filter switch
        {
            StockFilter.InStock => rows.Where(r => r.Available > 0),
            StockFilter.OutOfStock => rows.Where(r => r.Available == 0 && r.IsActive),
            StockFilter.LowStock => rows.Where(r => r.MinimumStock != null && r.Available - r.Expired <= r.MinimumStock && r.IsActive),
            StockFilter.HasExpired => rows.Where(r => r.Expired > 0),
            StockFilter.NearExpiry => rows.Where(r => r.NearExpiry > 0),
            StockFilter.HasQuarantined => rows.Where(r => r.Quarantined > 0),
            _ => rows,
        };

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(r => r.Name)
            .ThenBy(r => r.ProductId)
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = page.Select(r => r.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var units = await UnitsOfAsync(ids, cancellationToken);

        var canSeeCost = currentUser.HasPermission(Permissions.InventoryViewCost);
        IReadOnlyList<StockListItem> items =
        [
            .. page.Select(r => new StockListItem(
                r.ProductId,
                products[r.ProductId].TradeNameArabic,
                products[r.ProductId].TradeNameEnglish,
                products[r.ProductId].Strength,
                r.IsActive,
                r.Available,
                r.Quarantined,
                r.Expired,
                r.NearExpiry,
                r.MinimumStock,
                canSeeCost ? r.Value : null,
                units[r.ProductId])),
        ];
        return Result.Success(new PagedResult<StockListItem>(items, total));
    }

    /// <summary>A product with all its batches in the order they would be sold (first expiring first).</summary>
    public async Task<Result<ProductStockDetail>> GetProductStockAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.InventoryView))
        {
            return Result.Failure<ProductStockDetail>(_forbidden);
        }

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductStockDetail>(new Error(CommonErrors.NotFound));
        }

        var today = ledger.Today;
        var nearDays = await ledger.NearExpiryDaysAsync(productId, cancellationToken);
        var canSeeCost = currentUser.HasPermission(Permissions.InventoryViewCost);

        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == productId).ToListAsync(cancellationToken);
        IReadOnlyList<BatchRow> rows =
        [
            .. batches
                .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
                .ThenBy(b => b.ReceivedAtUtc)
                .ThenBy(b => b.Id)
                .Select(b => new BatchRow(
                    b.Id,
                    b.BatchNumber,
                    b.ExpiryDate,
                    ExpiryRules.StatusOf(b.ExpiryDate, today, nearDays),
                    b.ReceivedAtUtc,
                    b.QuantityAvailable,
                    b.QuantityQuarantined,
                    canSeeCost ? b.UnitCost : null)),
        ];

        var units = await UnitsOfAsync([productId], cancellationToken);
        return Result.Success(new ProductStockDetail(
            product.Id, product.TradeNameArabic, product.TradeNameEnglish, product.Strength, product.TracksExpiry, product.IsActive,
            product.MinimumStock, nearDays, units[productId], rows));
    }

    public async Task<Result<PagedResult<MovementRow>>> SearchMovementsAsync(MovementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.InventoryView))
        {
            return Result.Failure<PagedResult<MovementRow>>(_forbidden);
        }

        var movements = db.StockMovements.AsNoTracking().Where(m => m.ProductId == query.ProductId);
        if (query.BatchId is { } batchId)
        {
            movements = movements.Where(m => m.BatchId == batchId);
        }

        if (query.Type is { } type)
        {
            movements = movements.Where(m => m.Type == type);
        }

        if (query.FromUtc is { } from)
        {
            movements = movements.Where(m => m.OccurredAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            movements = movements.Where(m => m.OccurredAtUtc < to);
        }

        var canSeeCost = currentUser.HasPermission(Permissions.InventoryViewCost);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await movements.CountAsync(cancellationToken);
        var page = await (
                from m in movements
                join b in db.Batches on m.BatchId equals b.Id
                orderby m.OccurredAtUtc descending, m.Id descending
                select new MovementRow(
                    m.Id, m.OccurredAtUtc, m.Type, b.BatchNumber, b.ExpiryDate, m.AvailableDelta, m.QuarantineDelta,
                    m.AvailableAfter, m.QuarantineAfter, canSeeCost ? m.UnitCost : null, m.ReasonCode, m.Note, m.UserName))
            .Skip(Math.Max(query.PageIndex, 0) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<MovementRow>(page, total));
    }

    private async Task<Dictionary<int, IReadOnlyList<ProductUnitSummary>>> UnitsOfAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        var rows = await (
                from u in db.Set<ProductUnit>().AsNoTracking()
                where productIds.Contains(u.ProductId)
                join m in db.UnitsOfMeasure on u.UnitOfMeasureId equals m.Id
                orderby u.Factor
                select new { u.ProductId, Unit = new ProductUnitSummary(u.Id, new LocalizedName(m.NameArabic, m.NameEnglish), u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode) })
            .ToListAsync(cancellationToken);

        return productIds.ToDictionary(
            id => id,
            id => (IReadOnlyList<ProductUnitSummary>)[.. rows.Where(r => r.ProductId == id).Select(r => r.Unit)]);
    }
}
