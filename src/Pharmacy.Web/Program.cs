using System.Text.Encodings.Web;
using System.Text.Unicode;
using MudBlazor.Services;
using Pharmacy.Application;
using Pharmacy.Infrastructure;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.Web.Components;
using Pharmacy.Web.Localization;
using Serilog;

// Startup logger captures failures before configuration is loaded; AddSerilog below replaces it.
// (Not CreateBootstrapLogger: its process-wide reloadable logger breaks when tests host the app twice.)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

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

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<PharmacyDbContext>();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        await app.Services.MigrateDatabaseAsync();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }
    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseHttpsRedirection();
    app.UseSerilogRequestLogging();
    app.UseRequestLocalization(SupportedCultures.CreateOptions());
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapCultureEndpoints();
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
