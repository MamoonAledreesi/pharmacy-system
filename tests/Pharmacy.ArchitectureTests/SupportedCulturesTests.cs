using System.Globalization;
using Pharmacy.Web.Localization;

namespace Pharmacy.ArchitectureTests;

public class SupportedCulturesTests
{
    [Fact]
    public void Default_IsArabic()
    {
        var options = SupportedCultures.CreateOptions();

        options.DefaultRequestCulture.UICulture.Name.ShouldBe(SupportedCultures.Arabic);
        options.SupportedUICultures!.Select(culture => culture.Name).ShouldBe(SupportedCultures.Names);
    }

    [Fact]
    public void Arabic_UsesGregorianCalendar()
    {
        var arabic = SupportedCultures.CreateOptions().DefaultRequestCulture.Culture;

        arabic.DateTimeFormat.Calendar.ShouldBeOfType<GregorianCalendar>();
        new DateTime(2026, 10, 24).ToString("yyyy-MM-dd", arabic).ShouldBe("2026-10-24");
    }

    [Fact]
    public void Arabic_ParsesAndFormatsPricesWithAPeriod()
    {
        var arabic = SupportedCultures.CreateOptions().DefaultRequestCulture.Culture;

        decimal.Parse("1,234.50", NumberStyles.Number, arabic).ShouldBe(1234.50m);
        12.5m.ToString("N2", arabic).ShouldBe("12.50");
    }

    [Fact]
    public void TextDirection_FollowsCulture()
    {
        SupportedCultures.IsRightToLeft(new CultureInfo(SupportedCultures.Arabic)).ShouldBeTrue();
        SupportedCultures.IsRightToLeft(new CultureInfo(SupportedCultures.English)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("EN", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsSupported_AcceptsOnlyArabicAndEnglish(string? name, bool expected)
    {
        SupportedCultures.IsSupported(name).ShouldBe(expected);
    }
}
