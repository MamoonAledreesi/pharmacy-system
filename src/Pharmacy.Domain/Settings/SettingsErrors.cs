using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Settings;

/// <summary>Error codes are localization keys in the UI resources.</summary>
public static class SettingsErrors
{
    public const string NameArabicRequired = "Settings.NameArabicRequired";
    public const string NameEnglishRequired = "Settings.NameEnglishRequired";
    public const string NameTooLong = "Settings.NameTooLong";
    public const string PhoneTooLong = "Settings.PhoneTooLong";
    public const string AddressTooLong = "Settings.AddressTooLong";
    public const string InvalidThemeMode = "Settings.InvalidThemeMode";
    public const string InvalidColor = "Settings.InvalidColor";
    public const string InvalidNearExpiryDays = "Settings.InvalidNearExpiryDays";
    public const string InvalidMaxDiscountPercent = "Settings.InvalidMaxDiscountPercent";

    internal static Error TooLong(string code, int maxLength) => new(code, maxLength);
}
