namespace Pharmacy.Application.Auditing;

/// <summary>
/// Adds audit entries to the current unit of work. Nothing is written until the caller saves,
/// so an operation and its audit entry are committed (or rolled back) together.
/// </summary>
public interface IAuditLog
{
    /// <summary>Records an action by the current user.</summary>
    void Record(string action, string? target = null, object? details = null);

    /// <summary>Records an action for an explicit user, e.g. during sign-in before the user is current.</summary>
    void RecordFor(int? userId, string userName, string action, string? target = null, object? details = null);
}
