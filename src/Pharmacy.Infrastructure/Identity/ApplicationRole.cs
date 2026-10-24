using Microsoft.AspNetCore.Identity;

namespace Pharmacy.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<int>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }

    /// <summary>The Administrator role: holds all permissions implicitly and cannot be edited or deleted.</summary>
    public bool IsSystem { get; set; }
}
