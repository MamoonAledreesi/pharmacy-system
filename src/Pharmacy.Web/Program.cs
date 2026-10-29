using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;
using Pharmacy.Application;
using Pharmacy.Application.Abstractions;
using Pharmacy.Infrastructure;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.Web.Authentication;
using Pharmacy.Web.Common;
using Pharmacy.Web.Components;
using Pharmacy.Web.Localization;
using Pharmacy.Web.Reports;
using Serilog;

// Startup logger captures failures before configuration is loaded; AddSerilog below replaces it.
// (Not CreateBootstrapLogger: its process-wide reloadable logger breaks when tests host the app twice.)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
    // Lets the published app run as a Windows service (does nothing when started from a console).
    builder.Host.UseWindowsService();

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
    // Emit Arabic text as-is instead of &#x...; entities (several times smaller and readable HTML).
    builder.Services.AddWebEncoders(options =>
        options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddMudServices();
    builder.Services.AddTransient<MudBlazor.MudLocalizer, PharmacyMudLocalizer>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<PharmacyDbContext>();

    // Authentication: ASP.NET Core Identity with cookies (pattern from the Blazor Identity template).
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "Pharmacy.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Over HTTPS in production the cookie is never sent on plain HTTP.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.LoginPath = AccountEndpoints.LoginPath;
        options.AccessDeniedPath = AccountEndpoints.AccessDeniedPath;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });
    builder.Services.AddPharmacyIdentity()
        .AddSignInManager<ApplicationSignInManager>();

    // Authorization: one policy per permission, checked in pages and again in application services.
    builder.Services.AddAuthorizationBuilder().AddPermissionPolicies();
    builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddScoped<CurrentUserAccessor>();
    builder.Services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUserAccessor>());
    builder.Services.AddScoped<CircuitHandler, CurrentUserCircuitHandler>();
    builder.Services.AddScoped<SignInService>();
    builder.Services.AddScoped<ServiceRunner>();
    builder.Services.AddScoped<ReportDocumentBuilder>();
    builder.Services.AddSingleton<SetupState>();

    var app = builder.Build();

    // Development always migrates; a production install sets Database:MigrateOnStartup so upgrades update the database on start.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    {
        await app.Services.MigrateDatabaseAsync();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseSecurityHeaders();
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();
    app.UseRequestLocalization(SupportedCultures.CreateOptions());
    app.UseAuthentication();
    app.UseMiddleware<CurrentUserMiddleware>();
    // Before authorization: on a fresh install the setup page must win over the sign-in redirect.
    app.UseMiddleware<AccountGuardMiddleware>();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapCultureEndpoints();
    app.MapAccountEndpoints();
    app.MapReportEndpoints();
    app.MapHealthChecks("/health");
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Exposed so integration tests can host the app with WebApplicationFactory.
public partial class Program;
