using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Settings;
using Pharmacy.Domain.Settings;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Settings;

public sealed class PharmacySettingsServiceTests : IAsyncLifetime
{
    private static readonly PharmacySettingsData _updated = new(
        "صيدلية الشفاء", "Al Shifa Pharmacy", "0123456789", "الشارع الرئيسي", ThemeMode.Dark, ThemeColor.Purple);

    private readonly TestServices _services = new();

    public async Task InitializeAsync()
    {
        await _services.InitializeAsync();
        _services.CurrentUser.SignInAs(1, "manager", isAdministrator: false, Permissions.SettingsManage);
    }

    public Task DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task GetCurrent_LoadsOnceThenServesFromMemory()
    {
        var first = await _services.InScopeAsync(s => s.GetRequiredService<PharmacySettingsService>().GetCurrentAsync());
        await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database
            .ExecuteSqlRawAsync("UPDATE config.PharmacySettings SET NameEnglish = N'Changed directly'"));
        var second = await _services.InScopeAsync(s => s.GetRequiredService<PharmacySettingsService>().GetCurrentAsync());

        first.ShouldBe(PharmacySettings.Defaults);
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public async Task Update_PersistsRefreshesSharedStateAndIsAudited()
    {
        var state = await _services.InScopeAsync(s => Task.FromResult(s.GetRequiredService<PharmacySettingsState>()));
        var notified = new List<PharmacySettingsData>();
        state.Changed += notified.Add;

        var result = await UpdateAsync(_updated);

        result.IsSuccess.ShouldBeTrue();
        (await LoadSettingsAsync()).ShouldBe(_updated);
        state.Current.ShouldBe(_updated);
        notified.ShouldBe([_updated]);

        var audit = await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.SingleAsync());
        audit.Action.ShouldBe(AuditActions.SettingsUpdated);
        audit.UserName.ShouldBe("manager");
        using var details = JsonDocument.Parse(audit.Details!);
        details.RootElement.GetProperty("Before").GetProperty("NameArabic").GetString().ShouldBe(PharmacySettings.Defaults.NameArabic);
        details.RootElement.GetProperty("After").GetProperty("NameArabic").GetString().ShouldBe(_updated.NameArabic);
    }

    [Fact]
    public async Task Update_WithoutPermission_IsForbiddenAndChangesNothing()
    {
        _services.CurrentUser.SignInAs(2, "cashier");

        var edit = await _services.InScopeAsync(s => s.GetRequiredService<PharmacySettingsService>().GetForEditAsync());
        var update = await _services.InScopeAsync(s =>
            s.GetRequiredService<PharmacySettingsService>().UpdateAsync(_updated, []));

        edit.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        update.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await LoadSettingsAsync()).ShouldBe(PharmacySettings.Defaults);
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejectedAndKeepsTheOtherUsersChange()
    {
        // Two users open the settings page at the same time.
        var firstVersion = await LoadVersionAsync();
        var secondVersion = await LoadVersionAsync();

        (await UpdateAsync(_updated, firstVersion)).IsSuccess.ShouldBeTrue();
        var second = await UpdateAsync(_updated with { NameEnglish = "Overwrite attempt" }, secondVersion);

        second.Error!.Code.ShouldBe(CommonErrors.ConcurrencyConflict);
        (await LoadSettingsAsync()).NameEnglish.ShouldBe(_updated.NameEnglish);
    }

    [Fact]
    public async Task Update_WithInvalidValues_SavesNothing()
    {
        var result = await UpdateAsync(_updated with { NameArabic = " " });

        result.Error!.Code.ShouldBe(SettingsErrors.NameArabicRequired);
        (await LoadSettingsAsync()).ShouldBe(PharmacySettings.Defaults);
        (await _services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.CountAsync())).ShouldBe(0);
    }

    private async Task<Domain.Common.Result> UpdateAsync(PharmacySettingsData values, byte[]? rowVersion = null)
    {
        rowVersion ??= await LoadVersionAsync();
        return await _services.InScopeAsync(s => s.GetRequiredService<PharmacySettingsService>().UpdateAsync(values, rowVersion));
    }

    private Task<byte[]> LoadVersionAsync() =>
        _services.InScopeAsync(async s => (await s.GetRequiredService<PharmacySettingsService>().GetForEditAsync()).Value.RowVersion);

    private Task<PharmacySettingsData> LoadSettingsAsync() =>
        _services.InScopeAsync(async s =>
            (await s.GetRequiredService<PharmacyDbContext>().PharmacySettings.AsNoTracking().SingleAsync()).ToData());
}
