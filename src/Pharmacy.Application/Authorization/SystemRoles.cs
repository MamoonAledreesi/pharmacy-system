namespace Pharmacy.Application.Authorization;

/// <summary>Roles created by the first migration. Administrators may add more from the UI.</summary>
public static class SystemRoles
{
    /// <summary>Holds every permission implicitly and cannot be edited or deleted, so the system can never lock itself out.</summary>
    public const string Administrator = "Administrator";

    public const string Pharmacist = "Pharmacist";
    public const string Cashier = "Cashier";
    public const string Accountant = "Accountant";
    public const string StoreKeeper = "StoreKeeper";

    public static IReadOnlyList<string> Defaults { get; } = [Administrator, Pharmacist, Cashier, Accountant, StoreKeeper];
}
