using Pharmacy.Domain.Settings;

namespace Pharmacy.Application.Settings;

/// <summary>
/// Application-wide (singleton) copy of the current settings. Every open screen subscribes to
/// <see cref="Changed"/>, so a saved change applies immediately on all connected devices.
/// </summary>
public sealed class PharmacySettingsState
{
    private volatile PharmacySettingsData? _current;

    public event Action<PharmacySettingsData>? Changed;

    /// <summary>Null until first loaded from the database by <see cref="PharmacySettingsService"/>.</summary>
    public PharmacySettingsData? Current => _current;

    public void Set(PharmacySettingsData settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _current = settings;
        Changed?.Invoke(settings);
    }
}
