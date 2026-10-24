using Microsoft.EntityFrameworkCore;
using Pharmacy.Domain.Settings;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Persistence;

public sealed class DatabaseConnectionTests : IAsyncLifetime
{
    private readonly PharmacyDbContext _context = TestDatabase.CreateContext(TestDatabase.CreateUniqueConnectionString());

    // Uses the real migrations, exactly as the application does.
    public async Task InitializeAsync() => await _context.Database.MigrateAsync();

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_ApplyToAnEmptyDatabase()
    {
        (await _context.Database.CanConnectAsync()).ShouldBeTrue();
        (await _context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Migrations_SeedTheSingleSettingsRow()
    {
        var rows = await _context.PharmacySettings.AsNoTracking().ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].ToData().ShouldBe(PharmacySettings.Defaults);
    }

    [Fact]
    public async Task Database_RejectsASecondSettingsRow()
    {
        var insertSecondRow = () => _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO config.PharmacySettings (Id, NameArabic, NameEnglish, ThemeMode, PrimaryColor) " +
            "VALUES (2, N'x', N'x', N'Light', N'Blue')");

        await Should.ThrowAsync<Microsoft.Data.SqlClient.SqlException>(insertSecondRow);
    }
}
