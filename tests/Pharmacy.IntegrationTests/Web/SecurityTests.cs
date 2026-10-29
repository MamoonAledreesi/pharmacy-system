using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Web.Common;

namespace Pharmacy.IntegrationTests.Web;

public sealed class SecurityTests(PharmacyWebFactory factory) : IClassFixture<PharmacyWebFactory>
{
    // The only routes a signed-out visitor may reach: sign-in, first-run setup (refused once an administrator exists), the error pages,
    // the language switch, the health check, and the Blazor connection (circuits only serve pages the user may open).
    private static readonly string[] _anonymousRoutes =
    [
        "/Account/Login", "/Account/Setup", "/Account/AccessDenied", "/setup", "/Error", "/not-found", "/culture/set", "/health", "/_blazor",
    ];

    [Fact]
    public async Task EveryResponse_CarriesTheSecurityHeaders_AndNoServerHeader()
    {
        await factory.CreateUserAsync("sec1", "Secure1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        using var login = await browser.GetAsync("/Account/Login");
        await PharmacyWebFactory.SignInAsync(browser, "sec1", "Secure1234");
        using var home = await browser.GetAsync("/");

        foreach (var response in new[] { login, home })
        {
            response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
            response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
            response.Headers.GetValues("Referrer-Policy").ShouldContain("strict-origin-when-cross-origin");
            response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeaders.ContentSecurityPolicy);
            response.Headers.Contains("Server").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task TheAuthCookie_IsHttpOnlyAndSameSite()
    {
        await factory.CreateUserAsync("sec2", "Secure1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();

        using var response = await PharmacyWebFactory.SignInAsync(browser, "sec2", "Secure1234");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("Pharmacy.Auth=", StringComparison.Ordinal));

        cookie.ToLowerInvariant().ShouldContain("httponly");
        cookie.ToLowerInvariant().ShouldContain("samesite=lax");
    }

    [Fact]
    public void EveryPage_RequiresASignedInUser_ExceptTheSignInAndSetupPages()
    {
        var pages = typeof(Pharmacy.Web.Components.App).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Length > 0)
            .ToList();
        var open = pages.Where(t => t.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Length > 0).Select(t => t.Name).Order().ToList();
        var unprotected = pages.Where(t => open.Contains(t.Name) == false && t.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length == 0).Select(t => t.Name).ToList();

        pages.ShouldNotBeEmpty();
        open.ShouldBe(["Error", "Login", "NotFound", "Setup"]);
        unprotected.ShouldBeEmpty();
    }

    [Fact]
    public void EveryEndpoint_RequiresSignIn_ExceptTheKnownOpenRoutes()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var open = endpoints
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null && e.Metadata.GetMetadata<IAllowAnonymous>() is null
                || e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .Where(route => !_anonymousRoutes.Any(a => route.StartsWith(a, StringComparison.OrdinalIgnoreCase))
                && !route.StartsWith("/_framework", StringComparison.Ordinal) && !route.StartsWith("/_content", StringComparison.Ordinal)
                && !route.Contains('.', StringComparison.Ordinal) && !route.Contains("{**", StringComparison.Ordinal))
            .Distinct()
            .Order()
            .ToList();

        open.ShouldBeEmpty("endpoints reachable without signing in");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sales/new")]
    [InlineData("/reports")]
    [InlineData("/settings")]
    [InlineData("/admin/users")]
    [InlineData("/cash")]
    public async Task SignedOutVisitors_AreSentToTheLoginPage(string path)
    {
        var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.RedirectPath().ShouldStartWith("/Account/Login");
    }

    [Fact]
    public async Task TheReconciliationPage_IsOpenToTheAccountant_AndDeniedToTheCashier()
    {
        await factory.CreateUserAsync("sec4", "Secure1234", SystemRoles.Accountant);
        await factory.CreateUserAsync("sec5", "Secure1234", SystemRoles.Cashier);
        var accountant = factory.CreateBrowser();
        var cashier = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(accountant, "sec4", "Secure1234");
        await PharmacyWebFactory.SignInAsync(cashier, "sec5", "Secure1234");

        using var allowed = await accountant.GetAsync("/admin/reconciliation");
        using var denied = await cashier.GetAsync("/admin/reconciliation");

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        denied.RedirectPath().ShouldStartWith("/Account/AccessDenied");
    }

    [Fact]
    public async Task TheLogoutEndpoint_RejectsARequestWithoutTheAntiforgeryToken()
    {
        await factory.CreateUserAsync("sec3", "Secure1234", SystemRoles.Accountant);
        var browser = factory.CreateBrowser();
        await PharmacyWebFactory.SignInAsync(browser, "sec3", "Secure1234");

        using var response = await browser.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
