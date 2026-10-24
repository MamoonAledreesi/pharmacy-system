using MudBlazor;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Web.Components.Layout;

public static class PharmacyTheme
{
    // System fonts only: the app runs on a LAN and must not depend on internet font CDNs.
    // Segoe UI and Tahoma both cover Arabic and Latin scripts on Windows.
    private static readonly string[] _fontFamily = ["Segoe UI", "Tahoma", "Arial", "sans-serif"];

    // Each color has a darker shade for light mode and a lighter shade that stays readable on dark backgrounds.
    private static readonly Dictionary<ThemeColor, (string Light, string Dark)> _colors = new()
    {
        [ThemeColor.MedicalGreen] = ("#00796B", "#4DB6AC"),
        [ThemeColor.Blue] = ("#1565C0", "#64B5F6"),
        [ThemeColor.Indigo] = ("#3949AB", "#7986CB"),
        [ThemeColor.Purple] = ("#6A1B9A", "#BA68C8"),
        [ThemeColor.Teal] = ("#00838F", "#4DD0E1"),
        [ThemeColor.BurntOrange] = ("#D84315", "#FF8A65"),
    };

    private static readonly Dictionary<ThemeColor, MudTheme> _themes =
        _colors.Keys.ToDictionary(color => color, Build);

    public static MudTheme For(ThemeColor color) =>
        _themes.TryGetValue(color, out var theme) ? theme : _themes[ThemeColor.MedicalGreen];

    /// <summary>The light-mode shade, used for the color swatches on the settings page.</summary>
    public static string SwatchFor(ThemeColor color) => _colors[color].Light;

    private static MudTheme Build(ThemeColor color)
    {
        var (light, dark) = _colors[color];

        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = light,
                AppbarBackground = light,
            },
            PaletteDark = new PaletteDark
            {
                Primary = dark,
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = _fontFamily },
            },
        };
    }
}
