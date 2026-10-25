using System.Globalization;
using ClosedXML.Excel;
using Pharmacy.Application.Reports;

namespace Pharmacy.Infrastructure.Reporting;

internal static class ExcelReportWriter
{
    private const int MaxColumnWidth = 50;
    private const int MinColumnWidth = 10;
    private const int HeaderRow = 6;

    public static byte[] Write(ReportDocument document)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(document.Title));
        sheet.RightToLeft = document.IsRightToLeft;

        sheet.Cell(1, 1).Value = document.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(2, 1).Value = document.PharmacyName;
        sheet.Cell(3, 1).Value = document.GeneratedAt;
        sheet.Cell(4, 1).Value = string.Join("   |   ", document.Filters.Select(f => $"{f.Label}: {f.Value}"));

        var widths = new int[document.Columns.Count];
        for (var c = 0; c < document.Columns.Count; c++)
        {
            var cell = sheet.Cell(HeaderRow, c + 1);
            cell.Value = document.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            widths[c] = document.Columns[c].Header.Length;
        }

        var row = HeaderRow + 1;
        foreach (var cells in document.Rows)
        {
            WriteRow(sheet, row++, document, cells, widths, bold: false);
        }

        if (document.Totals is { } totals)
        {
            WriteRow(sheet, row, document, totals, widths, bold: true);
            sheet.Range(row, 1, row, document.Columns.Count).Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        for (var c = 0; c < widths.Length; c++)
        {
            sheet.Column(c + 1).Width = Math.Clamp(widths[c] + 2, MinColumnWidth, MaxColumnWidth);
        }

        sheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLWorksheet sheet, int rowNumber, ReportDocument document, IReadOnlyList<object?> cells, int[] widths, bool bold)
    {
        for (var c = 0; c < document.Columns.Count && c < cells.Count; c++)
        {
            var cell = sheet.Cell(rowNumber, c + 1);
            var length = 0;
            switch (cells[c])
            {
                case null:
                    break;
                case string text:
                    cell.Value = text;
                    length = text.Length;
                    break;
                case int number:
                    cell.Value = number;
                    cell.Style.NumberFormat.Format = "#,##0";
                    length = 8;
                    break;
                case decimal amount:
                    cell.Value = amount;
                    cell.Style.NumberFormat.Format = document.Columns[c].Kind == ReportColumnKind.Percent ? "0.0\"%\"" : "#,##0.00";
                    length = 14;
                    break;
                case DateOnly day:
                    cell.Value = day.ToDateTime(TimeOnly.MinValue);
                    cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                    length = 10;
                    break;
                case DateTime moment:
                    cell.Value = moment;
                    cell.Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";
                    length = 16;
                    break;
                default:
                    cell.Value = Convert.ToString(cells[c], CultureInfo.InvariantCulture);
                    break;
            }

            widths[c] = Math.Max(widths[c], length);
            cell.Style.Font.Bold = bold;
        }
    }

    // Sheet names are at most 31 characters and cannot contain : \ / ? * [ ].
    private static string SheetName(string title)
    {
        var cleaned = new string([.. title.Where(ch => !":\\/?*[]".Contains(ch))]).Trim();
        return cleaned.Length == 0 ? "Report" : cleaned[..Math.Min(cleaned.Length, 31)];
    }
}
