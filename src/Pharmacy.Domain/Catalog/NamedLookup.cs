using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Catalog;

/// <summary>
/// Shared behavior of the bilingual reference lists (categories, units, dosage forms):
/// both names are required, and items are deactivated rather than deleted because products reference them.
/// </summary>
public abstract class NamedLookup
{
    public const int NameMaxLength = 100;

    public int Id { get; protected set; }

    public string NameArabic { get; private set; } = string.Empty;

    public string NameEnglish { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;

    public string DisplayName(bool arabic) => arabic ? NameArabic : NameEnglish;

    public Result Update(string nameArabic, string nameEnglish, bool isActive)
    {
        var arabic = nameArabic?.Trim() ?? string.Empty;
        var english = nameEnglish?.Trim() ?? string.Empty;

        var error = Validate(arabic, english);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        NameArabic = arabic;
        NameEnglish = english;
        IsActive = isActive;
        return Result.Success();
    }

    private static Error? Validate(string arabic, string english)
    {
        if (arabic.Length == 0)
        {
            return new Error(CatalogErrors.NameArabicRequired);
        }

        if (english.Length == 0)
        {
            return new Error(CatalogErrors.NameEnglishRequired);
        }

        return arabic.Length > NameMaxLength || english.Length > NameMaxLength
            ? new Error(CatalogErrors.NameTooLong, NameMaxLength)
            : null;
    }
}

public sealed class Category : NamedLookup;

/// <summary>A unit of measure such as tablet, strip, or box. Products choose which units they use and their factors.</summary>
public sealed class UnitOfMeasure : NamedLookup;

/// <summary>A pharmaceutical dosage form such as tablets, syrup, or injection.</summary>
public sealed class DosageForm : NamedLookup;
