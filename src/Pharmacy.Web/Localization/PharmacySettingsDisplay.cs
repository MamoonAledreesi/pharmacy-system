using System.Globalization;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Web.Localization;

public static class PharmacySettingsDisplay
{
    /// <summary>The pharmacy name in the current UI language.</summary>
    public static string DisplayName(this PharmacySettingsData settings, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == SupportedCultures.Arabic ? settings.NameArabic : settings.NameEnglish;
}
