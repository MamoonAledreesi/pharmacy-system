using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Import;

/// <summary>
/// One data row of the products sheet, as read from the file. A cell that holds something unreadable for its column
/// (text where a number belongs) is null and its column is listed in <see cref="Unreadable"/>.
/// </summary>
public sealed record ProductImportRow(
    int RowNumber,
    string? NameArabic,
    string? NameEnglish,
    string? ScientificName,
    string? Strength,
    string? Manufacturer,
    string? Category,
    string? DosageForm,
    string? BaseUnit,
    string? LargerUnit,
    int? UnitsInLarger,
    decimal? SellingPrice,
    string? BaseBarcode,
    string? LargerBarcode,
    int? MinimumStock,
    bool? TracksExpiry,
    string? BatchNumber,
    DateOnly? ExpiryDate,
    int? Quantity,
    string? QuantityUnit,
    decimal? UnitCost,
    IReadOnlyList<string> Unreadable)
{
    /// <summary>True when the row carries any opening-stock cell.</summary>
    public bool HasStockCells =>
        !string.IsNullOrWhiteSpace(BatchNumber) || ExpiryDate is not null || Quantity is not null || UnitCost is not null || !string.IsNullOrWhiteSpace(QuantityUnit);
}

/// <summary>What was read from a file: its rows, or the reason the file cannot be used at all.</summary>
public sealed record ProductSheet(IReadOnlyList<ProductImportRow> Rows, Error? FileError);

/// <summary>Reads the products sheet from an Excel file. Implemented in Infrastructure.</summary>
public interface IProductSheetReader
{
    ProductSheet Read(Stream stream);
}

/// <summary>The translated texts of a sheet; the application never translates, the web layer supplies them.</summary>
public sealed record ProductSheetText(
    string SheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string NotesSheetName,
    IReadOnlyList<string> Notes,
    bool RightToLeft);

/// <summary>Writes a products sheet (the blank template, or the rejected rows of an import). Implemented in Infrastructure.</summary>
public interface IProductSheetWriter
{
    byte[] Write(ProductSheetText sheet);
}

/// <summary>The columns of the sheet, in order; <c>Import.Col.{name}</c> is the translated header.</summary>
public static class ProductColumns
{
    public static IReadOnlyList<string> All { get; } =
    [
        "NameArabic", "NameEnglish", "ScientificName", "Strength", "Manufacturer", "Category", "DosageForm", "BaseUnit", "LargerUnit",
        "UnitsInLarger", "SellingPrice", "BaseBarcode", "LargerBarcode", "MinimumStock", "TracksExpiry", "BatchNumber", "ExpiryDate",
        "Quantity", "QuantityUnit", "UnitCost",
    ];

    public const string ResourcePrefix = "Import.Col.";
}

public static class ImportErrors
{
    public const string FileInvalid = "Import.Error.FileInvalid";
    public const string NoRows = "Import.Error.NoRows";
    public const string TooManyRows = "Import.Error.TooManyRows";
    public const string HasErrors = "Import.Error.HasErrors";
    public const string Unreadable = "Import.Error.Unreadable";
    public const string CategoryRequired = "Import.Error.CategoryRequired";
    public const string UnknownCategory = "Import.Error.UnknownCategory";
    public const string UnknownDosageForm = "Import.Error.UnknownDosageForm";
    public const string BaseUnitRequired = "Import.Error.BaseUnitRequired";
    public const string UnknownUnit = "Import.Error.UnknownUnit";
    public const string LargerUnitNeedsCount = "Import.Error.LargerUnitNeedsCount";
    public const string LargerUnitSameAsBase = "Import.Error.LargerUnitSameAsBase";
    public const string DuplicateBarcodeInFile = "Import.Error.DuplicateBarcodeInFile";
    public const string InconsistentProduct = "Import.Error.InconsistentProduct";
    public const string QuantityRequired = "Import.Error.QuantityRequired";
    public const string UnitCostRequired = "Import.Error.UnitCostRequired";
    public const string ExpiryRequired = "Import.Error.ExpiryRequired";
    public const string UnknownQuantityUnit = "Import.Error.UnknownQuantityUnit";
    public const string NoLargerUnit = "Import.Error.NoLargerUnit";
    public const string StockRejected = "Import.Error.StockRejected";
    public const string ProductRejected = "Import.Error.ProductRejected";
}

public static class ImportWarnings
{
    public const string ProductExists = "Import.Warning.ProductExists";
    public const string MatchedByBarcode = "Import.Warning.MatchedByBarcode";
    public const string StockIgnored = "Import.Warning.StockIgnored";
    public const string ExpiredBatch = "Import.Warning.ExpiredBatch";
    public const string DuplicateBatchInFile = "Import.Warning.DuplicateBatchInFile";
}

public enum ImportRowStatus
{
    Ready,
    Warning,
    Error,
}

public sealed record ImportRowResult(
    int RowNumber, string ProductName, ImportRowStatus Status, bool ProductExists, bool HasStock, IReadOnlyList<Error> Errors, IReadOnlyList<Error> Warnings);

public sealed record ImportPreview(IReadOnlyList<ImportRowResult> Rows)
{
    public int ReadyCount => Rows.Count(r => r.Status != ImportRowStatus.Error);

    public int ErrorCount => Rows.Count(r => r.Status == ImportRowStatus.Error);

    public int NewProducts => Rows.Where(r => r.Status != ImportRowStatus.Error && !r.ProductExists).Select(r => r.ProductName).Distinct().Count();

    public int ExistingProducts => Rows.Where(r => r.Status != ImportRowStatus.Error && r.ProductExists).Select(r => r.ProductName).Distinct().Count();

    public int Batches => Rows.Count(r => r.Status != ImportRowStatus.Error && r.HasStock);
}

/// <summary>What an import did. <c>Rejected</c> are the rows that were not imported, with the reasons.</summary>
public sealed record ImportResult(int ProductsCreated, int ProductsExisting, int BatchesRecorded, IReadOnlyList<ImportRowResult> Rejected);
