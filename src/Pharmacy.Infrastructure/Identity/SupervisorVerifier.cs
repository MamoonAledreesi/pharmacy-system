using Microsoft.AspNetCore.Identity;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Sales;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Infrastructure.Identity;

internal sealed class SupervisorVerifier(
    UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager) : ISupervisorVerifier
{
    private static readonly Error _notAccepted = new(SalesErrors.SupervisorNotAccepted);

    public async Task<Result<SupervisorIdentity>> VerifyAsync(
        string? userName, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return Result.Failure<SupervisorIdentity>(_notAccepted);
        }

        var user = await userManager.FindByNameAsync(userName.Trim());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            return Result.Failure<SupervisorIdentity>(_notAccepted);
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Guessing a supervisor password counts toward the same lockout as guessing it at the sign-in page.
            await userManager.AccessFailedAsync(user);
            return Result.Failure<SupervisorIdentity>(_notAccepted);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleName in roles)
        {
            if (await roleManager.FindByNameAsync(roleName) is { } role)
            {
                var claims = await roleManager.GetClaimsAsync(role);
                permissions.UnionWith(claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value));
            }
        }

        return Result.Success(new SupervisorIdentity(
            user.Id, user.UserName ?? userName.Trim(), roles.Contains(SystemRoles.Administrator), permissions));
    }
}
