namespace Pharmacy.Application.Auditing;

/// <summary>Audit action codes. "AuditActions.{code}" is the localization key of each label.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string LockedOut = "Auth.LockedOut";
    public const string Logout = "Auth.Logout";
    public const string PasswordChanged = "Auth.PasswordChanged";

    public const string SetupCompleted = "Setup.Completed";

    public const string UserCreated = "Users.Created";
    public const string UserUpdated = "Users.Updated";
    public const string UserPasswordReset = "Users.PasswordReset";
    public const string UserUnlocked = "Users.Unlocked";

    public const string RoleCreated = "Roles.Created";
    public const string RoleUpdated = "Roles.Updated";
    public const string RoleDeleted = "Roles.Deleted";

    public const string SettingsUpdated = "Settings.Updated";

    public const string ProductCreated = "Catalog.ProductCreated";
    public const string ProductUpdated = "Catalog.ProductUpdated";
    public const string ProductStatusChanged = "Catalog.ProductStatusChanged";
    public const string LookupCreated = "Catalog.LookupCreated";
    public const string LookupUpdated = "Catalog.LookupUpdated";

    public const string StockOpening = "Inventory.OpeningStock";
    public const string StockAdjusted = "Inventory.Adjusted";
    public const string StockQuarantined = "Inventory.Quarantined";
    public const string StockReleased = "Inventory.Released";
    public const string StockWrittenOff = "Inventory.WrittenOff";

    public const string CashShiftOpened = "Cash.ShiftOpened";
    public const string CashShiftClosed = "Cash.ShiftClosed";
    public const string CashShiftForceClosed = "Cash.ShiftForceClosed";
    public const string CashManualMovement = "Cash.ManualMovement";

    public const string SupplierCreated = "Purchasing.SupplierCreated";
    public const string SupplierUpdated = "Purchasing.SupplierUpdated";
    public const string PurchaseCreated = "Purchasing.PurchaseCreated";
    public const string SupplierPaid = "Payables.SupplierPaid";
    public const string SupplierPaymentReversed = "Payables.PaymentReversed";
    public const string SupplierOpeningBalance = "Payables.OpeningBalance";

    public const string CustomerCreated = "Receivables.CustomerCreated";
    public const string CustomerUpdated = "Receivables.CustomerUpdated";
    public const string DebtCollected = "Receivables.DebtCollected";
    public const string CollectionReversed = "Receivables.CollectionReversed";
    public const string CustomerOpeningBalance = "Receivables.OpeningBalance";

    public const string SaleCreated = "Sales.SaleCreated";
    public const string SaleBelowCost = "Sales.BelowCost";
    public const string SaleHighDiscount = "Sales.HighDiscount";
    public const string SaleReturned = "Sales.Returned";
    public const string SaleLateReturn = "Sales.LateReturn";

    public static IReadOnlyList<string> All { get; } =
    [
        LoginSucceeded, LoginFailed, LockedOut, Logout, PasswordChanged, SetupCompleted,
        UserCreated, UserUpdated, UserPasswordReset, UserUnlocked,
        RoleCreated, RoleUpdated, RoleDeleted, SettingsUpdated,
        ProductCreated, ProductUpdated, ProductStatusChanged, LookupCreated, LookupUpdated,
        StockOpening, StockAdjusted, StockQuarantined, StockReleased, StockWrittenOff,
        CashShiftOpened, CashShiftClosed, CashShiftForceClosed, CashManualMovement,
        SupplierCreated, SupplierUpdated, PurchaseCreated, SupplierPaid, SupplierPaymentReversed, SupplierOpeningBalance,
        CustomerCreated, CustomerUpdated, DebtCollected, CollectionReversed, CustomerOpeningBalance,
        SaleCreated, SaleBelowCost, SaleHighDiscount, SaleReturned, SaleLateReturn,
    ];
}
