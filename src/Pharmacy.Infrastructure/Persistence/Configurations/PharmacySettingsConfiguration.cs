using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Settings;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class PharmacySettingsConfiguration : IEntityTypeConfiguration<PharmacySettings>
{
    private const int EnumMaxLength = 30;

    public void Configure(EntityTypeBuilder<PharmacySettings> builder)
    {
        builder.ToTable("PharmacySettings", Schemas.Config, table =>
            {
                table.HasCheckConstraint("CK_PharmacySettings_SingleRow", $"[Id] = {PharmacySettings.SingletonId}");
                table.HasCheckConstraint("CK_PharmacySettings_NearExpiryDays", "[NearExpiryDays] BETWEEN 1 AND 3650");
                table.HasCheckConstraint("CK_PharmacySettings_MaxDiscountPercent", "[MaxDiscountPercent] BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_PharmacySettings_ReturnWindowDays", "[ReturnWindowDays] BETWEEN 0 AND 365");
            });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.NameArabic).HasMaxLength(PharmacySettings.NameMaxLength).IsRequired();
        builder.Property(s => s.NameEnglish).HasMaxLength(PharmacySettings.NameMaxLength).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(PharmacySettings.PhoneMaxLength);
        builder.Property(s => s.Address).HasMaxLength(PharmacySettings.AddressMaxLength);

        // Enums are stored by name so the table stays readable and reordering members is safe.
        builder.Property(s => s.ThemeMode).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(s => s.PrimaryColor).HasConversion<string>().HasMaxLength(EnumMaxLength);

        builder.Property(s => s.NearExpiryDays).HasDefaultValue(PharmacySettings.DefaultNearExpiryDays);
        builder.Property(s => s.MaxDiscountPercent).HasPrecision(5, 2).HasDefaultValue(PharmacySettings.DefaultMaxDiscountPercent);
        builder.Property(s => s.ReturnWindowDays).HasDefaultValue(PharmacySettings.DefaultReturnWindowDays);
        builder.Property(s => s.RowVersion).IsRowVersion();

        var defaults = PharmacySettings.Defaults;
        builder.HasData(new
        {
            Id = PharmacySettings.SingletonId,
            defaults.NameArabic,
            defaults.NameEnglish,
            defaults.ThemeMode,
            defaults.PrimaryColor,
            defaults.NearExpiryDays,
            defaults.MaxDiscountPercent,
            defaults.ReturnWindowDays,
        });
    }
}
