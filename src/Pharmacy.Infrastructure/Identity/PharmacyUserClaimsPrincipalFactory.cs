using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Pharmacy.Infrastructure.Identity;

/// <summary>
/// Builds the signed-in principal: user name, roles, and the roles' permission claims (from the base
/// factory), plus the display name and the temporary-password flag.
/// </summary>
public sealed class PharmacyUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(PharmacyClaimTypes.FullName, user.FullName));

        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(PharmacyClaimTypes.MustChangePassword, "true"));
        }

        return identity;
    }
}
