using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Web;

/// <summary>End-to-end sign-in behavior through real HTTP requests, forms, cookies, and antiforgery tokens.</summary>
public sealed class AuthenticationFlowTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    [Fact]
    public async Task AnonymousUser_IsSentToSignIn()
    {
        using var response = await factory.CreateBrowser().GetAsync("/settings");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.RedirectPath().ShouldBe("/Account/Login?ReturnUrl=%2Fsettings");
    }

    [Fact]
    public async Task ValidCredentials_SignInAndRedirectHome()
    {
        var browser = factory.CreateBrowser();

        using var response = await PharmacyWebFactory.SignInAsync(browser, PharmacyWebFactory.AdminUserName, PharmacyWebFactory.AdminPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.RedirectPath().ShouldBe("/");
        response.Headers.GetValues(HeaderNames.SetCookie).ShouldContain(c => c.StartsWith("Pharmacy.Auth=", StringComparison.Ordinal));
        (await browser.GetAsync("/")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WrongPassword_ShowsAGenericErrorAndIsAudited()
    {
        using var response = await PharmacyWebFactory.SignInAsync(factory.CreateBrowser(), PharmacyWebFactory.AdminUserName, "Wrong1234");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("اسم المستخدم أو كلمة المرور غير صحيحة.");
        html.ShouldNotContain("Wrong1234", customMessage: "the typed password must not be echoed back into the page");
        (await AuditActionsForAsync(PharmacyWebFactory.AdminUserName)).ShouldContain(AuditActions.LoginFailed);
    }

    [Fact]
    public async Task FiveFailedAttempts_LockTheAccount_EvenForTheRightPassword()
    {
        await factory.CreateUserAsync("locktest", "Lock1234", SystemRoles.Cashier);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await PharmacyWebFactory.SignInAsync(factory.CreateBrowser(), "locktest", "Wrong1234");
        }

        using var response = await PharmacyWebFactory.SignInAsync(factory.CreateBrowser(), "locktest", "Lock1234");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("تم قفل الحساب مؤقتاً");
        (await AuditActionsForAsync("locktest")).ShouldContain(AuditActions.LockedOut);
    }

    [Fact]
    public async Task UserWithoutPermission_IsDeniedTheSettingsPage()
    {
        await factory.CreateUserAsync("cashier", "Cash1234", SystemRoles.Cashier);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "cashier", "Cash1234");

        using var response = await browser.GetAsync("/settings");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task DeactivatedUser_CannotSignIn()
    {
        await factory.CreateUserAsync("former", "Form1234", SystemRoles.Cashier);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE [identity].Users SET IsActive = 0 WHERE UserName = 'former'");
        }

        using var response = await PharmacyWebFactory.SignInAsync(factory.CreateBrowser(), "former", "Form1234");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("اسم المستخدم أو كلمة المرور غير صحيحة.");
    }

    [Fact]
    public async Task TemporaryPassword_ForcesAPasswordChangeFirst()
    {
        await factory.CreateUserAsync("newhire", "Temp1234", SystemRoles.Cashier);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE [identity].Users SET MustChangePassword = 1 WHERE UserName = 'newhire'");
        }

        var browser = factory.CreateBrowser();
        using var signIn = await PharmacyWebFactory.SignInAsync(browser, "newhire", "Temp1234");
        using var home = await browser.GetAsync("/");

        signIn.RedirectPath().ShouldBe("/Account/ChangePassword");
        home.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        home.RedirectPath().ShouldBe("/Account/ChangePassword");

        var page = await browser.GetStringAsync("/Account/ChangePassword");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = PharmacyWebFactory.AntiforgeryToken(page),
            ["_handler"] = "change-password",
            ["Input.CurrentPassword"] = "Temp1234",
            ["Input.NewPassword"] = "Mine5678",
            ["Input.ConfirmPassword"] = "Mine5678",
        });
        using var changed = await browser.PostAsync("/Account/ChangePassword", form);

        changed.RedirectPath().ShouldBe("/");
        (await browser.GetAsync("/")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_RequiresAntiforgeryAndEndsTheSession()
    {
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, PharmacyWebFactory.AdminUserName, PharmacyWebFactory.AdminPassword);

        using var forged = await browser.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));
        (await browser.GetAsync("/")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The logout form sits in the user menu, which renders only when opened; any form page of the
        // signed-in user carries a valid token for the same user.
        var formPage = await browser.GetStringAsync("/Account/ChangePassword");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = PharmacyWebFactory.AntiforgeryToken(formPage),
        });
        using var logout = await browser.PostAsync("/Account/Logout", form);

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        logout.RedirectPath().ShouldBe("/Account/Login");
        (await browser.GetAsync("/")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private async Task<List<string>> AuditActionsForAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().AuditLogEntries
            .Where(e => e.UserName == userName)
            .Select(e => e.Action)
            .ToListAsync();
    }
}

/// <summary>A fresh installation sends every page to the one-time setup screen until it is completed.</summary>
public sealed class FirstRunSetupTests(UnconfiguredWebFactory factory) : IClassFixture<UnconfiguredWebFactory>
{
    [Fact]
    public async Task FreshInstall_RedirectsToSetup_ThenSetupCreatesAndSignsInTheAdministrator()
    {
        var browser = factory.CreateBrowser();

        using var home = await browser.GetAsync("/");
        using var login = await browser.GetAsync("/Account/Login");
        home.RedirectPath().ShouldBe("/setup");
        login.RedirectPath().ShouldBe("/setup");

        var page = await browser.GetStringAsync("/setup");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = PharmacyWebFactory.AntiforgeryToken(page),
            ["_handler"] = "setup",
            ["Input.FullName"] = "مدير الصيدلية",
            ["Input.UserName"] = "owner",
            ["Input.Password"] = "Owner1234",
            ["Input.ConfirmPassword"] = "Owner1234",
        });
        using var setup = await browser.PostAsync("/setup", form);

        setup.RedirectPath().ShouldBe("/");
        (await browser.GetAsync("/settings")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The setup page closes for good.
        using var again = await factory.CreateBrowser().GetAsync("/setup");
        again.RedirectPath().ShouldBe("/Account/Login");
    }
}
