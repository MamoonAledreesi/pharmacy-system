namespace Pharmacy.Application.Users;

/// <summary>User and role management error codes. Each code is a localization key in the UI resources.</summary>
public static class UserErrors
{
    public const string FullNameRequired = "Users.FullNameRequired";
    public const string FullNameTooLong = "Users.FullNameTooLong";
    public const string RoleRequired = "Users.RoleRequired";
    public const string UnknownRole = "Users.UnknownRole";
    public const string CannotDeactivateSelf = "Users.CannotDeactivateSelf";
    public const string CannotRemoveOwnAdministratorRole = "Users.CannotRemoveOwnAdministratorRole";
    public const string LastAdministrator = "Users.LastAdministrator";
    public const string SetupAlreadyCompleted = "Users.SetupAlreadyCompleted";
    public const string CurrentPasswordIncorrect = "Users.CurrentPasswordIncorrect";

    public const string RoleNameRequired = "Roles.NameRequired";
    public const string RoleNameTaken = "Roles.NameTaken";
    public const string SystemRoleReadOnly = "Roles.SystemRoleReadOnly";
    public const string RoleInUse = "Roles.InUse";
    public const string UnknownPermission = "Roles.UnknownPermission";
}

/// <summary>
/// ASP.NET Core Identity validation failures surfaced to users, keyed as "Identity.{IdentityError.Code}".
/// Codes not listed here are reported as <see cref="DefaultError"/>.
/// </summary>
public static class IdentityErrors
{
    public const string DefaultError = "Identity.DefaultError";
    public const string DuplicateUserName = "Identity.DuplicateUserName";
    public const string InvalidUserName = "Identity.InvalidUserName";
    public const string PasswordTooShort = "Identity.PasswordTooShort";
    public const string PasswordRequiresDigit = "Identity.PasswordRequiresDigit";
    public const string PasswordRequiresLetter = "Identity.PasswordRequiresLetter";
    public const string PasswordMismatch = "Identity.PasswordMismatch";
    public const string DuplicateRoleName = "Identity.DuplicateRoleName";
    public const string InvalidRoleName = "Identity.InvalidRoleName";
    public const string ConcurrencyFailure = "Identity.ConcurrencyFailure";
}
