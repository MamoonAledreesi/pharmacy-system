using System.Globalization;

namespace Pharmacy.Web.Common;

public static class MoneyFormat
{
    /// <summary>
    /// An amount with two decimals and thousands separators, for the current UI language ("1,250.50"). The sign is
    /// always leading: the Arabic culture would otherwise put the minus after the digits.
    /// </summary>
    public static string Format(decimal amount) =>
        (amount < 0 ? "-" : string.Empty) + Math.Abs(amount).ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>A movement amount with an explicit sign ("+25.00" / "-10.00").</summary>
    public static string FormatSigned(decimal amount) =>
        amount > 0 ? "+" + Format(amount) : Format(amount);

    /// <summary>The CSS class that colors a signed amount: green for money in, red for money out.</summary>
    public static string SignClass(decimal amount) =>
        amount > 0 ? "delta-positive" : amount < 0 ? "delta-negative" : string.Empty;
}
