namespace Pharmacy.Domain.Inventory;

public enum ExpiryStatus
{
    /// <summary>The product has no expiry date (it is not expiry-tracked).</summary>
    NoExpiry = 0,
    Ok = 1,
    NearExpiry = 2,
    Expired = 3,
}

/// <summary>
/// Expiry rules. An expiry date is the last day the batch may be sold: a batch is expired
/// only after that day has ended, so it is still sellable on the expiry date itself.
/// </summary>
public static class ExpiryRules
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>Packaging usually prints only month and year; such a batch expires at the end of that month.</summary>
    public static DateOnly EndOfMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(year, MinYear);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, MaxYear);

        return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
    }

    public static bool IsExpired(DateOnly? expiryDate, DateOnly today) => expiryDate is { } date && date < today;

    /// <summary>
    /// Near expiry means sellable but expiring within <paramref name="nearExpiryDays"/> days (inclusive);
    /// the threshold is the product's own setting when it has one, otherwise the pharmacy-wide setting.
    /// </summary>
    public static bool IsNearExpiry(DateOnly? expiryDate, DateOnly today, int nearExpiryDays) =>
        expiryDate is { } date && date >= today && date.DayNumber - today.DayNumber <= nearExpiryDays;

    public static ExpiryStatus StatusOf(DateOnly? expiryDate, DateOnly today, int nearExpiryDays)
    {
        if (expiryDate is null)
        {
            return ExpiryStatus.NoExpiry;
        }

        if (IsExpired(expiryDate, today))
        {
            return ExpiryStatus.Expired;
        }

        return IsNearExpiry(expiryDate, today, nearExpiryDays) ? ExpiryStatus.NearExpiry : ExpiryStatus.Ok;
    }
}
