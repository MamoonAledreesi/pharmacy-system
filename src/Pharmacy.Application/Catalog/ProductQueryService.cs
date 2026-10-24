using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Catalog;

/// <summary>Product lists and fast search. Requires Catalog.View.</summary>
public sealed class ProductQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;
    public const int QuickSearchLimit = 20;

    public async Task<Result<PagedResult<ProductListItem>>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!currentUser.HasPermission(Permissions.CatalogView))
        {
            return Result.Failure<PagedResult<ProductListItem>>(new Error(CommonErrors.Forbidden));
        }

        var products = Filter(db.Products.AsNoTracking(), query.Text, query.IncludeInactive);
        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var total = await products.CountAsync(cancellationToken);
        var items = await Project(products
                .OrderBy(p => p.TradeNameEnglish ?? p.TradeNameArabic)
                .ThenBy(p => p.Id)
                .Skip(Math.Max(query.PageIndex, 0) * pageSize)
                .Take(pageSize))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<ProductListItem>(items, total));
    }

    /// <summary>
    /// Autocomplete for invoices: active products only, best matches first (names that start with
    /// the typed text before names that merely contain it).
    /// </summary>
    public async Task<Result<IReadOnlyList<ProductListItem>>> QuickSearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogView))
        {
            return Result.Failure<IReadOnlyList<ProductListItem>>(new Error(CommonErrors.Forbidden));
        }

        var normalized = SearchNormalizer.Normalize(text);
        if (normalized.Length == 0)
        {
            return Result.Success<IReadOnlyList<ProductListItem>>([]);
        }

        IReadOnlyList<ProductListItem> items = await Project(Filter(db.Products.AsNoTracking(), text, includeInactive: false)
                .OrderBy(p => p.SearchText.StartsWith(normalized) ? 0 : 1)
                .ThenBy(p => p.TradeNameEnglish ?? p.TradeNameArabic)
                .Take(QuickSearchLimit))
            .ToListAsync(cancellationToken);
        return Result.Success(items);
    }

    /// <summary>Every word typed must appear in the product's normalized search text, in any order.</summary>
    private static IQueryable<Product> Filter(IQueryable<Product> products, string? text, bool includeInactive)
    {
        if (!includeInactive)
        {
            products = products.Where(p => p.IsActive);
        }

        foreach (var word in SearchNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            products = products.Where(p => p.SearchText.Contains(word));
        }

        return products;
    }

    private IQueryable<ProductListItem> Project(IQueryable<Product> products) =>
        from p in products
        join category in db.Categories on p.CategoryId equals category.Id
        join dosageForm in db.DosageForms on p.DosageFormId equals dosageForm.Id into forms
        from form in forms.DefaultIfEmpty()
        select new ProductListItem(
            p.Id,
            p.TradeNameArabic,
            p.TradeNameEnglish,
            p.ScientificName,
            p.Strength,
            form == null ? null : new LocalizedName(form.NameArabic, form.NameEnglish),
            new LocalizedName(category.NameArabic, category.NameEnglish),
            p.IsActive,
            p.Units
                .OrderBy(u => u.Factor)
                .Select(u => new ProductUnitSummary(
                    u.Id,
                    db.UnitsOfMeasure.Where(m => m.Id == u.UnitOfMeasureId).Select(m => new LocalizedName(m.NameArabic, m.NameEnglish)).First(),
                    u.IsBase,
                    u.Factor,
                    u.DefaultSellingPrice,
                    u.Barcode))
                .ToList());
}
