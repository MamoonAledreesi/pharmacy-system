using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Catalog;

/// <summary>Creates and edits products. Reading needs Catalog.View; changes need Catalog.Manage and are audited.</summary>
public sealed class ProductService(IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider clock)
{
    private static readonly Error _forbidden = new(CommonErrors.Forbidden);
    private static readonly Error _notFound = new(CommonErrors.NotFound);

    public async Task<Result<ProductForEdit>> GetForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogView))
        {
            return Result.Failure<ProductForEdit>(_forbidden);
        }

        var product = await db.Products.AsNoTracking().Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null
            ? Result.Failure<ProductForEdit>(_notFound)
            : Result.Success(new ProductForEdit(
                product.Id, DetailsOf(product), UnitsOf(product), product.IsActive,
                UnitsLocked: await HasStockHistoryAsync(product.Id, cancellationToken), product.RowVersion));
    }

    public async Task<Result<int>> CreateAsync(
        ProductDetails details, IReadOnlyList<ProductUnitData> units, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(units);

        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return Result.Failure<int>(_forbidden);
        }

        var created = Product.Create(details, units, clock.GetUtcNow().UtcDateTime);
        if (created.IsFailure)
        {
            return Result.Failure<int>(created.Error!);
        }

        var product = created.Value;
        var referenceError = await ValidateReferencesAsync(product, cancellationToken);
        if (referenceError is not null)
        {
            return Result.Failure<int>(referenceError);
        }

        db.Products.Add(product);
        auditLog.Record(AuditActions.ProductCreated, product.DisplayName(arabic: false), Snapshot(product));

        var saved = await SaveAsync(cancellationToken);
        return saved.IsSuccess ? Result.Success(product.Id) : Result.Failure<int>(saved.Error!);
    }

    public async Task<Result> UpdateAsync(
        int id, ProductDetails details, IReadOnlyList<ProductUnitData> units, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return Result.Failure(_forbidden);
        }

        var product = await db.Products.Include(p => p.Units).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Result.Failure(_notFound);
        }

        var before = Snapshot(product);
        db.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;

        var updated = product.Update(details, units, unitsLocked: await HasStockHistoryAsync(id, cancellationToken));
        if (updated.IsFailure)
        {
            return updated;
        }

        var referenceError = await ValidateReferencesAsync(product, cancellationToken);
        if (referenceError is not null)
        {
            return Result.Failure(referenceError);
        }

        // Always update the product row, even when only its units changed, so the rowversion check runs
        // and a concurrent edit by another user is detected instead of silently merged.
        db.Entry(product).Property(p => p.SearchText).IsModified = true;

        auditLog.Record(AuditActions.ProductUpdated, product.DisplayName(arabic: false), new { Before = before, After = Snapshot(product) });
        return await SaveAsync(cancellationToken);
    }

    public async Task<Result> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return Result.Failure(_forbidden);
        }

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return Result.Failure(_notFound);
        }

        if (product.IsActive != isActive)
        {
            product.SetActive(isActive);
            auditLog.Record(AuditActions.ProductStatusChanged, product.DisplayName(arabic: false), new { IsActive = isActive });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private Task<bool> HasStockHistoryAsync(int productId, CancellationToken cancellationToken) =>
        db.Batches.AnyAsync(b => b.ProductId == productId, cancellationToken);

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error(CommonErrors.ConcurrencyConflict));
        }
        catch (DbUpdateException)
        {
            // The only unique constraint a valid product can hit is the barcode, when another
            // user saved the same barcode between our check and this save.
            return Result.Failure(new Error(CatalogErrors.DuplicateBarcode));
        }
    }

    private async Task<Error?> ValidateReferencesAsync(Product product, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == product.CategoryId, cancellationToken))
        {
            return new Error(CatalogErrors.UnknownCategory);
        }

        if (product.DosageFormId is { } formId && !await db.DosageForms.AnyAsync(f => f.Id == formId, cancellationToken))
        {
            return new Error(CatalogErrors.UnknownDosageForm);
        }

        var unitIds = product.Units.Select(u => u.UnitOfMeasureId).ToList();
        if (await db.UnitsOfMeasure.CountAsync(m => unitIds.Contains(m.Id), cancellationToken) != unitIds.Count)
        {
            return new Error(CatalogErrors.UnknownUnit);
        }

        var barcodes = product.Units.Where(u => u.Barcode is not null).Select(u => u.Barcode!).ToList();
        var barcodeTaken = barcodes.Count > 0 && await db.Products
            .Where(p => p.Id != product.Id)
            .SelectMany(p => p.Units)
            .AnyAsync(u => barcodes.Contains(u.Barcode!), cancellationToken);
        return barcodeTaken ? new Error(CatalogErrors.DuplicateBarcode) : null;
    }

    private static ProductDetails DetailsOf(Product p) => new(
        p.TradeNameArabic, p.TradeNameEnglish, p.ScientificName, p.Strength, p.Manufacturer, p.DosageFormId,
        p.CategoryId, p.Notes, p.TracksExpiry, p.MinimumStock, p.NearExpiryDays);

    private static List<ProductUnitData> UnitsOf(Product p) =>
        [.. p.Units.OrderBy(u => u.Factor).Select(u => new ProductUnitData(u.Id, u.UnitOfMeasureId, u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode))];

    private static object Snapshot(Product p) => new { Details = DetailsOf(p), Units = UnitsOf(p).Select(u => u with { Id = null }) };
}
