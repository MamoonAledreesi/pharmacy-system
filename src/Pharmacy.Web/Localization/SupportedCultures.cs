using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Pharmacy.Web.Localization;

public static class SupportedCultures
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string Default = Arabic;

    public static IReadOnlyList<string> Names { get; } = [Arabic, English];

    public static bool IsSupported(string? name) =>
        name is not null && Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsRightToLeft(CultureInfo culture) => culture.TextInfo.IsRightToLeft;

    /// <summary>True when the current UI language is Arabic; picks between the Arabic and English data fields.</summary>
    public static bool IsArabicUi => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Arabic;

    public static RequestLocalizationOptions CreateOptions()
    {
        CultureInfo[] cultures = [CreateArabic(), new CultureInfo(English)];

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(cultures[0]),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
        };

        // The language is the user's explicit choice (cookie), not the browser's Accept-Language header.
        options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        return options;
    }

    private static CultureInfo CreateArabic()
    {
        var arabic = new CultureInfo(Arabic);

        // The neutral "ar" culture defaults to the Um Al-Qura (Hijri) calendar.
        // Invoices, expiry dates, and reports use Gregorian dates.
        arabic.DateTimeFormat.Calendar = new GregorianCalendar();

        // "ar" uses the Arabic decimal separator (٫), so a price typed as "12.50" would not parse.
        // Prices and quantities use "." for decimals and "," for thousands in both languages.
        var numbers = arabic.NumberFormat;
        numbers.NumberDecimalSeparator = numbers.CurrencyDecimalSeparator = numbers.PercentDecimalSeparator = ".";
        numbers.NumberGroupSeparator = numbers.CurrencyGroupSeparator = numbers.PercentGroupSeparator = ",";
        return arabic;
    }
}
