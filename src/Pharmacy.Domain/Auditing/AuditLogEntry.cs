namespace Pharmacy.Domain.Auditing;

/// <summary>
/// An append-only record of a sensitive operation. Entries are never updated or deleted.
/// User name is copied (not only referenced) so the log stays readable if the user is renamed.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 100;
    public const int UserNameMaxLength = 256;
    public const int TargetMaxLength = 256;

    // Used by EF Core.
    private AuditLogEntry()
    {
    }

    public AuditLogEntry(DateTime occurredAtUtc, int? userId, string userName, string action, string? target, string? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        OccurredAtUtc = occurredAtUtc;
        UserId = userId;
        UserName = userName ?? string.Empty;
        Action = action;
        Target = target;
        Details = details;
    }

    public long Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Null for anonymous events such as a failed login with an unknown user name.</summary>
    public int? UserId { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    /// <summary>One of the AuditActions codes; also the localization key suffix for its label.</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>What the action applied to, e.g. a user name or role name.</summary>
    public string? Target { get; private set; }

    /// <summary>JSON with the relevant values, such as before/after for an update.</summary>
    public string? Details { get; private set; }
}
