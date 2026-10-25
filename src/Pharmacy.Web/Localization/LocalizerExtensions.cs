using Microsoft.Extensions.Localization;
using Pharmacy.Domain.Common;

namespace Pharmacy.Web.Localization;

public static class LocalizerExtensions
{
    /// <summary>The localized message for a business error (its code is the resource key).</summary>
    public static string Message(this IStringLocalizer localizer, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return localizer[error.Code, [.. error.Arguments]];
    }

    /// <summary>Built-in roles have translated names; roles created by administrators show as typed.</summary>
    public static string RoleName(this IStringLocalizer localizer, string roleName)
    {
        var localized = localizer[$"RoleNames.{roleName}"];
        return localized.ResourceNotFound ? roleName : localized.Value;
    }

    /// <summary>A UTC timestamp as a local date only (for example when a batch was received).</summary>
    public static string LocalDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>UTC timestamps are shown in the server's local time zone (the pharmacy's own LAN server).</summary>
    public static string LocalTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
}
