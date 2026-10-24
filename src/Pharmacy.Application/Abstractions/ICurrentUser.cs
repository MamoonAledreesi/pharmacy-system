namespace Pharmacy.Application.Abstractions;

/// <summary>The signed-in user of the current request or interactive session.</summary>
public interface ICurrentUser
{
    /// <summary>Null when nobody is signed in.</summary>
    int? UserId { get; }

    string? UserName { get; }

    bool HasPermission(string permission);
}
