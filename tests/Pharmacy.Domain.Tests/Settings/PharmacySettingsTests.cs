using Pharmacy.Domain.Settings;

namespace Pharmacy.Domain.Tests.Settings;

public class PharmacySettingsTests
{
    private static readonly PharmacySettingsData _valid = new(
        NameArabic: "صيدلية الشفاء",
        NameEnglish: "Al Shifa Pharmacy",
        Phone: "0123456789",
        Address: "الشارع الرئيسي",
        ThemeMode: ThemeMode.Dark,
        PrimaryColor: ThemeColor.Blue);

    [Fact]
    public void CreateDefault_UsesDefaults()
    {
        var settings = PharmacySettings.CreateDefault();

        settings.Id.ShouldBe(PharmacySettings.SingletonId);
        settings.ToData().ShouldBe(PharmacySettings.Defaults);
    }

    [Fact]
    public void Update_WithValidValues_AppliesThem()
    {
        var settings = PharmacySettings.CreateDefault();

        var result = settings.Update(_valid);

        result.IsSuccess.ShouldBeTrue();
        settings.ToData().ShouldBe(_valid);
    }

    [Fact]
    public void Update_TrimsTextAndStoresBlankOptionalFieldsAsNull()
    {
        var settings = PharmacySettings.CreateDefault();

        settings.Update(_valid with { NameArabic = "  صيدلية  ", Phone = "   ", Address = "" });

        settings.NameArabic.ShouldBe("صيدلية");
        settings.Phone.ShouldBeNull();
        settings.Address.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "Pharmacy", SettingsErrors.NameArabicRequired)]
    [InlineData("   ", "Pharmacy", SettingsErrors.NameArabicRequired)]
    [InlineData("صيدلية", "", SettingsErrors.NameEnglishRequired)]
    public void Update_WithoutNames_Fails(string nameArabic, string nameEnglish, string expectedCode)
    {
        var settings = PharmacySettings.CreateDefault();

        var result = settings.Update(_valid with { NameArabic = nameArabic, NameEnglish = nameEnglish });

        result.Error!.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void Update_WithTooLongValues_FailsWithTheLimit()
    {
        var settings = PharmacySettings.CreateDefault();

        var name = settings.Update(_valid with { NameEnglish = new string('a', PharmacySettings.NameMaxLength + 1) });
        var phone = settings.Update(_valid with { Phone = new string('1', PharmacySettings.PhoneMaxLength + 1) });
        var address = settings.Update(_valid with { Address = new string('x', PharmacySettings.AddressMaxLength + 1) });

        name.Error!.Code.ShouldBe(SettingsErrors.NameTooLong);
        name.Error.Arguments.ShouldBe([PharmacySettings.NameMaxLength]);
        phone.Error!.Code.ShouldBe(SettingsErrors.PhoneTooLong);
        address.Error!.Code.ShouldBe(SettingsErrors.AddressTooLong);
    }

    [Fact]
    public void Update_AtExactMaxLength_Succeeds()
    {
        var settings = PharmacySettings.CreateDefault();

        var result = settings.Update(_valid with { NameArabic = new string('ص', PharmacySettings.NameMaxLength) });

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Update_WithUndefinedEnums_Fails()
    {
        var settings = PharmacySettings.CreateDefault();

        settings.Update(_valid with { ThemeMode = (ThemeMode)99 }).Error!.Code.ShouldBe(SettingsErrors.InvalidThemeMode);
        settings.Update(_valid with { PrimaryColor = (ThemeColor)99 }).Error!.Code.ShouldBe(SettingsErrors.InvalidColor);
    }

    [Fact]
    public void Update_WhenInvalid_LeavesCurrentValuesUnchanged()
    {
        var settings = PharmacySettings.CreateDefault();
        settings.Update(_valid);

        settings.Update(_valid with { NameArabic = "", PrimaryColor = ThemeColor.Purple });

        settings.ToData().ShouldBe(_valid);
    }
}
