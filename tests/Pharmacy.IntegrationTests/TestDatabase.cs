using Microsoft.EntityFrameworkCore;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests;

/// <summary>
/// Integration tests run against a real SQL Server instance (the local default instance unless
/// PHARMACY_TEST_SQL_SERVER is set). Each test database gets a unique name and is dropped afterwards.
/// </summary>
internal static class TestDatabase
{
    private const string ServerVariable = "PHARMACY_TEST_SQL_SERVER";
    private const string DefaultServerConnection = "Server=.;Trusted_Connection=True;TrustServerCertificate=True";

    public static string CreateUniqueConnectionString()
    {
        var server = Environment.GetEnvironmentVariable(ServerVariable);
        var serverConnection = string.IsNullOrWhiteSpace(server) ? DefaultServerConnection : server;
        return $"{serverConnection};Database=PharmacyDb_Test_{Guid.NewGuid():N}";
    }

    public static PharmacyDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PharmacyDbContext>().UseSqlServer(connectionString).Options);
}
