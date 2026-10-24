using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Users;
using Pharmacy.Infrastructure.Identity;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Users;

public sealed class AccountServiceTests : IAsyncLifetime
{
    private const string Password = "Admin1234";
    private readonly TestServices _services = new();

    public Task InitializeAsync() => _services.InitializeAsync();

    public Task DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task Setup_CreatesTheFirstAdministratorOnce()
    {
        (await IsSetupRequiredAsync()).ShouldBeTrue();

        var first = await CompleteSetupAsync("admin", "مدير النظام", Password);
        var second = await CompleteSetupAsync("admin2", "Second", Password);

        first.IsSuccess.ShouldBeTrue();
        second.Error!.Code.ShouldBe(UserErrors.SetupAlreadyCompleted);
        (await IsSetupRequiredAsync()).ShouldBeFalse();

        await _services.InScopeAsync(async s =>
        {
            var users = s.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByNameAsync("admin"))!;
            (await users.IsInRoleAsync(admin, SystemRoles.Administrator)).ShouldBeTrue();
            admin.MustChangePassword.ShouldBeFalse();
            (await users.Users.CountAsync()).ShouldBe(1);

            var audit = await s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.SingleAsync();
            audit.Action.ShouldBe(AuditActions.SetupCompleted);
            audit.UserId.ShouldBe(admin.Id);
        });
    }

    [Fact]
    public async Task Setup_ConcurrentAttempts_CreateExactlyOneUser()
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(1, 4).Select(i => CompleteSetupAsync($"admin{i}", $"Admin {i}", Password)));

        attempts.Count(r => r.IsSuccess).ShouldBe(1);
        (await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Users.CountAsync())).ShouldBe(1);
    }

    [Theory]
    [InlineData("short1", IdentityErrors.PasswordTooShort)]
    [InlineData("NoDigitsHere", IdentityErrors.PasswordRequiresDigit)]
    [InlineData("1234567890", IdentityErrors.PasswordRequiresLetter)]
    public async Task Setup_RejectsWeakPasswords_AndLeavesNoUserBehind(string password, string expectedCode)
    {
        var result = await CompleteSetupAsync("admin", "Admin", password);

        result.Error!.Code.ShouldBe(expectedCode);
        (await IsSetupRequiredAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_RejectsInvalidUserName()
    {
        var result = await CompleteSetupAsync("مدير", "Admin", Password);

        result.Error!.Code.ShouldBe(IdentityErrors.InvalidUserName);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Fails()
    {
        await CompleteSetupAsync("admin", "Admin", Password);
        var adminId = await UserIdAsync("admin");

        var result = await _services.InScopeAsync(s =>
            s.GetRequiredService<IAccountService>().ChangePasswordAsync(adminId, "Wrong1234", "NewPass5678"));

        result.Error!.Code.ShouldBe(UserErrors.CurrentPasswordIncorrect);
    }

    [Fact]
    public async Task ChangePassword_ClearsTheTemporaryPasswordFlag()
    {
        await CompleteSetupAsync("admin", "Admin", Password);
        var adminId = await UserIdAsync("admin");
        await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE [identity].Users SET MustChangePassword = 1"));

        var result = await _services.InScopeAsync(s =>
            s.GetRequiredService<IAccountService>().ChangePasswordAsync(adminId, Password, "NewPass5678"));

        result.IsSuccess.ShouldBeTrue();
        await _services.InScopeAsync(async s =>
        {
            var users = s.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await users.FindByIdAsync(adminId.ToString(System.Globalization.CultureInfo.InvariantCulture)))!;
            admin.MustChangePassword.ShouldBeFalse();
            (await users.CheckPasswordAsync(admin, "NewPass5678")).ShouldBeTrue();
        });
    }

    private Task<bool> IsSetupRequiredAsync() =>
        _services.InScopeAsync(s => s.GetRequiredService<IAccountService>().IsSetupRequiredAsync());

    private Task<Domain.Common.Result> CompleteSetupAsync(string userName, string fullName, string password) =>
        _services.InScopeAsync(s => s.GetRequiredService<IAccountService>().CompleteSetupAsync(userName, fullName, password));

    private Task<int> UserIdAsync(string userName) =>
        _services.InScopeAsync(async s => (await s.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName))!.Id);
}
