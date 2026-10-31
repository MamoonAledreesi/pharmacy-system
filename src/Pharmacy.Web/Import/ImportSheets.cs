using Microsoft.Extensions.Localization;
using Pharmacy.Application.Import;
using Pharmacy.Domain.Common;
using Pharmacy.Web.Localization;

namespace Pharmacy.Web.Import;

/// <summary>Builds the translated texts of the products sheet: the blank template, and the rejected rows of an import.</summary>
public sealed class ImportSheets(IStringLocalizer<SharedResource> localizer)
{
    public const string TemplatePath = "/catalog/import/template";
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] _noteKeys =
        ["Import.Note.Title", "Import.Note.Rows", "Import.Note.Product", "Import.Note.Units", "Import.Note.Existing", "Import.Note.Stock", "Import.Note.Expiry", "Import.Note.Dates", "Import.Note.Again"];

    private IReadOnlyList<string> Headers() => [.. ProductColumns.All.Select(c => localizer[ProductColumns.ResourcePrefix + c].Value)];

    private IReadOnlyList<string> Notes() => [.. _noteKeys.Select(k => localizer[k].Value)];

    /// <summary>The blank template: the headers, two example rows, and the instructions.</summary>
    public ProductSheetText Template() => new(
        localizer["Import.Sheet.Products"], Headers(),
        [[.. localizer["Import.Example.Row1"].Value.Split(';')], [.. localizer["Import.Example.Row2"].Value.Split(';')]],
        localizer["Import.Sheet.Notes"], Notes(), SupportedCultures.IsArabicUi);

    /// <summary>The rows that were not imported, in the same layout plus a last column with the reasons, ready to fix and upload again.</summary>
    public ProductSheetText Rejected(IReadOnlyList<ProductImportRow> source, IReadOnlyList<ImportRowResult> rejected)
    {
        var byNumber = source.ToDictionary(r => r.RowNumber);
        var rows = rejected
            .Where(r => byNumber.ContainsKey(r.RowNumber))
            .Select(r => (IReadOnlyList<string>)[.. Cells(byNumber[r.RowNumber]), string.Join(" | ", r.Errors.Select(Message))])
            .ToList();
        return new ProductSheetText(
            localizer["Import.Sheet.Products"], [.. Headers(), localizer["Import.ErrorColumn"].Value], rows, localizer["Import.Sheet.Notes"], [], SupportedCultures.IsArabicUi);
    }

    /// <summary>An error or warning in words; arguments that name a column are translated first.</summary>
    public string Message(Error error)
    {
        var arguments = error.Arguments.Select(a => a is string s && s.StartsWith(ProductColumns.ResourcePrefix, StringComparison.Ordinal) ? localizer[s].Value : a).ToArray();
        return localizer[error.Code, arguments];
    }

    private static string[] Cells(ProductImportRow r) =>
    [
        r.NameArabic ?? string.Empty, r.NameEnglish ?? string.Empty, r.ScientificName ?? string.Empty, r.Strength ?? string.Empty, r.Manufacturer ?? string.Empty,
        r.Category ?? string.Empty, r.DosageForm ?? string.Empty, r.BaseUnit ?? string.Empty, r.LargerUnit ?? string.Empty, r.UnitsInLarger?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        r.SellingPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, r.BaseBarcode ?? string.Empty, r.LargerBarcode ?? string.Empty,
        r.MinimumStock?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, r.TracksExpiry is { } t ? (t ? "1" : "0") : string.Empty, r.BatchNumber ?? string.Empty,
        r.ExpiryDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, r.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        r.QuantityUnit ?? string.Empty, r.UnitCost?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    ];
}
