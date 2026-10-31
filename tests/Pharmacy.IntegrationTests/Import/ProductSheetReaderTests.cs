using Pharmacy.Application.Import;
using Pharmacy.Infrastructure.Import;

namespace Pharmacy.IntegrationTests.Import;

public sealed class ProductSheetReaderTests : ImportTestBase
{
    private static readonly ProductSheetReader _reader = new();

    private ProductSheet Read(byte[] file) => _reader.Read(new MemoryStream(file));

    [Fact]
    public void ARowIsReadWithEveryColumn_InTheOrderOfTheTemplate()
    {
        var file = Workbook(s =>
        {
            string?[] values = ["أموكسيل", "Amoxil", "Amoxicillin", "500 mg", "GSK", "General medicines", "Capsules", "Strip", "Box", "4", "12.5", "6221234567890", "6229999999999", "10", "yes", "L1", "2027-06-30", "40", "Box", "30"];
            for (var c = 0; c < values.Length; c++)
            {
                s.Cell(2, c + 1).SetValue(values[c]);
            }
        });

        var sheet = Read(file);

        sheet.FileError.ShouldBeNull();
        var row = sheet.Rows.Single();
        row.RowNumber.ShouldBe(2);
        (row.NameArabic, row.NameEnglish, row.ScientificName, row.Strength, row.Manufacturer).ShouldBe(("أموكسيل", "Amoxil", "Amoxicillin", "500 mg", "GSK"));
        (row.Category, row.DosageForm, row.BaseUnit, row.LargerUnit, row.UnitsInLarger).ShouldBe(("General medicines", "Capsules", "Strip", "Box", 4));
        (row.SellingPrice, row.BaseBarcode, row.LargerBarcode, row.MinimumStock, row.TracksExpiry).ShouldBe((12.5m, "6221234567890", "6229999999999", 10, true));
        (row.BatchNumber, row.ExpiryDate, row.Quantity, row.QuantityUnit, row.UnitCost).ShouldBe(("L1", new DateOnly(2027, 6, 30), 40, "Box", 30m));
        row.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void NumbersTypedInDifferentWays_AreAllUnderstood()
    {
        var file = Workbook(s =>
        {
            s.Cell(2, 1).Value = "A";
            s.Cell(2, 11).SetValue("1,250.50");   // thousands comma
            s.Cell(2, 21 - 1).SetValue("١٢٫٥");   // Arabic-Indic digits and decimal mark
            s.Cell(2, 18).SetValue("1,000");      // thousands
            s.Cell(3, 1).Value = "B";
            s.Cell(3, 11).SetValue("7,5");        // decimal comma
            s.Cell(3, 20).Value = 99.25;          // a real number cell
            s.Cell(3, 12).Value = 6221234567890;  // a barcode typed as a number
        });

        var rows = Read(file).Rows;

        rows[0].SellingPrice.ShouldBe(1250.5m);
        rows[0].UnitCost.ShouldBe(12.5m);
        rows[0].Quantity.ShouldBe(1000);
        rows[1].SellingPrice.ShouldBe(7.5m);
        rows[1].UnitCost.ShouldBe(99.25m);
        rows[1].BaseBarcode.ShouldBe("6221234567890");
    }

    [Fact]
    public void Dates_CanBeExcelDatesTextDatesOrMonthAndYear()
    {
        var file = Workbook(s =>
        {
            s.Cell(2, 1).Value = "A";
            s.Cell(2, 17).Value = new DateTime(2027, 3, 15);
            s.Cell(3, 1).Value = "B";
            s.Cell(3, 17).SetValue("31/12/2027");
            s.Cell(4, 1).Value = "C";
            s.Cell(4, 17).SetValue("02/2028");
            s.Cell(5, 1).Value = "D";
            s.Cell(5, 17).SetValue("not a date");
        });

        var rows = Read(file).Rows;

        rows[0].ExpiryDate.ShouldBe(new DateOnly(2027, 3, 15));
        rows[1].ExpiryDate.ShouldBe(new DateOnly(2027, 12, 31));
        rows[2].ExpiryDate.ShouldBe(new DateOnly(2028, 2, 29));
        rows[3].ExpiryDate.ShouldBeNull();
        rows[3].Unreadable.ShouldBe(["ExpiryDate"]);
    }

    [Fact]
    public void YesAndNo_AreReadInArabicAndEnglish_AndUnreadableValuesAreReported()
    {
        var file = Workbook(s =>
        {
            s.Cell(2, 1).Value = "A";
            s.Cell(2, 15).SetValue("نعم");
            s.Cell(3, 1).Value = "B";
            s.Cell(3, 15).SetValue("No");
            s.Cell(4, 1).Value = "C";
            s.Cell(4, 15).SetValue("maybe");
            s.Cell(4, 18).SetValue("many");
        });

        var rows = Read(file).Rows;

        rows[0].TracksExpiry.ShouldBe(true);
        rows[1].TracksExpiry.ShouldBe(false);
        rows[2].TracksExpiry.ShouldBeNull();
        rows[2].Unreadable.ShouldBe(["TracksExpiry", "Quantity"], ignoreOrder: true);
    }

    [Fact]
    public void EmptyRowsAreSkipped_AndTheRowNumbersStayThoseOfTheFile()
    {
        var file = Workbook(s =>
        {
            s.Cell(2, 1).Value = "A";
            s.Cell(4, 1).Value = "B";
        });

        var rows = Read(file).Rows;

        rows.Select(r => r.RowNumber).ShouldBe([2, 4]);
    }

    [Fact]
    public void AFileWithoutRows_OrNotAnExcelFile_IsRefused()
    {
        var empty = Read(Workbook(_ => { }));
        var garbage = Read([1, 2, 3, 4, 5]);

        empty.FileError!.Code.ShouldBe(ImportErrors.NoRows);
        garbage.FileError!.Code.ShouldBe(ImportErrors.FileInvalid);
    }

    [Fact]
    public void MoreRowsThanTheLimit_AreRefused()
    {
        var file = Workbook(s =>
        {
            for (var r = 2; r <= ProductImportService.MaxRows + 2; r++)
            {
                s.Cell(r, 1).Value = $"P{r}";
            }
        });

        Read(file).FileError!.Code.ShouldBe(ImportErrors.TooManyRows);
    }

    [Fact]
    public void TheWriter_ProducesASheetTheReaderCanReadBack()
    {
        var writer = new ProductSheetWriter();
        var headers = ProductColumns.All.ToArray();
        string[] values = ["أموكسيل", "Amoxil", "", "500 mg", "", "General medicines", "", "Strip", "Box", "4", "12.5", "00123", "", "", "نعم", "L1", "2027-06-30", "40", "Box", "30"];

        var file = writer.Write(new ProductSheetText("Products", headers, [values], "Notes", ["How to fill", "line"], RightToLeft: true));
        var row = Read(file).Rows.Single();

        row.NameArabic.ShouldBe("أموكسيل");
        row.BaseBarcode.ShouldBe("00123");   // leading zeros survive
        row.ExpiryDate.ShouldBe(new DateOnly(2027, 6, 30));
        row.TracksExpiry.ShouldBe(true);
    }
}
