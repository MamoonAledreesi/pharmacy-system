using Pharmacy.Domain.Settings;

namespace Pharmacy.Domain.Tests.Settings;

public class NearExpirySettingTests
{
    [Fact]
    public void TheDefaultIsNinetyDays()
    {
        PharmacySettings.Defaults.NearExpiryDays.ShouldBe(90);
        PharmacySettings.CreateDefault().NearExpiryDays.ShouldBe(90);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    [InlineData(PharmacySettings.MaxNearExpiryDays)]
    public void ValidValuesAreStored(int days)
    {
        var settings = PharmacySettings.CreateDefault();

        settings.Update(PharmacySettings.Defaults with { NearExpiryDays = days }).IsSuccess.ShouldBeTrue();

        settings.NearExpiryDays.ShouldBe(days);
        settings.ToData().NearExpiryDays.ShouldBe(days);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(PharmacySettings.MaxNearExpiryDays + 1)]
    public void OutOfRangeValuesAreRejected_AndNothingChanges(int days)
    {
        var settings = PharmacySettings.CreateDefault();

        var result = settings.Update(PharmacySettings.Defaults with { NameEnglish = "Changed", NearExpiryDays = days });

        result.Error!.Code.ShouldBe(SettingsErrors.InvalidNearExpiryDays);
        settings.NearExpiryDays.ShouldBe(90);
        settings.NameEnglish.ShouldBe(PharmacySettings.Defaults.NameEnglish);
    }
}
