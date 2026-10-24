using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Users;
using Pharmacy.Infrastructure.Identity;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users", Schemas.Identity);
        builder.Property(u => u.UserName).HasMaxLength(UserRules.UserNameMaxLength);
        builder.Property(u => u.NormalizedUserName).HasMaxLength(UserRules.UserNameMaxLength);
        builder.Property(u => u.FullName).HasMaxLength(UserRules.FullNameMaxLength).IsRequired();
        builder.Property(u => u.IsActive).HasDefaultValue(true);
    }
}

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    // Fixed values keep the seed stable across migrations.
    private static readonly (int Id, string Name, bool IsSystem)[] _seed =
    [
        (1, SystemRoles.Administrator, true),
        (2, SystemRoles.Pharmacist, false),
        (3, SystemRoles.Cashier, false),
        (4, SystemRoles.Accountant, false),
        (5, SystemRoles.StoreKeeper, false),
    ];

    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.ToTable("Roles", Schemas.Identity);
        builder.Property(r => r.Name).HasMaxLength(UserRules.RoleNameMaxLength);
        builder.Property(r => r.NormalizedName).HasMaxLength(UserRules.RoleNameMaxLength);

        builder.HasData(_seed.Select(role => new ApplicationRole
        {
            Id = role.Id,
            Name = role.Name,
            NormalizedName = role.Name.ToUpperInvariant(),
            ConcurrencyStamp = $"seed-role-{role.Id}",
            IsSystem = role.IsSystem,
        }));
    }
}

internal sealed class IdentityTableConfiguration :
    IEntityTypeConfiguration<IdentityUserRole<int>>,
    IEntityTypeConfiguration<IdentityRoleClaim<int>>,
    IEntityTypeConfiguration<IdentityUserClaim<int>>,
    IEntityTypeConfiguration<IdentityUserLogin<int>>,
    IEntityTypeConfiguration<IdentityUserToken<int>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<int>> builder) => builder.ToTable("UserRoles", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityRoleClaim<int>> builder) => builder.ToTable("RoleClaims", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserClaim<int>> builder) => builder.ToTable("UserClaims", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserLogin<int>> builder) => builder.ToTable("UserLogins", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserToken<int>> builder) => builder.ToTable("UserTokens", Schemas.Identity);
}
