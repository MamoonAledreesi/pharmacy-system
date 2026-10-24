using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Users;

public sealed record UserSummary(
    int Id,
    string UserName,
    string FullName,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    DateTime? LastLoginAtUtc);

public sealed record CreateUserRequest(string UserName, string FullName, string TemporaryPassword, IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(int UserId, string FullName, bool IsActive, IReadOnlyList<string> Roles);

public sealed record RoleSummary(int Id, string Name, bool IsSystem, int UserCount, IReadOnlyList<string> Permissions);

/// <summary>
/// Administration of users and roles. Every operation requires the Users.Manage permission and is audited.
/// Users are never deleted (documents will reference them); they are deactivated instead.
/// </summary>
public interface IUserManagementService
{
    Task<Result<IReadOnlyList<UserSummary>>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<Result<int>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets a temporary password the user must change at next sign-in, and signs out their sessions.</summary>
    Task<Result> ResetPasswordAsync(int userId, string temporaryPassword, CancellationToken cancellationToken = default);

    Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RoleSummary>>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Result<int>> CreateRoleAsync(string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    Task<Result> UpdateRoleAsync(int roleId, string name, IReadOnlyList<string> permissions, CancellationToken cancellationToken = default);

    Task<Result> DeleteRoleAsync(int roleId, CancellationToken cancellationToken = default);
}
