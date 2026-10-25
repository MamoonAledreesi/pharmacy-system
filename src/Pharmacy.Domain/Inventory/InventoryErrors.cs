namespace Pharmacy.Domain.Inventory;

/// <summary>Inventory error codes. Each code is a localization key in the UI resources.</summary>
public static class InventoryErrors
{
    public const string InvalidQuantity = "Inventory.InvalidQuantity";
    public const string QuantityTooLarge = "Inventory.QuantityTooLarge";
    public const string InvalidUnitCost = "Inventory.InvalidUnitCost";
    public const string BatchNumberRequired = "Inventory.BatchNumberRequired";
    public const string BatchNumberTooLong = "Inventory.BatchNumberTooLong";
    public const string ExpiryRequired = "Inventory.ExpiryRequired";
    public const string ExpiryOutOfRange = "Inventory.ExpiryOutOfRange";

    /// <summary>Not enough sellable stock. Argument 0 is the sellable quantity that is available.</summary>
    public const string InsufficientStock = "Inventory.InsufficientStock";
    public const string InsufficientQuarantined = "Inventory.InsufficientQuarantined";

    public const string UnknownProduct = "Inventory.UnknownProduct";
    public const string InactiveProduct = "Inventory.InactiveProduct";
    public const string UnknownBatch = "Inventory.UnknownBatch";
    public const string UnknownUnit = "Inventory.UnknownUnit";
    public const string ReasonRequired = "Inventory.ReasonRequired";
    public const string UnknownReason = "Inventory.UnknownReason";
    public const string NoteRequired = "Inventory.NoteRequired";
    public const string NoteTooLong = "Inventory.NoteTooLong";
}
