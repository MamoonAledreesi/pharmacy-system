using System.Security.Claims;

namespace Pharmacy.Application.Authorization;

/// <summary>Single permission rule shared by UI authorization policies and service-level checks.</summary>
public static class PermissionEvaluator
{
    public static bool Has(ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.Identity?.IsAuthenticated == true
            && (principal.IsInRole(SystemRoles.Administrator)
                || principal.HasClaim(Permissions.ClaimType, permission));
    }
}
