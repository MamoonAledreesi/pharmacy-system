namespace Pharmacy.Domain.Catalog;

/// <summary>
/// A unit a product is bought and sold in, with its conversion to the product's base unit.
/// Stock is always kept in base units: quantity in base units = quantity × <see cref="Factor"/>.
/// </summary>
public sealed class ProductUnit
{
    public const int BarcodeMaxLength = 50;

    // Used by EF Core.
    private ProductUnit()
    {
    }

    internal ProductUnit(ProductUnitData data)
    {
        Apply(data);
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int UnitOfMeasureId { get; private set; }

    public bool IsBase { get; private set; }

    public int Factor { get; private set; }

    /// <summary>Suggested price per this unit; the user may enter any price on an invoice.</summary>
    public decimal? DefaultSellingPrice { get; private set; }

    /// <summary>Optional and unique across all products. Not required for selling.</summary>
    public string? Barcode { get; private set; }

    public int ToBaseQuantity(int quantity) => checked(quantity * Factor);

    internal void Apply(ProductUnitData data)
    {
        UnitOfMeasureId = data.UnitOfMeasureId;
        IsBase = data.IsBase;
        Factor = data.Factor;
        DefaultSellingPrice = data.DefaultSellingPrice;
        Barcode = data.Barcode;
    }
}
