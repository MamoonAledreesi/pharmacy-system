using System.Text.Encodings.Web;
using System.Text.Json;
using Pharmacy.Application.Abstractions;
using Pharmacy.Domain.Auditing;

namespace Pharmacy.Application.Auditing;

public sealed class AuditLog(IPharmacyDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    // Keep Arabic text readable in the stored JSON instead of \uXXXX escapes.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Record(string action, string? target = null, object? details = null) =>
        RecordFor(currentUser.UserId, currentUser.UserName ?? string.Empty, action, target, details);

    public void RecordFor(int? userId, string userName, string action, string? target = null, object? details = null)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, _jsonOptions);

        db.AuditLogEntries.Add(new AuditLogEntry(
            clock.GetUtcNow().UtcDateTime,
            userId,
            Truncate(userName, AuditLogEntry.UserNameMaxLength),
            action,
            target is null ? null : Truncate(target, AuditLogEntry.TargetMaxLength),
            json));
    }

    // A failed login records whatever was typed as the user name; never let that break the insert.
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
