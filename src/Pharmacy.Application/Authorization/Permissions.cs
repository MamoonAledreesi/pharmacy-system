namespace Pharmacy.Application.Authorization;

/// <summary>
/// Fine-grained permissions, stored as role claims of type <see cref="ClaimType"/>.
/// Each value is "Module.Action"; the module prefix groups them in the role editor, and
/// "Permissions.{value}" is the localization key of its label.
/// Add a permission here together with the feature that checks it.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AuditLogView = "AuditLog.View";
    public const string CatalogView = "Catalog.View";
    public const string CatalogManage = "Catalog.Manage";
    public const string InventoryView = "Inventory.View";
    public const string InventoryViewCost = "Inventory.ViewCost";
    public const string InventoryOpeningStock = "Inventory.OpeningStock";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryWriteOff = "Inventory.WriteOff";

    public static IReadOnlyList<string> All { get; } =
        [
            CatalogView, CatalogManage,
            InventoryView, InventoryViewCost, InventoryOpeningStock, InventoryAdjust, InventoryWriteOff,
            UsersManage, SettingsManage, AuditLogView,
        ];

    public static bool IsDefined(string permission) => All.Contains(permission, StringComparer.Ordinal);

    public static string ModuleOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
