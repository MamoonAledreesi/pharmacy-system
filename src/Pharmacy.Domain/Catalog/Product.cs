using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Catalog;

/// <summary>
/// A medicine or other product. Owns its packaging units; exactly one is the base unit in which
/// stock, batches, FEFO allocation, and costs are tracked. Products are deactivated, never deleted.
/// </summary>
public sealed class Product
{
    public const int TradeNameMaxLength = 200;
    public const int ScientificNameMaxLength = 200;
    public const int StrengthMaxLength = 50;
    public const int ManufacturerMaxLength = 150;
    public const int NotesMaxLength = 1000;
    public const int SearchTextMaxLength = 1000;
    public const int MaxNearExpiryDays = 3650;

    private readonly List<ProductUnit> _units = [];

    // Used by EF Core.
    private Product()
    {
    }

    public int Id { get; private set; }

    public string? TradeNameArabic { get; private set; }

    public string? TradeNameEnglish { get; private set; }

    /// <summary>Active ingredient(s); searchable so a pharmacist can find alternatives.</summary>
    public string? ScientificName { get; private set; }

    public string? Strength { get; private set; }

    public string? Manufacturer { get; private set; }

    public int? DosageFormId { get; private set; }

    public int CategoryId { get; private set; }

    public string? Notes { get; private set; }

    public bool TracksExpiry { get; private set; }

    /// <summary>Low-stock alert threshold, in base units.</summary>
    public int? MinimumStock { get; private set; }

    /// <summary>Overrides the pharmacy-wide near-expiry threshold for this product.</summary>
    public int? NearExpiryDays { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Normalized names and barcodes (see <see cref="SearchNormalizer"/>), maintained on every change.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ProductUnit> Units => _units;

    public ProductUnit BaseUnit => _units.Single(u => u.IsBase);

    /// <summary>The trade name in the requested language, falling back to the other one.</summary>
    public string DisplayName(bool arabic) =>
        (arabic ? TradeNameArabic ?? TradeNameEnglish : TradeNameEnglish ?? TradeNameArabic) ?? string.Empty;

    public static Result<Product> Create(ProductDetails details, IReadOnlyList<ProductUnitData> units, DateTime createdAtUtc)
    {
        var product = new Product { CreatedAtUtc = createdAtUtc };
        var result = product.Update(details, units);
        return result.IsSuccess ? Result.Success(product) : Result.Failure<Product>(result.Error!);
    }

    /// <summary>
    /// Replaces the product data and units. Units are matched by id: existing ones are updated, missing
    /// ones removed, and ones without an id added. Nothing changes if validation fails.
    /// </summary>
    public Result Update(ProductDetails details, IReadOnlyList<ProductUnitData> units)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(units);

        var cleanDetails = Clean(details);
        var cleanUnits = units.Select(Clean).ToList();

        var error = ValidateDetails(cleanDetails) ?? ValidateUnits(cleanUnits);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        TradeNameArabic = cleanDetails.TradeNameArabic;
        TradeNameEnglish = cleanDetails.TradeNameEnglish;
        ScientificName = cleanDetails.ScientificName;
        Strength = cleanDetails.Strength;
        Manufacturer = cleanDetails.Manufacturer;
        DosageFormId = cleanDetails.DosageFormId;
        CategoryId = cleanDetails.CategoryId;
        Notes = cleanDetails.Notes;
        TracksExpiry = cleanDetails.TracksExpiry;
        MinimumStock = cleanDetails.MinimumStock;
        NearExpiryDays = cleanDetails.NearExpiryDays;

        SyncUnits(cleanUnits);
        SearchText = BuildSearchText();
        return Result.Success();
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    private void SyncUnits(List<ProductUnitData> units)
    {
        // Unsaved units (id 0) cannot be referenced by id, so they are replaced by the incoming data too.
        var keptIds = units.Where(u => u.Id is not null).Select(u => u.Id!.Value).ToHashSet();
        _units.RemoveAll(u => u.Id == 0 || !keptIds.Contains(u.Id));

        foreach (var data in units)
        {
            var existing = data.Id is { } id ? _units.SingleOrDefault(u => u.Id == id) : null;
            if (existing is not null)
            {
                existing.Apply(data);
            }
            else
            {
                _units.Add(new ProductUnit(data));
            }
        }
    }

    private string BuildSearchText()
    {
        var parts = new[] { TradeNameArabic, TradeNameEnglish, ScientificName }
            .Concat(_units.Select(u => u.Barcode))
            .Where(p => !string.IsNullOrEmpty(p));

        var text = SearchNormalizer.Normalize(string.Join(' ', parts));
        return text.Length <= SearchTextMaxLength ? text : text[..SearchTextMaxLength];
    }

    private static ProductDetails Clean(ProductDetails d) => d with
    {
        TradeNameArabic = NullIfBlank(d.TradeNameArabic),
        TradeNameEnglish = NullIfBlank(d.TradeNameEnglish),
        ScientificName = NullIfBlank(d.ScientificName),
        Strength = NullIfBlank(d.Strength),
        Manufacturer = NullIfBlank(d.Manufacturer),
        Notes = NullIfBlank(d.Notes),
    };

    private static ProductUnitData Clean(ProductUnitData u) => u with { Barcode = NullIfBlank(u.Barcode) };

    private static Error? ValidateDetails(ProductDetails d)
    {
        if (d.TradeNameArabic is null && d.TradeNameEnglish is null)
        {
            return new Error(CatalogErrors.TradeNameRequired);
        }

        if (d.CategoryId <= 0)
        {
            return new Error(CatalogErrors.CategoryRequired);
        }

        if (d.MinimumStock < 0)
        {
            return new Error(CatalogErrors.InvalidMinimumStock);
        }

        if (d.NearExpiryDays is < 1 or > MaxNearExpiryDays)
        {
            return new Error(CatalogErrors.InvalidNearExpiryDays, MaxNearExpiryDays);
        }

        return TooLong(d.TradeNameArabic, TradeNameMaxLength)
            ?? TooLong(d.TradeNameEnglish, TradeNameMaxLength)
            ?? TooLong(d.ScientificName, ScientificNameMaxLength)
            ?? TooLong(d.Strength, StrengthMaxLength)
            ?? TooLong(d.Manufacturer, ManufacturerMaxLength)
            ?? TooLong(d.Notes, NotesMaxLength);
    }

    private static Error? ValidateUnits(List<ProductUnitData> units)
    {
        if (units.Count == 0)
        {
            return new Error(CatalogErrors.UnitsRequired);
        }

        var baseUnits = units.Where(u => u.IsBase).ToList();
        if (baseUnits.Count != 1)
        {
            return new Error(CatalogErrors.ExactlyOneBaseUnit);
        }

        if (baseUnits[0].Factor != 1)
        {
            return new Error(CatalogErrors.BaseUnitFactorMustBeOne);
        }

        // A non-base unit must contain at least two base units; otherwise it is the base unit under another name.
        if (units.Any(u => !u.IsBase && u.Factor < 2))
        {
            return new Error(CatalogErrors.UnitFactorTooSmall);
        }

        if (units.Select(u => u.UnitOfMeasureId).Distinct().Count() != units.Count)
        {
            return new Error(CatalogErrors.DuplicateUnit);
        }

        if (units.Any(u => u.DefaultSellingPrice < 0))
        {
            return new Error(CatalogErrors.InvalidPrice);
        }

        if (units.Any(u => u.Barcode?.Length > ProductUnit.BarcodeMaxLength))
        {
            return new Error(CatalogErrors.BarcodeTooLong, ProductUnit.BarcodeMaxLength);
        }

        var barcodes = units.Where(u => u.Barcode is not null).Select(u => u.Barcode!).ToList();
        return barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != barcodes.Count
            ? new Error(CatalogErrors.DuplicateBarcode)
            : null;
    }

    private static Error? TooLong(string? value, int maxLength) =>
        value?.Length > maxLength ? new Error(CatalogErrors.FieldTooLong, maxLength) : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
