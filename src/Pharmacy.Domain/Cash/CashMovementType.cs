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
}

public static class CashMovementTypes
{
    /// <summary>Types that bring cash into the drawer.</summary>
    public static IReadOnlySet<CashMovementType> Receipts { get; } = new HashSet<CashMovementType> { CashMovementType.ManualIn };

    /// <summary>Types that take cash out of the drawer.</summary>
    public static IReadOnlySet<CashMovementType> Payments { get; } = new HashSet<CashMovementType> { CashMovementType.ManualOut };
}
