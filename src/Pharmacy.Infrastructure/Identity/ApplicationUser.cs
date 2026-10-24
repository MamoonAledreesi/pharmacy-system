using Microsoft.AspNetCore.Identity;

namespace Pharmacy.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Deactivated users cannot sign in. Users are never deleted because documents reference them.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set for temporary passwords; the user must choose a new password before using the system.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}
