namespace Pharmacy.Application.Users;

public static class UserRules
{
    public const int FullNameMaxLength = 150;
    public const int UserNameMaxLength = 50;
    public const int RoleNameMaxLength = 50;

    // Password policy (approved): at least 8 characters with at least one letter and one digit.
    public const int PasswordMinLength = 8;

    // Lockout policy (approved): 5 failed attempts lock the account for 15 minutes.
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
