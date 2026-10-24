namespace Pharmacy.Application.Auditing;

/// <summary>Audit action codes. "AuditActions.{code}" is the localization key of each label.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string LockedOut = "Auth.LockedOut";
    public const string Logout = "Auth.Logout";
    public const string PasswordChanged = "Auth.PasswordChanged";

    public const string SetupCompleted = "Setup.Completed";

    public const string UserCreated = "Users.Created";
    public const string UserUpdated = "Users.Updated";
    public const string UserPasswordReset = "Users.PasswordReset";
    public const string UserUnlocked = "Users.Unlocked";

    public const string RoleCreated = "Roles.Created";
    public const string RoleUpdated = "Roles.Updated";
    public const string RoleDeleted = "Roles.Deleted";

    public const string SettingsUpdated = "Settings.Updated";

    public static IReadOnlyList<string> All { get; } =
    [
        LoginSucceeded, LoginFailed, LockedOut, Logout, PasswordChanged, SetupCompleted,
        UserCreated, UserUpdated, UserPasswordReset, UserUnlocked,
        RoleCreated, RoleUpdated, RoleDeleted, SettingsUpdated,
    ];
}
