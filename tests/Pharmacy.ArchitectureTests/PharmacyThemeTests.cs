using Pharmacy.Domain.Settings;
using Pharmacy.Web.Components.Layout;

namespace Pharmacy.ArchitectureTests;

public class PharmacyThemeTests
{
    [Fact]
    public void EveryColor_HasItsOwnTheme()
    {
        var primaries = Enum.GetValues<ThemeColor>()
            .Select(color => PharmacyTheme.For(color).PaletteLight.Primary.ToString())
            .ToList();

        primaries.ShouldBeUnique();
    }

    [Fact]
    public void DarkPalette_UsesALighterShadeThanLightPalette()
    {
        foreach (var color in Enum.GetValues<ThemeColor>())
        {
            var theme = PharmacyTheme.For(color);
            theme.PaletteDark.Primary.ShouldNotBe(theme.PaletteLight.Primary, $"color {color}");
        }
    }

    [Fact]
    public void UnknownColor_FallsBackToDefault()
    {
        PharmacyTheme.For((ThemeColor)99).ShouldBeSameAs(PharmacyTheme.For(PharmacySettings.Defaults.PrimaryColor));
    }
}
