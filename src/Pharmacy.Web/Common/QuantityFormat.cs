using Pharmacy.Application.Catalog;
using Pharmacy.Web.Localization;

namespace Pharmacy.Web.Common;

public static class QuantityFormat
{
    /// <summary>
    /// A base-unit quantity in the product's own packaging, largest unit first:
    /// 7 strips of a product whose box holds 3 strips reads "2 Box + 1 Strip".
    /// A single-unit product, or a quantity smaller than every larger unit, is just "7 Strip".
    /// </summary>
    public static string Describe(int baseQuantity, IReadOnlyList<ProductUnitSummary> units, bool arabic)
    {
        var baseUnit = units.FirstOrDefault(u => u.IsBase);
        if (baseUnit is null)
        {
            return baseQuantity.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        var remaining = baseQuantity;
        var parts = new List<string>();
        foreach (var unit in units.Where(u => !u.IsBase).OrderByDescending(u => u.Factor))
        {
            var count = remaining / unit.Factor;
            if (count > 0)
            {
                parts.Add($"{count} {unit.Unit.For(arabic)}");
                remaining -= count * unit.Factor;
            }
        }

        if (remaining > 0 || parts.Count == 0)
        {
            parts.Add($"{remaining} {baseUnit.Unit.For(arabic)}");
        }

        return string.Join(arabic ? " و" : " + ", parts);
    }

    /// <summary>The quantity in the packaging of the current UI language.</summary>
    public static string Describe(int baseQuantity, IReadOnlyList<ProductUnitSummary> units) =>
        Describe(baseQuantity, units, SupportedCultures.IsArabicUi);
}
