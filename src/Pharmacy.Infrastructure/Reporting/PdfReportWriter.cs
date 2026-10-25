using System.Globalization;
using Pharmacy.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pharmacy.Infrastructure.Reporting;

internal static class PdfReportWriter
{
    // Fonts of the Windows machine that hosts the system; they carry the Arabic letters.
    private static readonly string[] _fonts = ["Segoe UI", "Tahoma", "Arial"];
    private const int WideTableColumns = 6;

    static PdfReportWriter() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Write(ReportDocument document) => Compose(document).GeneratePdf();

    internal static Document Compose(ReportDocument document)
    {
        // Numbers and dates are always written with "." and "," like the screens do, whatever the language.
        var culture = CultureInfo.InvariantCulture;
        var landscape = document.Columns.Count > WideTableColumns;

        return Document.Create(container => container.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontFamily(_fonts).FontSize(9));
            if (document.IsRightToLeft)
            {
                page.ContentFromRightToLeft();
            }

            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Text(document.Title).FontSize(16).Bold();
                column.Item().Text(document.PharmacyName);
                column.Item().Text(document.GeneratedAt).FontColor(Colors.Grey.Darken1);
                foreach (var filter in document.Filters)
                {
                    column.Item().Text($"{filter.Label}: {filter.Value}");
                }
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var _ in document.Columns)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    foreach (var column in document.Columns)
                    {
                        header.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(column.Header).Bold();
                    }
                });

                foreach (var row in document.Rows)
                {
                    WriteRow(table, document, row, culture, bold: false);
                }

                if (document.Totals is { } totals)
                {
                    WriteRow(table, document, totals, culture, bold: true);
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{document.PageLabel} ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        }));
    }

    private static void WriteRow(TableDescriptor table, ReportDocument document, IReadOnlyList<object?> cells, CultureInfo culture, bool bold)
    {
        for (var c = 0; c < document.Columns.Count; c++)
        {
            var value = c < cells.Count ? cells[c] : null;
            var text = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3)
                .Text(Format(value, document.Columns[c].Kind, culture));
            if (bold)
            {
                text.Bold();
            }
        }
    }

    private static string Format(object? value, ReportColumnKind kind, CultureInfo culture) => value switch
    {
        null => string.Empty,
        string text => text,
        int number => number.ToString("N0", culture),
        decimal amount when kind == ReportColumnKind.Percent => amount.ToString("0.0", culture) + "%",
        decimal amount => amount.ToString("N2", culture),
        DateOnly day => day.ToString("yyyy-MM-dd", culture),
        DateTime moment => moment.ToString("yyyy-MM-dd HH:mm", culture),
        _ => Convert.ToString(value, culture) ?? string.Empty,
    };
}
