using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Application.Settings;

/// <summary>Settings values plus the concurrency token the edit form must send back when saving.</summary>
public sealed record PharmacySettingsForEdit(PharmacySettingsData Values, byte[] RowVersion);

public sealed partial class PharmacySettingsService(
    IPharmacyDbContext db,
    PharmacySettingsState state,
    ILogger<PharmacySettingsService> logger)
{
    /// <summary>Current settings, served from memory after the first load.</summary>
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

    public async Task<PharmacySettingsForEdit> GetForEditAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(asNoTracking: true, cancellationToken);
        return new PharmacySettingsForEdit(settings.ToData(), settings.RowVersion);
    }

    public async Task<Result> UpdateAsync(
        PharmacySettingsData values, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rowVersion);

        var settings = await LoadAsync(asNoTracking: false, cancellationToken);

        // Compare against the version the user loaded, not the one just read, so a save made by
        // someone else in between is detected instead of silently overwritten.
        db.Entry(settings).Property(s => s.RowVersion).OriginalValue = rowVersion;

        var result = settings.Update(values);
        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogConcurrencyConflict(logger);
            return Result.Failure(new Error(CommonErrors.ConcurrencyConflict));
        }

        state.Set(settings.ToData());
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
