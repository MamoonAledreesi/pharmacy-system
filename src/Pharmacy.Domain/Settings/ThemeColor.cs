namespace Pharmacy.Domain.Settings;

/// <summary>
/// Curated primary colors. A fixed palette (rather than a free color picker) keeps text readable
/// in both light and dark modes. The UI maps each value to concrete color codes.
/// </summary>
public enum ThemeColor
{
    MedicalGreen = 0,
    Blue = 1,
    Indigo = 2,
    Purple = 3,
    Teal = 4,
    BurntOrange = 5,
}
