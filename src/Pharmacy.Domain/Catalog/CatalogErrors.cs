namespace Pharmacy.Domain.Catalog;

/// <summary>Catalog error codes. Each code is a localization key in the UI resources.</summary>
public static class CatalogErrors
{
    public const string NameArabicRequired = "Catalog.NameArabicRequired";
    public const string NameEnglishRequired = "Catalog.NameEnglishRequired";
    public const string NameTooLong = "Catalog.NameTooLong";
    public const string NameTaken = "Catalog.NameTaken";

    public const string TradeNameRequired = "Catalog.TradeNameRequired";
    public const string FieldTooLong = "Catalog.FieldTooLong";
    public const string CategoryRequired = "Catalog.CategoryRequired";
    public const string UnknownCategory = "Catalog.UnknownCategory";
    public const string UnknownDosageForm = "Catalog.UnknownDosageForm";
    public const string InvalidMinimumStock = "Catalog.InvalidMinimumStock";
    public const string InvalidNearExpiryDays = "Catalog.InvalidNearExpiryDays";

    public const string UnitsRequired = "Catalog.UnitsRequired";
    public const string ExactlyOneBaseUnit = "Catalog.ExactlyOneBaseUnit";
    public const string BaseUnitFactorMustBeOne = "Catalog.BaseUnitFactorMustBeOne";
    public const string UnitFactorTooSmall = "Catalog.UnitFactorTooSmall";
    public const string DuplicateUnit = "Catalog.DuplicateUnit";
    public const string UnknownUnit = "Catalog.UnknownUnit";
    public const string InvalidPrice = "Catalog.InvalidPrice";
    public const string BarcodeTooLong = "Catalog.BarcodeTooLong";
    public const string DuplicateBarcode = "Catalog.DuplicateBarcode";
}
