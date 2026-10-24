using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pharmacy.Application.Common;
using Pharmacy.Application.Settings;
using Pharmacy.Domain.Settings;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Settings;

public sealed class PharmacySettingsServiceTests : IAsyncLifetime
{
    private static readonly PharmacySettingsData _updated = new(
        "صيدلية الشفاء", "Al Shifa Pharmacy", "0123456789", "الشارع الرئيسي", ThemeMode.Dark, ThemeColor.Purple);

    private readonly string _connectionString = TestDatabase.CreateUniqueConnectionString();
    private readonly PharmacySettingsState _state = new();

    public async Task InitializeAsync()
    {
        await using var context = TestDatabase.CreateContext(_connectionString);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = TestDatabase.CreateContext(_connectionString);
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task GetCurrent_LoadsOnceThenServesFromMemory()
    {
        await using var context = TestDatabase.CreateContext(_connectionString);

        var first = await CreateService(context).GetCurrentAsync();
        await context.Database.ExecuteSqlRawAsync("UPDATE config.PharmacySettings SET NameEnglish = N'Changed directly'");
        var second = await CreateService(context).GetCurrentAsync();

        first.ShouldBe(PharmacySettings.Defaults);
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task Update_PersistsAndRefreshesTheSharedState()
    {
        var notified = new List<PharmacySettingsData>();
        _state.Changed += notified.Add;

        await using (var context = TestDatabase.CreateContext(_connectionString))
        {
            var service = CreateService(context);
            var edit = await service.GetForEditAsync();

            var result = await service.UpdateAsync(_updated, edit.RowVersion);

            result.IsSuccess.ShouldBeTrue();
        }

        await using var verify = TestDatabase.CreateContext(_connectionString);
        (await verify.PharmacySettings.AsNoTracking().SingleAsync()).ToData().ShouldBe(_updated);
        _state.Current.ShouldBe(_updated);
        notified.ShouldBe([_updated]);
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejectedAndKeepsTheOtherUsersChange()
    {
        // Two users open the settings page at the same time.
        await using var firstContext = TestDatabase.CreateContext(_connectionString);
        await using var secondContext = TestDatabase.CreateContext(_connectionString);
        var firstUser = CreateService(firstContext);
        var secondUser = CreateService(secondContext);
        var firstEdit = await firstUser.GetForEditAsync();
        var secondEdit = await secondUser.GetForEditAsync();

        (await firstUser.UpdateAsync(_updated, firstEdit.RowVersion)).IsSuccess.ShouldBeTrue();
        var second = await secondUser.UpdateAsync(
            _updated with { NameEnglish = "Overwrite attempt" }, secondEdit.RowVersion);

        second.Error!.Code.ShouldBe(CommonErrors.ConcurrencyConflict);
        await using var verify = TestDatabase.CreateContext(_connectionString);
        (await verify.PharmacySettings.AsNoTracking().SingleAsync()).NameEnglish.ShouldBe(_updated.NameEnglish);
    }

    [Fact]
    public async Task Update_WithInvalidValues_SavesNothing()
    {
        await using var context = TestDatabase.CreateContext(_connectionString);
        var service = CreateService(context);
        var edit = await service.GetForEditAsync();

        var result = await service.UpdateAsync(_updated with { NameArabic = " " }, edit.RowVersion);

        result.Error!.Code.ShouldBe(SettingsErrors.NameArabicRequired);
        await using var verify = TestDatabase.CreateContext(_connectionString);
        (await verify.PharmacySettings.AsNoTracking().SingleAsync()).ToData().ShouldBe(PharmacySettings.Defaults);
        _state.Current.ShouldBeNull();
    }

    private PharmacySettingsService CreateService(PharmacyDbContext context) =>
        new(context, _state, NullLogger<PharmacySettingsService>.Instance);
}
