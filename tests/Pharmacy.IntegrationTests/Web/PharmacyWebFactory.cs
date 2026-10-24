using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Users;
using Pharmacy.Infrastructure;
using Pharmacy.Infrastructure.Identity;

namespace Pharmacy.IntegrationTests.Web;

/// <summary>
/// Hosts the real application against its own throwaway database, so tests never touch the
/// development database. The app migrates it on startup (Development environment); it is dropped on dispose.
/// The first administrator is created unless <see cref="CompleteSetup"/> is false.
/// </summary>
public class PharmacyWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Admin1234";

    private readonly string _connectionString = TestDatabase.CreateUniqueConnectionString();

    protected virtual bool CompleteSetup => true;

    public async Task InitializeAsync()
    {
        // Starting the server applies the migrations.
        _ = Server;

        if (CompleteSetup)
        {
            await using var scope = Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IAccountService>()
                .CompleteSetupAsync(AdminUserName, "Test Administrator", AdminPassword);
            result.IsSuccess.ShouldBeTrue();
        }
    }

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await using var context = TestDatabase.CreateContext(_connectionString);
        await context.Database.EnsureDeletedAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A client that keeps cookies and does not follow redirects, so tests can assert on them.</summary>
    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Signs in through the real login form (antiforgery token included) and returns the response.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string userName, string password)
    {
        var page = await browser.GetStringAsync("/Account/Login");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryToken(page),
            ["_handler"] = "login",
            ["Input.UserName"] = userName,
            ["Input.Password"] = password,
        });
        return await browser.PostAsync("/Account/Login", form);
    }

    /// <summary>Creates a user with a permanent password (no forced change), acting as the administrator.</summary>
    public async Task CreateUserAsync(string userName, string password, params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, FullName = userName, IsActive = true };
        (await users.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
        (await users.AddToRolesAsync(user, roles)).Succeeded.ShouldBeTrue();
    }

    public static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        match.Success.ShouldBeTrue("the page has no antiforgery token");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", _connectionString);
    }
}

public static class RedirectAssertions
{
    /// <summary>The redirect target as an app path; Blazor issues absolute URLs, minimal APIs relative ones.</summary>
    public static string RedirectPath(this HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
    }
}

/// <summary>A fresh installation: no user exists yet.</summary>
public sealed class UnconfiguredWebFactory : PharmacyWebFactory
{
    protected override bool CompleteSetup => false;
}
