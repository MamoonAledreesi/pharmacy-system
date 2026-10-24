using System.Globalization;
using System.Text;

namespace Pharmacy.Domain.Catalog;

/// <summary>
/// Normalizes text so searches ignore common Arabic spelling variants and letter case:
/// hamza forms of alef (أ إ آ ٱ → ا), taa marbuta (ة → ه), alef maqsura (ى → ي), hamza carriers
/// (ؤ → و, ئ → ي), diacritics and tatweel are removed, Arabic-Indic digits become ASCII, and
/// whitespace is collapsed. Stored search text and typed queries go through the same rules.
/// </summary>
public static class SearchNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var original in text)
        {
            if (char.IsWhiteSpace(original))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (IsIgnorable(original))
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(Map(original));
        }

        return builder.ToString();
    }

    // Arabic diacritics (fathatan .. sukun), superscript alef, and tatweel carry no search meaning.
    private static bool IsIgnorable(char c) => c is (>= 'ً' and <= 'ْ') or 'ٰ' or 'ـ';

    private static char Map(char c) => c switch
    {
        'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
        'ة' => 'ه',
        'ى' => 'ي',
        'ؤ' => 'و',
        'ئ' => 'ي',
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        _ => char.ToLower(c, CultureInfo.InvariantCulture),
    };
}
