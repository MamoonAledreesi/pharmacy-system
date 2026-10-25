using System.Text;
using ClosedXML.Excel;
using Pharmacy.Application.Reports;
using Pharmacy.Infrastructure.Reporting;

namespace Pharmacy.IntegrationTests.Reports;

public sealed class ReportExporterTests
{
    private static readonly ReportExporter _exporter = new();

    private static ReportDocument Document(bool arabic, int rows = 3) => new(
        arabic ? "المبيعات اليومية" : "Daily sales",
        arabic ? "صيدلية الشفاء" : "Shifa Pharmacy",
        "2026-10-25 10:00",
        arabic,
        [new ReportDocumentFilter(arabic ? "من" : "From", "2026-10-01")],
        [
            new ReportDocumentColumn(arabic ? "التاريخ" : "Date", ReportColumnKind.Date),
            new ReportDocumentColumn(arabic ? "الاسم" : "Name", ReportColumnKind.Text),
            new ReportDocumentColumn(arabic ? "العدد" : "Count", ReportColumnKind.Integer),
            new ReportDocumentColumn(arabic ? "المبلغ" : "Amount", ReportColumnKind.Money),
            new ReportDocumentColumn(arabic ? "النسبة" : "Share", ReportColumnKind.Percent),
            new ReportDocumentColumn(arabic ? "الوقت" : "Time", ReportColumnKind.DateTime),
        ],
        [.. Enumerable.Range(1, rows).Select(i => (IReadOnlyList<object?>)new object?[]
        {
            new DateOnly(2026, 10, 1).AddDays(i - 1), arabic ? $"منتج {i}" : $"Product {i}", i, 10.5m * i, 12.5m, new DateTime(2026, 10, 1, 14, 30, 0).AddDays(i - 1),
        })],
        ["Total", null, rows, 10.5m * rows, null, null],
        arabic ? "صفحة" : "Page");

    [Fact]
    public void Excel_HoldsTheTitleHeadersTypedValuesAndTotals()
    {
        var bytes = _exporter.ToExcel(Document(arabic: false));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(1, 1).GetString().ShouldBe("Daily sales");
        sheet.Cell(2, 1).GetString().ShouldBe("Shifa Pharmacy");
        sheet.Cell(4, 1).GetString().ShouldBe("From: 2026-10-01");
        sheet.Cell(6, 3).GetString().ShouldBe("Count");
        sheet.Cell(7, 1).GetDateTime().ShouldBe(new DateTime(2026, 10, 1));
        sheet.Cell(7, 2).GetString().ShouldBe("Product 1");
        sheet.Cell(8, 3).GetDouble().ShouldBe(2);
        sheet.Cell(9, 4).GetValue<decimal>().ShouldBe(31.5m);
        sheet.Cell(9, 4).Style.NumberFormat.Format.ShouldBe("#,##0.00");
        sheet.Cell(9, 5).Style.NumberFormat.Format.ShouldBe("0.0\"%\"");
        sheet.Cell(10, 1).GetString().ShouldBe("Total");
        sheet.Cell(10, 4).GetValue<decimal>().ShouldBe(31.5m);
        sheet.Cell(10, 1).Style.Font.Bold.ShouldBeTrue();
        sheet.RightToLeft.ShouldBeFalse();
    }

    [Fact]
    public void Excel_InArabic_IsRightToLeft_AndKeepsTheArabicText()
    {
        var bytes = _exporter.ToExcel(Document(arabic: true));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.RightToLeft.ShouldBeTrue();
        sheet.Cell(1, 1).GetString().ShouldBe("المبيعات اليومية");
        sheet.Cell(7, 2).GetString().ShouldBe("منتج 1");
    }

    [Fact]
    public void Excel_KeepsTextThatLooksLikeAFormulaAsText()
    {
        var document = Document(arabic: false) with
        {
            Rows = [new object?[] { new DateOnly(2026, 10, 1), "=1+1", 1, 1m, 1m, new DateTime(2026, 10, 1) }],
            Totals = null,
        };

        using var workbook = new XLWorkbook(new MemoryStream(_exporter.ToExcel(document)));
        var cell = workbook.Worksheet(1).Cell(7, 2);

        cell.HasFormula.ShouldBeFalse();
        cell.GetString().ShouldBe("=1+1");
    }

    [Fact]
    public void Excel_UsesAValidSheetName_EvenForAnAwkwardTitle()
    {
        var document = Document(arabic: false) with { Title = "A/B: report [very long title that goes beyond thirty-one characters]" };

        using var workbook = new XLWorkbook(new MemoryStream(_exporter.ToExcel(document)));

        workbook.Worksheet(1).Name.Length.ShouldBeLessThanOrEqualTo(31);
        workbook.Worksheet(1).Name.IndexOfAny([':', '/', '[', ']']).ShouldBe(-1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pdf_IsAPdfDocument(bool arabic)
    {
        var bytes = _exporter.ToPdf(Document(arabic));

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        bytes.Length.ShouldBeGreaterThan(1500);
    }

    [Fact]
    public void Pdf_OfManyRows_SpansSeveralPages()
    {
        var small = _exporter.ToPdf(Document(arabic: false, rows: 3));
        var large = _exporter.ToPdf(Document(arabic: false, rows: 400));

        large.Length.ShouldBeGreaterThan(small.Length);
        Encoding.ASCII.GetString(large).Split("/Type /Page", StringSplitOptions.None).Length.ShouldBeGreaterThan(3);
    }
}
