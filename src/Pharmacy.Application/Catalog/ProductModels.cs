using Pharmacy.Domain.Catalog;

namespace Pharmacy.Application.Catalog;

public sealed record LocalizedName(string Arabic, string English)
{
    public string For(bool arabic) => arabic ? Arabic : English;
}

public sealed record ProductUnitSummary(int Id, LocalizedName Unit, bool IsBase, int Factor, decimal? DefaultSellingPrice, string? Barcode);

public sealed record ProductListItem(
    int Id,
    string? TradeNameArabic,
    string? TradeNameEnglish,
    string? ScientificName,
    string? Strength,
    LocalizedName? DosageForm,
    LocalizedName Category,
    bool IsActive,
    IReadOnlyList<ProductUnitSummary> Units)
{
    public string DisplayName(bool arabic) =>
        (arabic ? TradeNameArabic ?? TradeNameEnglish : TradeNameEnglish ?? TradeNameArabic) ?? string.Empty;
}

/// <summary>Product list filter. <see cref="Text"/> matches trade names, scientific name, and barcodes.</summary>
public sealed record ProductQuery(string? Text, int? CategoryId, bool IncludeInactive, int PageIndex, int PageSize);

public sealed record ProductForEdit(int Id, ProductDetails Details, IReadOnlyList<ProductUnitData> Units, bool IsActive, byte[] RowVersion);
