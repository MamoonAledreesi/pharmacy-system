using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Inventory;

/// <summary>
/// A received lot of one product, identified by batch number, expiry date, and the actual cost of one
/// base unit. The same batch number received again at a different cost is a separate batch, so the cost of
/// every unit sold stays exact. Quantities are always in the product's base unit and can never go negative.
/// </summary>
public sealed class Batch
{
    public const int BatchNumberMaxLength = 50;

    /// <summary>Largest quantity of one operation; keeps balance arithmetic far from int overflow.</summary>
    public const int MaxOperationQuantity = 100_000_000;

    public const decimal MaxUnitCost = 1_000_000_000m;
    public const int UnitCostDecimals = 4;

    // Used by EF Core.
    private Batch()
    {
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public string BatchNumber { get; private set; } = string.Empty;

    /// <summary>The last day the batch may be sold; null for products that do not expire.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Actual purchase cost of one base unit.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Sellable quantity in base units (it includes expired stock until that is written off).</summary>
    public int QuantityAvailable { get; private set; }

    /// <summary>Quantity set aside in quarantine; never sold.</summary>
    public int QuantityQuarantined { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Result<Batch> Create(int productId, string? batchNumber, DateOnly? expiryDate, decimal unitCost, DateTime receivedAtUtc)
    {
        var number = batchNumber?.Trim() ?? string.Empty;
        if (number.Length == 0)
        {
            return Result.Failure<Batch>(new Error(InventoryErrors.BatchNumberRequired));
        }

        if (number.Length > BatchNumberMaxLength)
        {
            return Result.Failure<Batch>(new Error(InventoryErrors.BatchNumberTooLong, BatchNumberMaxLength));
        }

        if (!IsValidUnitCost(unitCost))
        {
            return Result.Failure<Batch>(new Error(InventoryErrors.InvalidUnitCost));
        }

        if (expiryDate is { } expiry && (expiry.Year < ExpiryRules.MinYear || expiry.Year > ExpiryRules.MaxYear))
        {
            return Result.Failure<Batch>(new Error(InventoryErrors.ExpiryOutOfRange, ExpiryRules.MinYear, ExpiryRules.MaxYear));
        }

        return Result.Success(new Batch
        {
            ProductId = productId,
            BatchNumber = number,
            ExpiryDate = expiryDate,
            UnitCost = unitCost,
            ReceivedAtUtc = receivedAtUtc,
        });
    }

    /// <summary>Cost of one base unit from the cost of a larger unit, rounded to the stored precision.</summary>
    public static decimal UnitCostFromPackCost(decimal packCost, int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        return decimal.Round(packCost / factor, UnitCostDecimals, MidpointRounding.AwayFromZero);
    }

    public static bool IsValidUnitCost(decimal unitCost) =>
        unitCost is >= 0 and <= MaxUnitCost && decimal.Round(unitCost, UnitCostDecimals) == unitCost;

    /// <summary>Adds sellable stock: opening stock, a purchase, or a positive adjustment.</summary>
    public Result<StockMovement> Receive(StockMovementType type, int quantity, MovementContext context)
    {
        if (type is not (StockMovementType.OpeningStock or StockMovementType.Purchase or StockMovementType.AdjustmentIncrease))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a stock receipt type.");
        }

        return Apply(type, quantity, availableDelta: quantity, quarantineDelta: 0, context);
    }

    /// <summary>Removes sellable stock: a sale, a negative adjustment, or a write-off.</summary>
    public Result<StockMovement> Deduct(StockMovementType type, int quantity, MovementContext context)
    {
        if (type is not (StockMovementType.Sale or StockMovementType.AdjustmentDecrease or StockMovementType.WriteOff))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a stock deduction type.");
        }

        return Apply(type, quantity, availableDelta: -quantity, quarantineDelta: 0, context);
    }

    /// <summary>
    /// Takes goods back from a customer: onto the shelf, or into quarantine when they are damaged or expired.
    /// </summary>
    public Result<StockMovement> ReturnFromCustomer(int quantity, bool toQuarantine, MovementContext context) =>
        toQuarantine
            ? Apply(StockMovementType.SaleReturnQuarantined, quantity, availableDelta: 0, quarantineDelta: quantity, context)
            : Apply(StockMovementType.SaleReturn, quantity, availableDelta: quantity, quarantineDelta: 0, context);

    /// <summary>Sets sellable stock aside so it can no longer be sold.</summary>
    public Result<StockMovement> Quarantine(int quantity, MovementContext context) =>
        Apply(StockMovementType.QuarantineIn, quantity, availableDelta: -quantity, quarantineDelta: quantity, context);

    /// <summary>Returns quarantined stock to sellable stock.</summary>
    public Result<StockMovement> Release(int quantity, MovementContext context) =>
        Apply(StockMovementType.QuarantineOut, quantity, availableDelta: quantity, quarantineDelta: -quantity, context);

    public Result<StockMovement> WriteOffQuarantined(int quantity, MovementContext context) =>
        Apply(StockMovementType.WriteOffQuarantined, quantity, availableDelta: 0, quarantineDelta: -quantity, context);

    private Result<StockMovement> Apply(
        StockMovementType type, int quantity, int availableDelta, int quarantineDelta, MovementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (quantity <= 0)
        {
            return Result.Failure<StockMovement>(new Error(InventoryErrors.InvalidQuantity));
        }

        if (quantity > MaxOperationQuantity)
        {
            return Result.Failure<StockMovement>(new Error(InventoryErrors.QuantityTooLarge, MaxOperationQuantity));
        }

        var available = (long)QuantityAvailable + availableDelta;
        var quarantined = (long)QuantityQuarantined + quarantineDelta;

        if (available < 0)
        {
            return Result.Failure<StockMovement>(new Error(InventoryErrors.InsufficientStock, QuantityAvailable));
        }

        if (quarantined < 0)
        {
            return Result.Failure<StockMovement>(new Error(InventoryErrors.InsufficientQuarantined, QuantityQuarantined));
        }

        if (available > int.MaxValue || quarantined > int.MaxValue)
        {
            return Result.Failure<StockMovement>(new Error(InventoryErrors.QuantityTooLarge, MaxOperationQuantity));
        }

        QuantityAvailable = (int)available;
        QuantityQuarantined = (int)quarantined;
        return Result.Success(new StockMovement(this, type, availableDelta, quarantineDelta, context));
    }
}
