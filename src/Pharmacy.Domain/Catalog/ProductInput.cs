namespace Pharmacy.Domain.Catalog;

/// <summary>Descriptive product data entered by the user.</summary>
public sealed record ProductDetails(
    string? TradeNameArabic,
    string? TradeNameEnglish,
    string? ScientificName,
    string? Strength,
    string? Manufacturer,
    int? DosageFormId,
    int CategoryId,
    string? Notes,
    bool TracksExpiry,
    int? MinimumStock,
    int? NearExpiryDays);

/// <summary>
/// One packaging unit of a product. <see cref="Factor"/> is how many base units it contains
/// (the base unit itself has factor 1). <see cref="Id"/> is null for a unit being added.
/// </summary>
public sealed record ProductUnitData(
    int? Id,
    int UnitOfMeasureId,
    bool IsBase,
    int Factor,
    decimal? DefaultSellingPrice,
    string? Barcode);
