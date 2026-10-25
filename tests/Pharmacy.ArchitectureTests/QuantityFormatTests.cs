using Pharmacy.Application.Catalog;
using Pharmacy.Web.Common;

namespace Pharmacy.ArchitectureTests;

public class QuantityFormatTests
{
    private static readonly ProductUnitSummary _strip = new(1, new LocalizedName("شريط", "Strip"), true, 1, null, null);
    private static readonly ProductUnitSummary _box = new(2, new LocalizedName("علبة", "Box"), false, 3, null, null);
    private static readonly ProductUnitSummary _pack = new(3, new LocalizedName("باكت", "Pack"), false, 30, null, null);

    [Theory]
    [InlineData(7, "2 Box + 1 Strip")]
    [InlineData(6, "2 Box")]
    [InlineData(2, "2 Strip")]
    [InlineData(0, "0 Strip")]
    public void ShowsTheQuantityInTheLargestUnitsFirst(int baseQuantity, string expected)
    {
        QuantityFormat.Describe(baseQuantity, [_strip, _box], arabic: false).ShouldBe(expected);
    }

    [Fact]
    public void WorksWithMoreThanTwoUnits_InAnyOrder()
    {
        QuantityFormat.Describe(65, [_box, _pack, _strip], arabic: false).ShouldBe("2 Pack + 1 Box + 2 Strip");
    }

    [Fact]
    public void ReadsNaturallyInArabic()
    {
        QuantityFormat.Describe(7, [_strip, _box], arabic: true).ShouldBe("2 علبة و1 شريط");
    }

    [Fact]
    public void AProductWithOneUnit_IsJustAQuantity()
    {
        QuantityFormat.Describe(7, [_strip], arabic: false).ShouldBe("7 Strip");
    }
}
