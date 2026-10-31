using System.Globalization;
using ClosedXML.Excel;
using Pharmacy.Application.Import;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Infrastructure.Import;

/// <summary>
/// Reads the products sheet (the first worksheet; row 1 is the header, columns in the order of <see cref="ProductColumns"/>).
/// It is forgiving about how people type: Arabic-Indic digits, "1,250.50" or "1250,5", Excel dates or text dates, month and
/// year only ("06/2027" is the last day of June), and yes/no in Arabic or English.
/// </summary>
public sealed class ProductSheetReader : IProductSheetReader
{
    private static readonly string[] _dateFormats =
        ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "yyyyMMdd"];

    private static readonly string[] _monthFormats = ["MM/yyyy", "M/yyyy", "yyyy-MM", "yyyy/MM", "MM-yyyy"];

    private static readonly HashSet<string> _yes = ["yes", "y", "true", "1", "نعم", "ن", "صح"];

    private static readonly HashSet<string> _no = ["no", "n", "false", "0", "لا", "خطا", "خطأ"];

    public ProductSheet Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ProductSheet([], new Error(ImportErrors.FileInvalid));
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            var last = sheet?.LastRowUsed()?.RowNumber() ?? 1;
            if (sheet is null || last < 2)
            {
                return new ProductSheet([], new Error(ImportErrors.NoRows));
            }

            if (last - 1 > ProductImportService.MaxRows)
            {
                return new ProductSheet([], new Error(ImportErrors.TooManyRows, ProductImportService.MaxRows));
            }

            var rows = new List<ProductImportRow>();
            for (var r = 2; r <= last; r++)
            {
                var row = sheet.Row(r);
                if (Enumerable.Range(1, ProductColumns.All.Count).All(c => row.Cell(c).IsEmpty()))
                {
                    continue;
                }

                rows.Add(ReadRow(row, r));
            }

            return rows.Count == 0 ? new ProductSheet([], new Error(ImportErrors.NoRows)) : new ProductSheet(rows, null);
        }
    }

    private static ProductImportRow ReadRow(IXLRow row, int number)
    {
        var bad = new List<string>();

        string? Text(int c) => TextOf(row.Cell(c));
        int? Int(int c) => Parse(c, cell => IntOf(cell), bad);
        decimal? Money(int c) => Parse(c, cell => DecimalOf(cell), bad);
        bool? Flag(int c) => Parse(c, cell => FlagOf(cell), bad);
        DateOnly? Date(int c) => Parse(c, cell => DateOf(cell), bad);

        T? Parse<T>(int c, Func<IXLCell, (bool Ok, T? Value)> convert, List<string> problems)
            where T : struct
        {
            var cell = row.Cell(c);
            if (cell.IsEmpty())
            {
                return null;
            }

            var (ok, value) = convert(cell);
            if (!ok)
            {
                problems.Add(ProductColumns.All[c - 1]);
            }

            return ok ? value : null;
        }

        return new ProductImportRow(
            number, Text(1), Text(2), Text(3), Text(4), Text(5), Text(6), Text(7), Text(8), Text(9),
            Int(10), Money(11), Text(12), Text(13), Int(14), Flag(15), Text(16), Date(17), Int(18), Text(19), Money(20), bad);
    }

    // Barcodes and batch numbers are often typed as numbers; read them back as the digits that were typed, not as 6.2E+12.
    private static string? TextOf(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var value = cell.Value;
        string text = value.IsNumber
            ? (value.GetNumber() % 1 == 0 ? ((decimal)value.GetNumber()).ToString("0", CultureInfo.InvariantCulture) : value.GetNumber().ToString("R", CultureInfo.InvariantCulture))
            : value.IsDateTime ? value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.IsBoolean ? (value.GetBoolean() ? "TRUE" : "FALSE")
            : cell.GetString();
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    private static (bool Ok, int? Value) IntOf(IXLCell cell)
    {
        var (ok, number) = NumberOf(cell);
        return ok && number is { } n && n == decimal.Truncate(n) && n >= int.MinValue && n <= int.MaxValue ? (true, (int)n) : (false, null);
    }

    private static (bool Ok, decimal? Value) DecimalOf(IXLCell cell) => NumberOf(cell);

    private static (bool Ok, decimal? Value) NumberOf(IXLCell cell)
    {
        if (cell.Value.IsNumber)
        {
            return (true, (decimal)cell.Value.GetNumber());
        }

        var text = Digits(cell.GetString()).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Contains(',', StringComparison.Ordinal))
        {
            // "1,250.50" and "1,250" use the comma for thousands; "1250,5" uses it as the decimal point.
            var thousands = text.Contains('.', StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d{1,3}(,\d{3})+$");
            text = thousands ? text.Replace(",", string.Empty, StringComparison.Ordinal) : text.Replace(',', '.');
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, null);
    }

    private static (bool Ok, bool? Value) FlagOf(IXLCell cell)
    {
        if (cell.Value.IsBoolean)
        {
            return (true, cell.Value.GetBoolean());
        }

        var text = SearchNormalizer.Normalize(cell.IsEmpty() ? null : TextOf(cell));
        return _yes.Contains(text) ? (true, true) : _no.Contains(text) ? (true, false) : (false, null);
    }

    private static (bool Ok, DateOnly? Value) DateOf(IXLCell cell)
    {
        if (cell.Value.IsDateTime)
        {
            return (true, DateOnly.FromDateTime(cell.Value.GetDateTime()));
        }

        var text = Digits(cell.GetString()).Trim();
        if (DateOnly.TryParseExact(text, _dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (true, day);
        }

        // A month and a year is the last day of that month (the expiry convention printed on packs).
        if (DateOnly.TryParseExact(text, _monthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            && month.Year is >= 2000 and <= 2100)
        {
            return (true, new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
        }

        return (false, null);
    }

    // Arabic-Indic and Persian digits and the Arabic decimal and thousands marks become their ASCII forms.
    private static string Digits(string text)
    {
        var chars = text.Select(ch => ch switch
        {
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            '٫' => '.',
            '٬' => ',',
            _ => ch,
        });
        return new string([.. chars]);
    }
}
