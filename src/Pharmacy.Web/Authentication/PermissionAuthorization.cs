using Microsoft.AspNetCore.Authorization;
using Pharmacy.Application.Authorization;

namespace Pharmacy.Web.Authentication;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Grants a permission policy using the same rule as service-level checks (<see cref="PermissionEvaluator"/>).</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (PermissionEvaluator.Has(context.User, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class PermissionPolicies
{
    /// <summary>One policy per permission, named after it: <c>[Authorize(Policy = Permissions.UsersManage)]</c>.</summary>
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(permission, policy => policy.AddRequirements(new PermissionRequirement(permission)));
        }

        return builder;
    }
}
