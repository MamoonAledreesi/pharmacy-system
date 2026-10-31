using ClosedXML.Excel;
using Pharmacy.Application.Import;

namespace Pharmacy.Infrastructure.Import;

/// <summary>Writes the products sheet: a header row, the given rows, and a notes sheet. Used for the blank template and for rejected rows.</summary>
public sealed class ProductSheetWriter : IProductSheetWriter
{
    private const int MaxWidth = 40;

    public byte[] Write(ProductSheetText sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add(SafeName(sheet.SheetName));
        data.RightToLeft = sheet.RightToLeft;

        for (var c = 0; c < sheet.Headers.Count; c++)
        {
            var cell = data.Cell(1, c + 1);
            cell.Value = sheet.Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGreen;
            cell.Style.Alignment.WrapText = true;
        }

        var widths = sheet.Headers.Select(h => Math.Min(h.Length + 2, MaxWidth)).ToArray();
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            for (var c = 0; c < sheet.Rows[r].Count && c < widths.Length; c++)
            {
                var text = sheet.Rows[r][c];
                // Everything is written as text so barcodes and batch numbers keep their leading zeros.
                data.Cell(r + 2, c + 1).SetValue(text).Style.NumberFormat.Format = "@";
                widths[c] = Math.Max(widths[c], Math.Min(text.Length + 2, MaxWidth));
            }
        }

        for (var c = 0; c < widths.Length; c++)
        {
            data.Column(c + 1).Width = Math.Max(widths[c], 12);
        }

        data.SheetView.FreezeRows(1);

        if (sheet.Notes.Count > 0)
        {
            var notes = workbook.Worksheets.Add(SafeName(sheet.NotesSheetName));
            notes.RightToLeft = sheet.RightToLeft;
            for (var i = 0; i < sheet.Notes.Count; i++)
            {
                notes.Cell(i + 1, 1).Value = sheet.Notes[i];
                notes.Cell(i + 1, 1).Style.Alignment.WrapText = true;
            }

            notes.Cell(1, 1).Style.Font.Bold = true;
            notes.Column(1).Width = 110;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SafeName(string name)
    {
        var cleaned = new string([.. name.Where(ch => !":\\/?*[]".Contains(ch))]).Trim();
        return cleaned.Length == 0 ? "Sheet" : cleaned[..Math.Min(cleaned.Length, 31)];
    }
}
