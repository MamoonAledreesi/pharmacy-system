using Pharmacy.Domain.Catalog;

namespace Pharmacy.Domain.Tests.Catalog;

/// <summary>Once a product has stock, quantities are stored in base units, so the units' meaning is fixed.</summary>
public class ProductUnitLockTests
{
    private const int Strip = 3;
    private const int Box = 4;
    private const int Pack = 5;

    private static readonly DateTime _now = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private static readonly ProductDetails _details = new(
        "بنادول", "Panadol", null, null, null, null, 1, null, true, null, null);

    private static Product SavedProduct()
    {
        var product = Product.Create(_details,
        [
            new(null, Strip, true, 1, 15m, null),
            new(null, Box, false, 3, 40m, null),
        ], _now).Value;

        // Simulates what the database does when the product is saved: it gives the units their ids.
        var ids = 100;
        foreach (var unit in product.Units)
        {
            typeof(ProductUnit).GetProperty(nameof(ProductUnit.Id))!.SetValue(unit, ++ids);
        }

        return product;
    }

    private static ProductUnitData[] Existing(Product product, Func<ProductUnitData, ProductUnitData>? change = null) =>
        [.. product.Units.Select(u =>
        {
            var data = new ProductUnitData(u.Id, u.UnitOfMeasureId, u.IsBase, u.Factor, u.DefaultSellingPrice, u.Barcode);
            return change is null ? data : change(data);
        })];

    [Fact]
    public void PricesAndBarcodesCanStillChange()
    {
        var product = SavedProduct();

        var result = product.Update(_details, Existing(product, u => u with { DefaultSellingPrice = 99m, Barcode = $"B{u.Id}" }), unitsLocked: true);

        result.IsSuccess.ShouldBeTrue();
        product.Units.ShouldAllBe(u => u.DefaultSellingPrice == 99m);
    }

    [Fact]
    public void NewUnitsCanBeAdded()
    {
        var product = SavedProduct();
        ProductUnitData[] units = [.. Existing(product), new(null, Pack, false, 30, null, null)];

        product.Update(_details, units, unitsLocked: true).IsSuccess.ShouldBeTrue();

        product.Units.Count.ShouldBe(3);
    }

    [Fact]
    public void DescriptiveDetailsCanStillChange()
    {
        var product = SavedProduct();

        product.Update(_details with { TradeNameEnglish = "Panadol Extra" }, Existing(product), unitsLocked: true).IsSuccess.ShouldBeTrue();

        product.TradeNameEnglish.ShouldBe("Panadol Extra");
    }

    public static TheoryData<string> ForbiddenChanges => ["factor", "unit", "base", "remove"];

    [Theory]
    [MemberData(nameof(ForbiddenChanges))]
    public void ChangingTheMeaningOfAnExistingUnit_IsRejected_AndNothingChanges(string change)
    {
        var product = SavedProduct();
        var units = change switch
        {
            "factor" => Existing(product, u => u.IsBase ? u : u with { Factor = 6 }),
            "unit" => Existing(product, u => u.IsBase ? u : u with { UnitOfMeasureId = Pack }),
            "base" => Existing(product, u => u with { IsBase = !u.IsBase, Factor = u.IsBase ? 3 : 1 }),
            _ => [Existing(product)[0]],
        };

        var result = product.Update(_details with { TradeNameEnglish = "Changed" }, units, unitsLocked: true);

        result.Error!.Code.ShouldBe(CatalogErrors.UnitsLockedByStock);
        product.TradeNameEnglish.ShouldBe("Panadol");
        product.Units.Select(u => (u.UnitOfMeasureId, u.IsBase, u.Factor)).ShouldBe([(Strip, true, 1), (Box, false, 3)]);
    }

    [Fact]
    public void WithoutStock_TheSameChangesAreAllowed()
    {
        var product = SavedProduct();

        product.Update(_details, Existing(product, u => u.IsBase ? u : u with { Factor = 6 }), unitsLocked: false).IsSuccess.ShouldBeTrue();
    }
}
