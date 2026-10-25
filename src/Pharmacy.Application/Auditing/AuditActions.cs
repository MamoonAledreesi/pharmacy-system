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

    public static IReadOnlyList<string> All { get; } =
    [
        LoginSucceeded, LoginFailed, LockedOut, Logout, PasswordChanged, SetupCompleted,
        UserCreated, UserUpdated, UserPasswordReset, UserUnlocked,
        RoleCreated, RoleUpdated, RoleDeleted, SettingsUpdated,
        ProductCreated, ProductUpdated, ProductStatusChanged, LookupCreated, LookupUpdated,
        StockOpening, StockAdjusted, StockQuarantined, StockReleased, StockWrittenOff,
        CashShiftOpened, CashShiftClosed, CashShiftForceClosed, CashManualMovement,
    ];
}
