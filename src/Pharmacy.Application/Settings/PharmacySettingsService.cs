using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Application.Settings;

/// <summary>Settings values plus the concurrency token the edit form must send back when saving.</summary>
public sealed record PharmacySettingsForEdit(PharmacySettingsData Values, byte[] RowVersion);

public sealed partial class PharmacySettingsService(
    IPharmacyDbContext db,
    PharmacySettingsState state,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    ILogger<PharmacySettingsService> logger)
{
    /// <summary>Current settings, served from memory after the first load. Readable by everyone (theme, name).</summary>
    public async Task<PharmacySettingsData> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (state.Current is { } cached)
        {
            return cached;
        }

        var settings = await LoadAsync(asNoTracking: true, cancellationToken);
        state.Set(settings.ToData());
        return state.Current!;
    }

    public async Task<Result<PharmacySettingsForEdit>> GetForEditAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.SettingsManage))
        {
            return Result.Failure<PharmacySettingsForEdit>(new Error(CommonErrors.Forbidden));
        }

        var settings = await LoadAsync(asNoTracking: true, cancellationToken);
        return Result.Success(new PharmacySettingsForEdit(settings.ToData(), settings.RowVersion));
    }

    public async Task<Result> UpdateAsync(
        PharmacySettingsData values, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rowVersion);

        if (!currentUser.HasPermission(Permissions.SettingsManage))
        {
            return Result.Failure(new Error(CommonErrors.Forbidden));
        }

        var settings = await LoadAsync(asNoTracking: false, cancellationToken);
        var before = settings.ToData();

        // Compare against the version the user loaded, not the one just read, so a save made by
        // someone else in between is detected instead of silently overwritten.
        db.Entry(settings).Property(s => s.RowVersion).OriginalValue = rowVersion;

        var result = settings.Update(values);
        if (result.IsFailure)
        {
            return result;
        }

        var after = settings.ToData();
        auditLog.Record(AuditActions.SettingsUpdated, details: new { Before = before, After = after });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogConcurrencyConflict(logger);
            return Result.Failure(new Error(CommonErrors.ConcurrencyConflict));
        }

        state.Set(after);
        LogSettingsUpdated(logger, settings.ThemeMode, settings.PrimaryColor);
        return Result.Success();
    }

    private Task<PharmacySettings> LoadAsync(bool asNoTracking, CancellationToken cancellationToken)
    {
        var query = asNoTracking ? db.PharmacySettings.AsNoTracking() : db.PharmacySettings;
        return query.SingleAsync(s => s.Id == PharmacySettings.SingletonId, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pharmacy settings updated (theme {ThemeMode}, color {PrimaryColor})")]
    private static partial void LogSettingsUpdated(ILogger logger, ThemeMode themeMode, ThemeColor primaryColor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pharmacy settings update rejected: changed by another user since it was loaded")]
    private static partial void LogConcurrencyConflict(ILogger logger);
}
