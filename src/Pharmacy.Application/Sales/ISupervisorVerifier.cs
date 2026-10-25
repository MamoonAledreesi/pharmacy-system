using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Sales;

/// <summary>A supervisor who proved who they are by entering their own credentials, and what they are allowed to approve.</summary>
public sealed class SupervisorIdentity(int userId, string userName, bool isAdministrator, IReadOnlySet<string> permissions)
{
    public int UserId { get; } = userId;

    public string UserName { get; } = userName;

    public bool Has(string permission) => isAdministrator || permissions.Contains(permission);
}

/// <summary>
/// Confirms that a supervisor, not necessarily the signed-in user, agrees to an action: the account must exist, be
/// active and not locked, and the password must be right. Failures are deliberately indistinguishable so a wrong
/// user name reveals nothing. A wrong password counts toward the account lockout, so call this outside the
/// transaction of the action it approves (a rolled-back transaction would also roll back the count).
/// </summary>
public interface ISupervisorVerifier
{
    Task<Result<SupervisorIdentity>> VerifyAsync(string? userName, string? password, CancellationToken cancellationToken = default);
}
