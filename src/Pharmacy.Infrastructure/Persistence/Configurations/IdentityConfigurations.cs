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

    public void Configure(EntityTypeBuilder<IdentityRoleClaim<int>> builder)
    {
        builder.ToTable("RoleClaims", Schemas.Identity);

        // Default permissions of the built-in roles, granted when each feature is introduced.
        // Administrators can change them afterwards. Ids are permanent: append new grants with new ids.
        builder.HasData(DefaultGrants.Select((grant, index) => new IdentityRoleClaim<int>
        {
            Id = index + 1,
            RoleId = grant.RoleId,
            ClaimType = Permissions.ClaimType,
            ClaimValue = grant.Permission,
        }));
    }

    // Role ids as seeded in ApplicationRoleConfiguration: 2 Pharmacist, 3 Cashier, 4 Accountant, 5 StoreKeeper.
    private static readonly (int RoleId, string Permission)[] DefaultGrants =
    [
        (2, Permissions.CatalogView),
        (2, Permissions.CatalogManage),
        (3, Permissions.CatalogView),
        (4, Permissions.CatalogView),
        (5, Permissions.CatalogView),
        (5, Permissions.CatalogManage),

        // Inventory (Task 3). Cashiers see stock but not costs; the store keeper enters opening stock.
        (2, Permissions.InventoryView),
        (3, Permissions.InventoryView),
        (4, Permissions.InventoryView),
        (5, Permissions.InventoryView),
        (2, Permissions.InventoryAdjust),
        (5, Permissions.InventoryAdjust),
        (2, Permissions.InventoryWriteOff),
        (5, Permissions.InventoryWriteOff),
        (5, Permissions.InventoryOpeningStock),
        (2, Permissions.InventoryViewCost),
        (4, Permissions.InventoryViewCost),

        // Cash register (Task 4). Force-closing someone else's shift is left to administrators unless granted.
        (2, Permissions.CashView),
        (3, Permissions.CashView),
        (4, Permissions.CashView),
        (2, Permissions.CashOpenShift),
        (3, Permissions.CashOpenShift),
        (4, Permissions.CashOpenShift),
        (2, Permissions.CashManualMovement),
        (4, Permissions.CashManualMovement),
    ];

    public void Configure(EntityTypeBuilder<IdentityUserClaim<int>> builder) => builder.ToTable("UserClaims", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserLogin<int>> builder) => builder.ToTable("UserLogins", Schemas.Identity);

    public void Configure(EntityTypeBuilder<IdentityUserToken<int>> builder) => builder.ToTable("UserTokens", Schemas.Identity);
}
