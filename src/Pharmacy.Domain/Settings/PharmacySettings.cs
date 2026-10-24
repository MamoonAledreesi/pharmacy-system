using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Settings;

/// <summary>
/// Pharmacy-wide settings. Exactly one row exists (<see cref="SingletonId"/>); it is seeded by the
/// first migration and only ever updated, never created or deleted by the application.
/// </summary>
public sealed class PharmacySettings
{
    public const int SingletonId = 1;
    public const int NameMaxLength = 150;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 300;

    public static PharmacySettingsData Defaults { get; } = new(
        NameArabic: "الصيدلية",
        NameEnglish: "Pharmacy",
        Phone: null,
        Address: null,
        ThemeMode: ThemeMode.Light,
        PrimaryColor: ThemeColor.MedicalGreen);

    // Used by EF Core.
    private PharmacySettings()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public string NameArabic { get; private set; } = Defaults.NameArabic;

    public string NameEnglish { get; private set; } = Defaults.NameEnglish;

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public ThemeMode ThemeMode { get; private set; } = Defaults.ThemeMode;

    public ThemeColor PrimaryColor { get; private set; } = Defaults.PrimaryColor;

    /// <summary>Optimistic concurrency token (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static PharmacySettings CreateDefault() => new();

    public PharmacySettingsData ToData() =>
        new(NameArabic, NameEnglish, Phone, Address, ThemeMode, PrimaryColor);

    public Result Update(PharmacySettingsData values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var nameArabic = values.NameArabic?.Trim() ?? string.Empty;
        var nameEnglish = values.NameEnglish?.Trim() ?? string.Empty;
        var phone = NullIfBlank(values.Phone);
        var address = NullIfBlank(values.Address);

        var error = Validate(nameArabic, nameEnglish, phone, address, values.ThemeMode, values.PrimaryColor);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        NameArabic = nameArabic;
        NameEnglish = nameEnglish;
        Phone = phone;
        Address = address;
        ThemeMode = values.ThemeMode;
        PrimaryColor = values.PrimaryColor;
        return Result.Success();
    }

    private static Error? Validate(
        string nameArabic, string nameEnglish, string? phone, string? address, ThemeMode themeMode, ThemeColor color)
    {
        if (nameArabic.Length == 0)
        {
            return new Error(SettingsErrors.NameArabicRequired);
        }

        if (nameEnglish.Length == 0)
        {
            return new Error(SettingsErrors.NameEnglishRequired);
        }

        if (nameArabic.Length > NameMaxLength || nameEnglish.Length > NameMaxLength)
        {
            return SettingsErrors.TooLong(SettingsErrors.NameTooLong, NameMaxLength);
        }

        if (phone?.Length > PhoneMaxLength)
        {
            return SettingsErrors.TooLong(SettingsErrors.PhoneTooLong, PhoneMaxLength);
        }

        if (address?.Length > AddressMaxLength)
        {
            return SettingsErrors.TooLong(SettingsErrors.AddressTooLong, AddressMaxLength);
        }

        if (!Enum.IsDefined(themeMode))
        {
            return new Error(SettingsErrors.InvalidThemeMode);
        }

        if (!Enum.IsDefined(color))
        {
            return new Error(SettingsErrors.InvalidColor);
        }

        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
