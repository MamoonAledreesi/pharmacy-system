using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Users;

/// <summary>Operations a user performs on their own account, plus first-run setup.</summary>
public interface IAccountService
{
    /// <summary>True while no user exists, i.e. the first administrator has not been created yet.</summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the first administrator. Fails once any user exists.</summary>
    Task<Result> CompleteSetupAsync(string userName, string fullName, string password, CancellationToken cancellationToken = default);

    /// <summary>Changes the user's password and clears the "must change password" flag.</summary>
    Task<Result> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
