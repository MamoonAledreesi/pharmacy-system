using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Pharmacy.Infrastructure;

namespace Pharmacy.IntegrationTests.Web;

/// <summary>
/// Hosts the real application against its own throwaway database, so tests never touch the
/// development database. The app migrates it on startup (Development environment); it is dropped on dispose.
/// </summary>
public sealed class PharmacyWebFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = TestDatabase.CreateUniqueConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", _connectionString);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await using var context = TestDatabase.CreateContext(_connectionString);
        await context.Database.EnsureDeletedAsync();
    }
}
