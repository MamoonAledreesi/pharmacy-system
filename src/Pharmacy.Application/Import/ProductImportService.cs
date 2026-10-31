using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Import;

/// <summary>
/// Imports products and their opening stock from a sheet. The rows are checked first (<see cref="PreviewAsync"/> changes
/// nothing); <see cref="ImportAsync"/> then creates each new product with <see cref="ProductService"/> and records each
/// opening batch with <see cref="InventoryService"/>, so every rule of those services applies. Products that already exist
/// are never changed, only given the stock of their rows. Each product and each batch is saved on its own, and a row has a
/// fixed operation id, so uploading the same file again continues where it stopped and never doubles stock.
/// Needs Catalog.Manage; the stock cells need Inventory.OpeningStock too (without it they are ignored with a warning).
/// </summary>
public sealed class ProductImportService(
    IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog, ProductService products, InventoryService inventory, TimeProvider clock)
{
    public const int MaxRows = 5000;

    private static readonly Error _forbidden = new(CommonErrors.Forbidden);

    public async Task<Result<ImportPreview>> PreviewAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var denied = Check(rows);
        if (denied is not null)
        {
            return Result.Failure<ImportPreview>(denied);
        }

        var analysis = await AnalyzeAsync(rows, cancellationToken);
        return Result.Success(new ImportPreview([.. analysis.Rows.Select(r => r.ToResult())]));
    }

    /// <summary>Imports the file. <paramref name="progress"/> is told how many products are done, so a long import can show it. With <paramref name="skipInvalid"/> false any error stops everything; true imports what is valid and returns the rest in <c>Rejected</c>.</summary>
    public async Task<Result<ImportResult>> ImportAsync(
        IReadOnlyList<ProductImportRow> rows, Guid fileKey, bool skipInvalid, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var denied = Check(rows);
        if (denied is not null)
        {
            return Result.Failure<ImportResult>(denied);
        }

        var analysis = await AnalyzeAsync(rows, cancellationToken);
        if (!skipInvalid && analysis.Rows.Any(r => r.Errors.Count > 0))
        {
            return Result.Failure<ImportResult>(new Error(ImportErrors.HasErrors));
        }

        var created = 0;
        var existing = 0;
        var batches = 0;
        var importable = analysis.Groups.Where(g => g.Errors.Count == 0).ToList();
        var done = 0;

        foreach (var group in importable)
        {
            progress?.Report((done++, importable.Count));
            var productId = group.ExistingId;
            var unitIds = group.UnitIds;
            if (productId is null)
            {
                var result = await products.CreateAsync(group.Details!, group.NewUnits!, cancellationToken);
                if (result.IsFailure)
                {
                    foreach (var row in group.Rows)
                    {
                        row.Errors.Add(result.Error!);
                    }

                    continue;
                }

                productId = result.Value;
                unitIds = await UnitIdsOfAsync(productId.Value, cancellationToken);
                created++;
            }
            else
            {
                existing++;
            }

            foreach (var row in group.Rows.Where(r => r.Errors.Count == 0 && r.StockUnit is not null))
            {
                var unitId = row.StockUnit == StockUnit.Larger ? unitIds.Larger : unitIds.Base;
                var request = new OpeningStockRequest(
                    OperationIdOf(fileKey, row.Row.RowNumber), productId.Value, unitId!.Value, row.Row.Quantity!.Value, row.Row.UnitCost!.Value,
                    string.IsNullOrWhiteSpace(row.Row.BatchNumber) ? null : row.Row.BatchNumber.Trim(), row.Row.ExpiryDate);
                var received = await inventory.ReceiveOpeningStockAsync(request, cancellationToken);
                if (received.IsFailure)
                {
                    row.Errors.Add(received.Error!);
                    continue;
                }

                batches++;
            }
        }

        var rejected = analysis.Rows.Where(r => r.Errors.Count > 0).Select(r => r.ToResult()).ToList();
        auditLog.Record(AuditActions.ProductsImported, $"{created}/{batches}", new { ProductsCreated = created, ProductsExisting = existing, Batches = batches, Rejected = rejected.Count });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(new ImportResult(created, existing, batches, rejected));
    }

    private Error? Check(IReadOnlyList<ProductImportRow> rows)
    {
        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return _forbidden;
        }

        if (rows.Count == 0)
        {
            return new Error(ImportErrors.NoRows);
        }

        return rows.Count > MaxRows ? new Error(ImportErrors.TooManyRows, MaxRows) : null;
    }

    private static Guid OperationIdOf(Guid fileKey, int rowNumber) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"{fileKey:N}:{rowNumber}")));

    private async Task<UnitIds> UnitIdsOfAsync(int productId, CancellationToken cancellationToken)
    {
        var units = await db.Set<ProductUnit>().AsNoTracking().Where(u => u.ProductId == productId).Select(u => new { u.Id, u.IsBase, u.Factor }).ToListAsync(cancellationToken);
        return new UnitIds(units.SingleOrDefault(u => u.IsBase)?.Id, units.Where(u => !u.IsBase).OrderByDescending(u => u.Factor).Select(u => (int?)u.Id).FirstOrDefault());
    }

    private async Task<Analysis> AnalyzeAsync(IReadOnlyList<ProductImportRow> rows, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var canStock = currentUser.HasPermission(Permissions.InventoryOpeningStock);
        var categories = await LookupAsync(db.Categories.Select(c => new LookupRow(c.Id, c.NameArabic, c.NameEnglish)), cancellationToken);
        var forms = await LookupAsync(db.DosageForms.Select(c => new LookupRow(c.Id, c.NameArabic, c.NameEnglish)), cancellationToken);
        var units = await LookupAsync(db.UnitsOfMeasure.Select(c => new LookupRow(c.Id, c.NameArabic, c.NameEnglish)), cancellationToken);
        var existingProducts = await db.Products.AsNoTracking()
            .Select(p => new ExistingProduct(p.Id, p.TradeNameArabic, p.TradeNameEnglish, p.Strength, p.TracksExpiry)).ToListAsync(cancellationToken);
        var existingUnits = await db.Set<ProductUnit>().AsNoTracking()
            .Select(u => new ExistingUnit(u.ProductId, u.Id, u.UnitOfMeasureId, u.IsBase, u.Factor, u.Barcode)).ToListAsync(cancellationToken);

        var byName = new Dictionary<string, ExistingProduct>();
        foreach (var p in existingProducts)
        {
            byName.TryAdd(NameKey(p.NameArabic, p.NameEnglish, p.Strength), p);
        }

        var byBarcode = new Dictionary<string, ExistingProduct>();
        var productsById = existingProducts.ToDictionary(p => p.Id);
        foreach (var u in existingUnits.Where(u => !string.IsNullOrWhiteSpace(u.Barcode)))
        {
            byBarcode.TryAdd(BarcodeKey(u.Barcode!), productsById[u.ProductId]);
        }

        var unitsOfProduct = existingUnits.GroupBy(u => u.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        // Rows of one product (same names and strength) are one group; a row with no name stands alone and fails the name check.
        var groups = new List<Group>();
        var groupByKey = new Dictionary<string, Group>();
        var states = new List<RowState>();
        foreach (var row in rows)
        {
            var key = string.IsNullOrWhiteSpace(row.NameArabic) && string.IsNullOrWhiteSpace(row.NameEnglish)
                ? $"#row{row.RowNumber}"
                : NameKey(row.NameArabic, row.NameEnglish, row.Strength);
            if (!groupByKey.TryGetValue(key, out var group))
            {
                group = new Group(key, row);
                groups.Add(group);
                groupByKey[key] = group;
            }

            var state = new RowState(row, group);
            group.Rows.Add(state);
            states.Add(state);
            foreach (var column in row.Unreadable)
            {
                state.Errors.Add(new Error(ImportErrors.Unreadable, ProductColumns.ResourcePrefix + column));
            }
        }

        var seenBarcodes = new Dictionary<string, Group>();
        foreach (var group in groups)
        {
            var first = group.First;
            group.Display = first.NameArabic?.Trim() is { Length: > 0 } ar ? ar : first.NameEnglish?.Trim() ?? string.Empty;

            // The product already exists when one of its barcodes or its names and strength are known.
            ExistingProduct? match = null;
            var byCode = false;
            foreach (var code in group.Rows.SelectMany(r => new[] { r.Row.BaseBarcode, r.Row.LargerBarcode }).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (byBarcode.TryGetValue(BarcodeKey(code!), out var hit))
                {
                    match = hit;
                    byCode = true;
                    break;
                }
            }

            if (match is null && !group.Key.StartsWith('#') && byName.TryGetValue(group.Key, out var named))
            {
                match = named;
            }

            if (match is not null)
            {
                group.ExistingId = match.Id;
                group.TracksExpiry = match.TracksExpiry;
                var own = unitsOfProduct.GetValueOrDefault(match.Id) ?? [];
                group.UnitIds = new UnitIds(own.SingleOrDefault(u => u.IsBase)?.Id, own.Where(u => !u.IsBase).OrderByDescending(u => u.Factor).Select(u => (int?)u.Id).FirstOrDefault());
                group.UnitMeasures = new UnitMeasures(own.SingleOrDefault(u => u.IsBase)?.UnitOfMeasureId, own.Where(u => !u.IsBase).OrderByDescending(u => u.Factor).Select(u => (int?)u.UnitOfMeasureId).FirstOrDefault());
                foreach (var row in group.Rows)
                {
                    row.Warnings.Add(new Error(ImportWarnings.ProductExists));
                }

                if (byCode)
                {
                    group.Rows[0].Warnings.Add(new Error(ImportWarnings.MatchedByBarcode));
                }
            }
            else
            {
                BuildNewProduct(group, categories, forms, units);
            }

            // A barcode may appear once in the whole file.
            foreach (var code in group.Rows.SelectMany(r => new[] { r.Row.BaseBarcode, r.Row.LargerBarcode }).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => BarcodeKey(c!)).Distinct())
            {
                if (seenBarcodes.TryGetValue(code, out var other) && other != group)
                {
                    group.Errors.Add(new Error(ImportErrors.DuplicateBarcodeInFile, code));
                }
                else
                {
                    seenBarcodes[code] = group;
                }
            }

            // Later rows of the product must not contradict its first row.
            foreach (var row in group.Rows.Skip(1))
            {
                if (Contradicts(row.Row.Category, first.Category) || Contradicts(row.Row.BaseUnit, first.BaseUnit)
                    || Contradicts(row.Row.LargerUnit, first.LargerUnit) || (row.Row.UnitsInLarger is { } n && first.UnitsInLarger is { } m && n != m))
                {
                    row.Errors.Add(new Error(ImportErrors.InconsistentProduct));
                }
            }
        }

        var batchesInFile = new HashSet<string>();
        foreach (var state in states)
        {
            foreach (var error in state.Group.Errors)
            {
                state.Errors.Add(error);
            }

            if (state.Group.Errors.Count == 0)
            {
                CheckStock(state, canStock, today, units, batchesInFile);
            }
        }

        return new Analysis(groups, states);
    }

    private void BuildNewProduct(Group group, Dictionary<string, int> categories, Dictionary<string, int> forms, Dictionary<string, int> units)
    {
        var first = group.First;
        int? categoryId = null;
        if (string.IsNullOrWhiteSpace(first.Category))
        {
            group.Errors.Add(new Error(ImportErrors.CategoryRequired));
        }
        else if (categories.TryGetValue(SearchNormalizer.Normalize(first.Category), out var categoryFound))
        {
            categoryId = categoryFound;
        }
        else
        {
            group.Errors.Add(new Error(ImportErrors.UnknownCategory, first.Category.Trim()));
        }

        int? formId = null;
        if (!string.IsNullOrWhiteSpace(first.DosageForm))
        {
            if (forms.TryGetValue(SearchNormalizer.Normalize(first.DosageForm), out var formFound))
            {
                formId = formFound;
            }
            else
            {
                group.Errors.Add(new Error(ImportErrors.UnknownDosageForm, first.DosageForm.Trim()));
            }
        }

        int? baseId = null;
        if (string.IsNullOrWhiteSpace(first.BaseUnit))
        {
            group.Errors.Add(new Error(ImportErrors.BaseUnitRequired));
        }
        else if (units.TryGetValue(SearchNormalizer.Normalize(first.BaseUnit), out var baseFound))
        {
            baseId = baseFound;
        }
        else
        {
            group.Errors.Add(new Error(ImportErrors.UnknownUnit, first.BaseUnit.Trim()));
        }

        int? largerId = null;
        if (!string.IsNullOrWhiteSpace(first.LargerUnit))
        {
            if (!units.TryGetValue(SearchNormalizer.Normalize(first.LargerUnit), out var largerFound))
            {
                group.Errors.Add(new Error(ImportErrors.UnknownUnit, first.LargerUnit.Trim()));
            }
            else if (largerFound == baseId)
            {
                group.Errors.Add(new Error(ImportErrors.LargerUnitSameAsBase));
            }
            else if (first.UnitsInLarger is not >= 2)
            {
                group.Errors.Add(new Error(ImportErrors.LargerUnitNeedsCount));
            }
            else
            {
                largerId = largerFound;
            }
        }

        group.TracksExpiry = first.TracksExpiry ?? true;
        group.UnitMeasures = new UnitMeasures(baseId, largerId);
        if (group.Errors.Count > 0)
        {
            return;
        }

        var details = new ProductDetails(
            Clean(first.NameArabic), Clean(first.NameEnglish), Clean(first.ScientificName), Clean(first.Strength), Clean(first.Manufacturer), formId, categoryId!.Value,
            null, group.TracksExpiry, first.MinimumStock, null);
        List<ProductUnitData> data = [new(null, baseId!.Value, true, 1, first.SellingPrice, Clean(first.BaseBarcode))];
        if (largerId is { } larger)
        {
            data.Add(new ProductUnitData(null, larger, false, first.UnitsInLarger!.Value, null, Clean(first.LargerBarcode)));
        }

        // The domain validates names, lengths, prices and barcodes exactly as the product form does.
        var check = Product.Create(details, data, clock.GetUtcNow().UtcDateTime);
        if (check.IsFailure)
        {
            group.Errors.Add(check.Error!);
            return;
        }

        group.Details = details;
        group.NewUnits = data;
    }

    private void CheckStock(RowState state, bool canStock, DateOnly today, Dictionary<string, int> units, HashSet<string> batchesInFile)
    {
        var row = state.Row;
        if (!row.HasStockCells)
        {
            return;
        }

        if (!canStock)
        {
            state.Warnings.Add(new Error(ImportWarnings.StockIgnored));
            return;
        }

        var errorsBefore = state.Errors.Count;
        if (row.Quantity is not > 0 && !row.Unreadable.Contains("Quantity"))
        {
            state.Errors.Add(new Error(ImportErrors.QuantityRequired));
        }

        if (row.UnitCost is null && !row.Unreadable.Contains("UnitCost"))
        {
            state.Errors.Add(new Error(ImportErrors.UnitCostRequired));
        }

        if (state.Group.TracksExpiry && row.ExpiryDate is null && !row.Unreadable.Contains("ExpiryDate"))
        {
            state.Errors.Add(new Error(ImportErrors.ExpiryRequired));
        }

        var stockUnit = ResolveStockUnit(row.QuantityUnit, state.Group, units);
        if (stockUnit is null)
        {
            state.Errors.Add(new Error(ImportErrors.UnknownQuantityUnit, row.QuantityUnit!.Trim()));
        }
        else if (stockUnit == StockUnit.Larger && state.Group.UnitMeasures.Larger is null)
        {
            state.Errors.Add(new Error(ImportErrors.NoLargerUnit));
        }

        if (state.Errors.Count > errorsBefore)
        {
            return;
        }

        if (row.ExpiryDate is { } expiry && expiry < today)
        {
            state.Warnings.Add(new Error(ImportWarnings.ExpiredBatch));
        }

        if (!batchesInFile.Add($"{state.Group.Key}|{row.BatchNumber?.Trim()}|{row.ExpiryDate}|{row.UnitCost}|{stockUnit}"))
        {
            state.Warnings.Add(new Error(ImportWarnings.DuplicateBatchInFile));
        }

        state.StockUnit = stockUnit;
    }

    // Empty or the base unit's name (or "base") means the base unit; the larger unit's name (or "larger") means the pack.
    private static StockUnit? ResolveStockUnit(string? text, Group group, Dictionary<string, int> units)
    {
        var normalized = SearchNormalizer.Normalize(text);
        if (normalized.Length == 0 || normalized is "base" or "اساسيه" or "الاساسيه" or "الوحده الاساسيه")
        {
            return StockUnit.Base;
        }

        if (normalized is "larger" or "pack" or "اكبر" or "الاكبر" or "الوحده الاكبر")
        {
            return StockUnit.Larger;
        }

        if (units.TryGetValue(normalized, out var id))
        {
            if (id == group.UnitMeasures.Base)
            {
                return StockUnit.Base;
            }

            if (id == group.UnitMeasures.Larger)
            {
                return StockUnit.Larger;
            }
        }

        return null;
    }

    private static async Task<Dictionary<string, int>> LookupAsync(IQueryable<LookupRow> query, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, int>();
        foreach (var item in await query.ToListAsync(cancellationToken))
        {
            result.TryAdd(SearchNormalizer.Normalize(item.NameArabic), item.Id);
            result.TryAdd(SearchNormalizer.Normalize(item.NameEnglish), item.Id);
        }

        return result;
    }

    private static string NameKey(string? arabic, string? english, string? strength) =>
        $"{SearchNormalizer.Normalize(arabic)}|{SearchNormalizer.Normalize(english)}|{SearchNormalizer.Normalize(strength)}";

    private static string BarcodeKey(string barcode) => barcode.Trim().ToUpperInvariant();

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Contradicts(string? value, string? first) =>
        !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(first)
        && SearchNormalizer.Normalize(value) != SearchNormalizer.Normalize(first);

    private enum StockUnit
    {
        Base,
        Larger,
    }

    private sealed record LookupRow(int Id, string NameArabic, string NameEnglish);

    private sealed record ExistingProduct(int Id, string? NameArabic, string? NameEnglish, string? Strength, bool TracksExpiry);

    private sealed record ExistingUnit(int ProductId, int Id, int UnitOfMeasureId, bool IsBase, int Factor, string? Barcode);

    private sealed record UnitIds(int? Base, int? Larger);

    private sealed record UnitMeasures(int? Base, int? Larger);

    private sealed class Group(string key, ProductImportRow first)
    {
        public string Key { get; } = key;

        public ProductImportRow First { get; } = first;

        public List<RowState> Rows { get; } = [];

        public List<Error> Errors { get; } = [];

        public string Display { get; set; } = string.Empty;

        public int? ExistingId { get; set; }

        public bool TracksExpiry { get; set; }

        public ProductDetails? Details { get; set; }

        public List<ProductUnitData>? NewUnits { get; set; }

        public UnitIds UnitIds { get; set; } = new(null, null);

        public UnitMeasures UnitMeasures { get; set; } = new(null, null);
    }

    private sealed class RowState(ProductImportRow row, Group group)
    {
        public ProductImportRow Row { get; } = row;

        public Group Group { get; } = group;

        public List<Error> Errors { get; } = [];

        public List<Error> Warnings { get; } = [];

        public StockUnit? StockUnit { get; set; }

        public ImportRowResult ToResult() => new(
            Row.RowNumber, Group.Display, Errors.Count > 0 ? ImportRowStatus.Error : Warnings.Any(w => w.Code != ImportWarnings.ProductExists) ? ImportRowStatus.Warning : ImportRowStatus.Ready,
            Group.ExistingId is not null, StockUnit is not null, [.. Errors], [.. Warnings]);
    }

    private sealed record Analysis(List<Group> Groups, List<RowState> Rows);
}
