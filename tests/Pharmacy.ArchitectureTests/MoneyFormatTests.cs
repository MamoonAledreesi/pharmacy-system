using System.Globalization;
using Pharmacy.Web.Common;
using Pharmacy.Web.Localization;

namespace Pharmacy.ArchitectureTests;

public class MoneyFormatTests
{
    [Theory]
    [InlineData("en", 1250.5, "1,250.50")]
    [InlineData("en", -30, "-30.00")]
    [InlineData("ar", -30, "-30.00")]
    [InlineData("ar", 0, "0.00")]
    public void Format_ShowsTwoDecimals_AndALeadingMinusInBothLanguages(string culture, double amount, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = SupportedCultures.CreateOptions().SupportedCultures!.Single(c => c.Name == culture);

            MoneyFormat.Format((decimal)amount).ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatSigned_AddsAPlusOnlyToPositiveAmounts()
    {
        MoneyFormat.FormatSigned(25m).ShouldBe("+25.00");
        MoneyFormat.FormatSigned(-10m).ShouldBe("-10.00");
        MoneyFormat.FormatSigned(0m).ShouldBe("0.00");
    }
}
