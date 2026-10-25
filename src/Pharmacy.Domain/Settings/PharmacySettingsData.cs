namespace Pharmacy.Domain.Settings;

/// <summary>Immutable snapshot of the pharmacy settings, used both as update input and as the cached current value.</summary>
public sealed record PharmacySettingsData(
    string NameArabic,
    string NameEnglish,
    string? Phone,
    string? Address,
    ThemeMode ThemeMode,
    ThemeColor PrimaryColor,
    int NearExpiryDays = PharmacySettings.DefaultNearExpiryDays,
    decimal MaxDiscountPercent = PharmacySettings.DefaultMaxDiscountPercent);
