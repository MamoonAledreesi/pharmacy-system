namespace Pharmacy.Application.Common;

/// <summary>Error codes shared by all modules. Each code is a localization key in the UI resources.</summary>
public static class CommonErrors
{
    /// <summary>Another user saved the same record after it was loaded (optimistic concurrency).</summary>
    public const string ConcurrencyConflict = "Errors.ConcurrencyConflict";
}
