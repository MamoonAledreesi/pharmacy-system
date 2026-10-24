using Pharmacy.Domain.Catalog;

namespace Pharmacy.Domain.Tests.Catalog;

public class ProductTests
{
    private const int Strip = 3;
    private const int Box = 4;
    private static readonly DateTime _now = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private static readonly ProductDetails _details = new(
        TradeNameArabic: "بنادول إكسترا",
        TradeNameEnglish: "Panadol Extra",
        ScientificName: "Paracetamol, Caffeine",
        Strength: "500/65 mg",
        Manufacturer: "GSK",
        DosageFormId: 1,
        CategoryId: 1,
        Notes: null,
        TracksExpiry: true,
        MinimumStock: 10,
        NearExpiryDays: null);

    // The pharmacy sells strips and boxes of 3 strips; the strip is the base unit.
    private static readonly ProductUnitData[] _stripAndBox =
    [
        new(null, Strip, IsBase: true, Factor: 1, DefaultSellingPrice: 15m, Barcode: null),
        new(null, Box, IsBase: false, Factor: 3, DefaultSellingPrice: 40m, Barcode: "6281000000017"),
    ];

    [Fact]
    public void Create_WithValidData_BuildsProductAndSearchText()
    {
        var product = Product.Create(_details, _stripAndBox, _now).Value;

        product.DisplayName(arabic: true).ShouldBe("بنادول إكسترا");
        product.IsActive.ShouldBeTrue();
        product.BaseUnit.UnitOfMeasureId.ShouldBe(Strip);
        product.Units.Count.ShouldBe(2);
        product.SearchText.ShouldBe("بنادول اكسترا panadol extra paracetamol, caffeine 6281000000017");
    }

    [Fact]
    public void ToBaseQuantity_UsesTheUnitFactor()
    {
        var product = Product.Create(_details, _stripAndBox, _now).Value;
        var box = product.Units.Single(u => u.UnitOfMeasureId == Box);

        box.ToBaseQuantity(2).ShouldBe(6);
        product.BaseUnit.ToBaseQuantity(2).ShouldBe(2);
    }

    [Theory]
    [InlineData(null, "Panadol", "Panadol")]
    [InlineData("بنادول", null, "بنادول")]
    public void DisplayName_FallsBackToTheOtherLanguage(string? arabic, string? english, string expected)
    {
        var product = Product.Create(_details with { TradeNameArabic = arabic, TradeNameEnglish = english }, _stripAndBox, _now).Value;

        product.DisplayName(arabic: true).ShouldBe(expected);
        product.DisplayName(arabic: false).ShouldBe(expected);
    }

    [Fact]
    public void Create_TrimsTextAndStoresBlanksAsNull()
    {
        var product = Product.Create(_details with { TradeNameArabic = "  بنادول  ", Manufacturer = "   " }, _stripAndBox, _now).Value;

        product.TradeNameArabic.ShouldBe("بنادول");
        product.Manufacturer.ShouldBeNull();
    }

    [Fact]
    public void ProductWithoutBarcode_IsValid()
    {
        var units = new[] { new ProductUnitData(null, Box, true, 1, null, "  ") };

        var product = Product.Create(_details, units, _now).Value;

        product.Units.Single().Barcode.ShouldBeNull();
    }

    public static TheoryData<ProductDetails, string> InvalidDetails => new()
    {
        { _details with { TradeNameArabic = " ", TradeNameEnglish = null }, CatalogErrors.TradeNameRequired },
        { _details with { CategoryId = 0 }, CatalogErrors.CategoryRequired },
        { _details with { MinimumStock = -1 }, CatalogErrors.InvalidMinimumStock },
        { _details with { NearExpiryDays = 0 }, CatalogErrors.InvalidNearExpiryDays },
        { _details with { NearExpiryDays = Product.MaxNearExpiryDays + 1 }, CatalogErrors.InvalidNearExpiryDays },
        { _details with { Strength = new string('x', Product.StrengthMaxLength + 1) }, CatalogErrors.FieldTooLong },
    };

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void Create_WithInvalidDetails_Fails(ProductDetails details, string expectedCode)
    {
        Product.Create(details, _stripAndBox, _now).Error!.Code.ShouldBe(expectedCode);
    }

    public static TheoryData<ProductUnitData[], string> InvalidUnits => new()
    {
        { [], CatalogErrors.UnitsRequired },
        { [new(null, Strip, false, 2, null, null)], CatalogErrors.ExactlyOneBaseUnit },
        { [new(null, Strip, true, 1, null, null), new(null, Box, true, 1, null, null)], CatalogErrors.ExactlyOneBaseUnit },
        { [new(null, Strip, true, 3, null, null)], CatalogErrors.BaseUnitFactorMustBeOne },
        { [new(null, Strip, true, 1, null, null), new(null, Box, false, 1, null, null)], CatalogErrors.UnitFactorTooSmall },
        { [new(null, Strip, true, 1, null, null), new(null, Strip, false, 3, null, null)], CatalogErrors.DuplicateUnit },
        { [new(null, Strip, true, 1, -1m, null)], CatalogErrors.InvalidPrice },
        { [new(null, Strip, true, 1, null, new string('9', ProductUnit.BarcodeMaxLength + 1))], CatalogErrors.BarcodeTooLong },
        { [new(null, Strip, true, 1, null, "123"), new(null, Box, false, 3, null, "123")], CatalogErrors.DuplicateBarcode },
    };

    [Theory]
    [MemberData(nameof(InvalidUnits))]
    public void Create_WithInvalidUnits_Fails(ProductUnitData[] units, string expectedCode)
    {
        Product.Create(_details, units, _now).Error!.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void Update_WhenInvalid_ChangesNothing()
    {
        var product = Product.Create(_details, _stripAndBox, _now).Value;

        var result = product.Update(_details with { TradeNameEnglish = "Changed", CategoryId = 0 }, []);

        result.IsFailure.ShouldBeTrue();
        product.TradeNameEnglish.ShouldBe("Panadol Extra");
        product.Units.Count.ShouldBe(2);
    }

    [Fact]
    public void Update_KeepsChangesAndRemovesUnits()
    {
        var product = Product.Create(_details, _stripAndBox, _now).Value;

        product.Update(_details, [new(null, Box, true, 1, 40m, null)]).IsSuccess.ShouldBeTrue();

        product.Units.Count.ShouldBe(1);
        product.BaseUnit.UnitOfMeasureId.ShouldBe(Box);
        product.SearchText.ShouldNotContain("6281000000017");
    }
}
