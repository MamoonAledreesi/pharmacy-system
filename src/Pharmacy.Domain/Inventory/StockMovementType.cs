namespace Pharmacy.Domain.Inventory;

/// <summary>
/// Why stock changed. Stored by name, so new types can be added by later features
/// (purchase returns, sales returns, ...) without touching existing rows.
/// </summary>
public enum StockMovementType
{
    /// <summary>Stock present when the pharmacy started using the system.</summary>
    OpeningStock = 0,

    /// <summary>Goods received from a supplier (purchase invoices).</summary>
    Purchase = 1,

    /// <summary>Goods sold to a customer (sales invoices).</summary>
    Sale = 2,

    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,

    /// <summary>Sellable stock set aside (damaged, suspect); it can no longer be sold.</summary>
    QuarantineIn = 5,

    /// <summary>Quarantined stock returned to sellable stock.</summary>
    QuarantineOut = 6,

    /// <summary>Sellable stock destroyed or written off (for example expired stock).</summary>
    WriteOff = 7,

    /// <summary>Quarantined stock destroyed or written off.</summary>
    WriteOffQuarantined = 8,

    /// <summary>Goods a customer returned that go back on the shelf and can be sold again.</summary>
    SaleReturn = 9,

    /// <summary>Goods a customer returned that are damaged or expired; they are set aside in quarantine.</summary>
    SaleReturnQuarantined = 10,
}
