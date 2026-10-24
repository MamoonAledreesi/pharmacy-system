using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Users;
using Pharmacy.Domain.Common;
using Pharmacy.Infrastructure.Identity;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Users;

public sealed class UserManagementServiceTests : IAsyncLifetime
{
    private const string Password = "Pass1234";
    private readonly TestServices _services = new();
    private int _adminId;

    public async Task InitializeAsync()
    {
        await _services.InitializeAsync();
        await _services.InScopeAsync(s => s.GetRequiredService<IAccountService>().CompleteSetupAsync("admin", "Admin", Password));
        _adminId = await UserIdAsync("admin");
        _services.CurrentUser.SignInAsAdministrator(_adminId);
    }

    public Task DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task EveryOperation_RequiresUsersManage()
    {
        _services.CurrentUser.SignInAs(99, "cashier", isAdministrator: false, Permissions.SettingsManage);

        var results = await _services.InScopeAsync(async s =>
        {
            var service = s.GetRequiredService<IUserManagementService>();
            return new Result[]
            {
                await service.GetUsersAsync(),
                await service.CreateUserAsync(new("x", "X", Password, [SystemRoles.Cashier])),
                await service.UpdateUserAsync(new(_adminId, "X", false, [SystemRoles.Cashier])),
                await service.ResetPasswordAsync(_adminId, Password),
                await service.UnlockAsync(_adminId),
                await service.GetRolesAsync(),
                await service.CreateRoleAsync("Role", []),
                await service.UpdateRoleAsync(2, "Role", []),
                await service.DeleteRoleAsync(2),
            };
        });

        results.ShouldAllBe(r => r.Error!.Code == CommonErrors.Forbidden);
    }

    [Fact]
    public async Task CreateUser_RequiresPasswordChangeAndIsAudited()
    {
        var result = await CreateUserAsync("cashier1", SystemRoles.Cashier);

        result.IsSuccess.ShouldBeTrue();
        var users = (await Service(s => s.GetUsersAsync())).Value;
        var cashier = users.Single(u => u.UserName == "cashier1");
        cashier.Roles.ShouldBe([SystemRoles.Cashier]);
        cashier.IsActive.ShouldBeTrue();
        (await LoadUserAsync("cashier1")).MustChangePassword.ShouldBeTrue();
        (await AuditActionsAsync()).ShouldContain(AuditActions.UserCreated);
    }

    [Fact]
    public async Task CreateUser_Validation()
    {
        (await CreateUserAsync("admin", SystemRoles.Cashier)).Error!.Code.ShouldBe(IdentityErrors.DuplicateUserName);
        (await CreateUserAsync("new1", "NoSuchRole")).Error!.Code.ShouldBe(UserErrors.UnknownRole);
        (await Service(s => s.CreateUserAsync(new("new2", "New", Password, [])))).Error!.Code.ShouldBe(UserErrors.RoleRequired);
        (await Service(s => s.CreateUserAsync(new("new3", " ", Password, [SystemRoles.Cashier])))).Error!.Code.ShouldBe(UserErrors.FullNameRequired);
        (await Service(s => s.CreateUserAsync(new("new4", "New", "weak", [SystemRoles.Cashier])))).Error!.Code.ShouldBe(IdentityErrors.PasswordTooShort);

        // Failed creations roll back completely.
        (await Service(s => s.GetUsersAsync())).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Administrator_CannotDeactivateOrDemoteSelf()
    {
        var deactivate = await Service(s => s.UpdateUserAsync(new(_adminId, "Admin", false, [SystemRoles.Administrator])));
        var demote = await Service(s => s.UpdateUserAsync(new(_adminId, "Admin", true, [SystemRoles.Cashier])));

        deactivate.Error!.Code.ShouldBe(UserErrors.CannotDeactivateSelf);
        demote.Error!.Code.ShouldBe(UserErrors.CannotRemoveOwnAdministratorRole);
    }

    [Fact]
    public async Task LastActiveAdministrator_CannotBeRemoved()
    {
        // A non-administrator who holds Users.Manage tries to deactivate the only administrator.
        await CreateUserAsync("manager", SystemRoles.Pharmacist);
        _services.CurrentUser.SignInAs(await UserIdAsync("manager"), "manager", isAdministrator: false, Permissions.UsersManage);

        var result = await Service(s => s.UpdateUserAsync(new(_adminId, "Admin", false, [SystemRoles.Administrator])));

        result.Error!.Code.ShouldBe(UserErrors.LastAdministrator);
        (await LoadUserAsync("admin")).IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task SecondAdministrator_CanBeDemotedWhileAnotherRemains()
    {
        await CreateUserAsync("admin2", SystemRoles.Administrator);
        var admin2 = await UserIdAsync("admin2");

        var result = await Service(s => s.UpdateUserAsync(new(admin2, "Admin 2", false, [SystemRoles.Administrator])));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ChangingAccess_EndsTheUsersSessions()
    {
        await CreateUserAsync("cashier1", SystemRoles.Cashier);
        var cashierId = await UserIdAsync("cashier1");
        var stampBefore = (await LoadUserAsync("cashier1")).SecurityStamp;

        await Service(s => s.UpdateUserAsync(new(cashierId, "Renamed only", true, [SystemRoles.Cashier])));
        var stampAfterRename = (await LoadUserAsync("cashier1")).SecurityStamp;
        await Service(s => s.UpdateUserAsync(new(cashierId, "Renamed only", false, [SystemRoles.Cashier])));
        var stampAfterDeactivate = (await LoadUserAsync("cashier1")).SecurityStamp;

        stampAfterRename.ShouldBe(stampBefore);
        stampAfterDeactivate.ShouldNotBe(stampBefore);
    }

    [Fact]
    public async Task ResetPassword_SetsTemporaryPassword()
    {
        await CreateUserAsync("cashier1", SystemRoles.Cashier);
        var cashierId = await UserIdAsync("cashier1");
        await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE [identity].Users SET MustChangePassword = 0 WHERE UserName = 'cashier1'"));

        var weak = await Service(s => s.ResetPasswordAsync(cashierId, "weak"));
        var reset = await Service(s => s.ResetPasswordAsync(cashierId, "Temp9876"));

        weak.Error!.Code.ShouldBe(IdentityErrors.PasswordTooShort);
        reset.IsSuccess.ShouldBeTrue();
        await _services.InScopeAsync(async s =>
        {
            var users = s.GetRequiredService<UserManager<ApplicationUser>>();
            var cashier = (await users.FindByNameAsync("cashier1"))!;
            cashier.MustChangePassword.ShouldBeTrue();
            (await users.CheckPasswordAsync(cashier, "Temp9876")).ShouldBeTrue();
            (await users.CheckPasswordAsync(cashier, Password)).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Unlock_ClearsLockout()
    {
        await CreateUserAsync("cashier1", SystemRoles.Cashier);
        var cashierId = await UserIdAsync("cashier1");
        await _services.InScopeAsync(async s =>
        {
            var users = s.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByNameAsync("cashier1"))!, DateTimeOffset.UtcNow.AddMinutes(15));
        });
        (await Service(s => s.GetUsersAsync())).Value.Single(u => u.Id == cashierId).IsLockedOut.ShouldBeTrue();

        (await Service(s => s.UnlockAsync(cashierId))).IsSuccess.ShouldBeTrue();

        (await Service(s => s.GetUsersAsync())).Value.Single(u => u.Id == cashierId).IsLockedOut.ShouldBeFalse();
    }

    [Fact]
    public async Task Roles_CreateUpdateDelete()
    {
        var created = await Service(s => s.CreateRoleAsync(" Supervisor ", [Permissions.AuditLogView]));
        created.IsSuccess.ShouldBeTrue();

        var updated = await Service(s => s.UpdateRoleAsync(created.Value, "Shift supervisor", [Permissions.AuditLogView, Permissions.SettingsManage]));
        updated.IsSuccess.ShouldBeTrue();

        var role = (await Service(s => s.GetRolesAsync())).Value.Single(r => r.Id == created.Value);
        role.Name.ShouldBe("Shift supervisor");
        role.Permissions.ShouldBe([Permissions.AuditLogView, Permissions.SettingsManage], ignoreOrder: true);

        (await Service(s => s.DeleteRoleAsync(created.Value))).IsSuccess.ShouldBeTrue();
        (await Service(s => s.GetRolesAsync())).Value.ShouldNotContain(r => r.Id == created.Value);
        (await AuditActionsAsync()).ShouldContain(AuditActions.RoleDeleted);
    }

    [Fact]
    public async Task Roles_Rules()
    {
        var administratorRoleId = (await Service(s => s.GetRolesAsync())).Value.Single(r => r.IsSystem).Id;
        await CreateUserAsync("cashier1", SystemRoles.Cashier);
        var cashierRoleId = (await Service(s => s.GetRolesAsync())).Value.Single(r => r.Name == SystemRoles.Cashier).Id;

        (await Service(s => s.UpdateRoleAsync(administratorRoleId, SystemRoles.Administrator, []))).Error!.Code.ShouldBe(UserErrors.SystemRoleReadOnly);
        (await Service(s => s.DeleteRoleAsync(administratorRoleId))).Error!.Code.ShouldBe(UserErrors.SystemRoleReadOnly);
        (await Service(s => s.DeleteRoleAsync(cashierRoleId))).Error!.Code.ShouldBe(UserErrors.RoleInUse);
        (await Service(s => s.CreateRoleAsync(SystemRoles.Cashier, []))).Error!.Code.ShouldBe(IdentityErrors.DuplicateRoleName);
        (await Service(s => s.CreateRoleAsync("Bad", ["Nope.Nope"]))).Error!.Code.ShouldBe(UserErrors.UnknownPermission);
        (await Service(s => s.CreateRoleAsync(" ", []))).Error!.Code.ShouldBe(UserErrors.RoleNameRequired);
    }

    [Fact]
    public async Task ChangingRolePermissions_EndsMembersSessions()
    {
        await CreateUserAsync("cashier1", SystemRoles.Cashier);
        var cashierRoleId = (await Service(s => s.GetRolesAsync())).Value.Single(r => r.Name == SystemRoles.Cashier).Id;
        var stampBefore = (await LoadUserAsync("cashier1")).SecurityStamp;

        await Service(s => s.UpdateRoleAsync(cashierRoleId, SystemRoles.Cashier, [Permissions.AuditLogView]));

        (await LoadUserAsync("cashier1")).SecurityStamp.ShouldNotBe(stampBefore);
    }

    [Fact]
    public async Task AdministratorRole_ReportsAllPermissions()
    {
        var administrator = (await Service(s => s.GetRolesAsync())).Value.Single(r => r.IsSystem);

        administrator.Name.ShouldBe(SystemRoles.Administrator);
        administrator.Permissions.ShouldBe(Permissions.All);
        administrator.UserCount.ShouldBe(1);
    }

    private Task<Result<int>> CreateUserAsync(string userName, string role) =>
        Service(s => s.CreateUserAsync(new(userName, $"Name of {userName}", Password, [role])));

    private Task<T> Service<T>(Func<IUserManagementService, Task<T>> call) =>
        _services.InScopeAsync(s => call(s.GetRequiredService<IUserManagementService>()));

    private Task<ApplicationUser> LoadUserAsync(string userName) =>
        _services.InScopeAsync(async s => await s.GetRequiredService<PharmacyDbContext>().Users.AsNoTracking().SingleAsync(u => u.UserName == userName));

    private Task<int> UserIdAsync(string userName) =>
        _services.InScopeAsync(async s => (await s.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName))!.Id);

    private Task<List<string>> AuditActionsAsync() =>
        _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.Select(e => e.Action).ToListAsync());
}
