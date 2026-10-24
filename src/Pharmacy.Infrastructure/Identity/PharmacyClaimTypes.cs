namespace Pharmacy.Infrastructure.Identity;

/// <summary>Custom claims added to the sign-in cookie by <see cref="PharmacyUserClaimsPrincipalFactory"/>.</summary>
public static class PharmacyClaimTypes
{
    public const string FullName = "pharmacy:full_name";

    /// <summary>Present (value "true") while the user still has a temporary password.</summary>
    public const string MustChangePassword = "pharmacy:must_change_password";
}
