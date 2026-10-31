using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Import;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.IntegrationTests.Reports;

namespace Pharmacy.IntegrationTests.Import;

/// <summary>The sales test database with a signed-in user who may manage the catalog and receive opening stock.</summary>
public abstract class ImportTestBase : ReportsTestBase
{
    protected static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.Now).AddDays(400);

    protected Task<Result<ImportPreview>> PreviewAsync(params ProductImportRow[] rows) =>
        Services.InScopeAsync(s => s.GetRequiredService<ProductImportService>().PreviewAsync(rows));

    protected Task<Result<ImportResult>> ImportAsync(bool skipInvalid, Guid? file, params ProductImportRow[] rows) =>
        Services.InScopeAsync(s => s.GetRequiredService<ProductImportService>().ImportAsync(rows, file ?? Guid.NewGuid(), skipInvalid));

    /// <summary>A row with the defaults of a typical product: tracked, strips in boxes of 4. Override what a test is about.</summary>
    protected static ProductImportRow Row(
        int number, string? ar = "أموكسيل", string? en = "Amoxil", string? strength = "500 mg", string? category = "General medicines",
        string? baseUnit = "Strip", string? largerUnit = "Box", int? perLarger = 4, string? baseBarcode = null, string? largerBarcode = null,
        bool? tracks = true, string? batch = null, DateOnly? expiry = null, int? quantity = null, string? quantityUnit = null, decimal? cost = null,
        string? form = null, decimal? price = null, string[]? unreadable = null) =>
        new(number, ar, en, null, strength, null, category, form, baseUnit, largerUnit, perLarger, price, baseBarcode, largerBarcode, null, tracks,
            batch, expiry, quantity, quantityUnit, cost, unreadable ?? []);

    protected Task<int> CountAsync<T>() where T : class =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Set<T>().CountAsync());

    protected byte[] Workbook(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");
        for (var c = 0; c < ProductColumns.All.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = ProductColumns.All[c];
        }

        fill(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    protected static string[] WithoutStockPermission => [.. ReportReader.Where(p => p != Permissions.InventoryOpeningStock)];
}
