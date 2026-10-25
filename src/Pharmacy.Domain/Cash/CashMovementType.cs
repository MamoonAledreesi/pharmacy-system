namespace Pharmacy.Domain.Cash;

/// <summary>
/// Why cash entered or left the drawer. Stored by name, so later features (sales, debt collection,
/// supplier payments, expenses, refunds) add their own types without touching existing rows.
/// </summary>
public enum CashMovementType
{
    /// <summary>Cash put into the drawer by hand (owner deposit, change float, correction, ...).</summary>
    ManualIn = 0,

    /// <summary>Cash taken out of the drawer by hand (owner withdrawal, bank deposit, correction, ...).</summary>
    ManualOut = 1,

    /// <summary>Cash paid to a supplier for a purchase invoice.</summary>
    PurchasePayment = 2,

    /// <summary>Cash paid to a supplier against what the pharmacy owes.</summary>
    SupplierPayment = 3,

    /// <summary>A supplier payment that was entered by mistake and taken back.</summary>
    SupplierPaymentReversal = 4,

    /// <summary>Cash collected from a customer against what they owe.</summary>
    DebtCollection = 5,

    /// <summary>A debt collection that was entered by mistake and taken back.</summary>
    DebtCollectionReversal = 6,
}

public static class CashMovementTypes
{
    /// <summary>Types that bring cash into the drawer.</summary>
    public static IReadOnlySet<CashMovementType> Receipts { get; } = new HashSet<CashMovementType> { CashMovementType.ManualIn, CashMovementType.SupplierPaymentReversal, CashMovementType.DebtCollection };

    /// <summary>Types that take cash out of the drawer.</summary>
    public static IReadOnlySet<CashMovementType> Payments { get; } = new HashSet<CashMovementType> { CashMovementType.ManualOut, CashMovementType.PurchasePayment, CashMovementType.SupplierPayment, CashMovementType.DebtCollectionReversal };
}
