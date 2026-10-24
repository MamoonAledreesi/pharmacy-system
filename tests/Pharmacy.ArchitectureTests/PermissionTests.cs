using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Pharmacy.Application.Authorization;
using Pharmacy.Web.Authentication;

namespace Pharmacy.ArchitectureTests;

public class PermissionTests
{
    [Fact]
    public void Administrator_HasEveryPermission()
    {
        var administrator = SignedIn(new Claim(ClaimTypes.Role, SystemRoles.Administrator));

        Permissions.All.ShouldAllBe(permission => PermissionEvaluator.Has(administrator, permission));
    }

    [Fact]
    public void OtherRoles_HaveOnlyGrantedPermissions()
    {
        var cashier = SignedIn(
            new Claim(ClaimTypes.Role, SystemRoles.Cashier),
            new Claim(Permissions.ClaimType, Permissions.AuditLogView));

        PermissionEvaluator.Has(cashier, Permissions.AuditLogView).ShouldBeTrue();
        PermissionEvaluator.Has(cashier, Permissions.SettingsManage).ShouldBeFalse();
    }

    [Fact]
    public void AnonymousUser_HasNoPermission_EvenWithAClaim()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Permissions.ClaimType, Permissions.UsersManage)]));

        PermissionEvaluator.Has(anonymous, Permissions.UsersManage).ShouldBeFalse();
    }

    [Fact]
    public async Task AuthorizationHandler_UsesTheSameRule()
    {
        var requirement = new PermissionRequirement(Permissions.SettingsManage);
        var granted = new AuthorizationHandlerContext(
            [requirement], SignedIn(new Claim(Permissions.ClaimType, Permissions.SettingsManage)), null);
        var denied = new AuthorizationHandlerContext([requirement], SignedIn(), null);
        var handler = new PermissionAuthorizationHandler();

        await handler.HandleAsync(granted);
        await handler.HandleAsync(denied);

        granted.HasSucceeded.ShouldBeTrue();
        denied.HasSucceeded.ShouldBeFalse();
    }

    [Fact]
    public void Permissions_AreUniqueAndNamedModuleDotAction()
    {
        Permissions.All.ShouldBeUnique();
        Permissions.All.ShouldAllBe(p => p.Split('.', StringSplitOptions.None).Length == 2);
        Permissions.ModuleOf(Permissions.UsersManage).ShouldBe("Users");
    }

    [Fact]
    public void CurrentUserAccessor_ReadsIdNameAndPermissions()
    {
        var accessor = new CurrentUserAccessor();
        accessor.UserId.ShouldBeNull();

        accessor.SetPrincipal(SignedIn(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "ahmed"),
            new Claim(Permissions.ClaimType, Permissions.AuditLogView)));

        accessor.UserId.ShouldBe(42);
        accessor.UserName.ShouldBe("ahmed");
        accessor.HasPermission(Permissions.AuditLogView).ShouldBeTrue();
        accessor.HasPermission(Permissions.UsersManage).ShouldBeFalse();
    }

    private static ClaimsPrincipal SignedIn(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
