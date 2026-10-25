using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

/// <summary>Shared mapping for the bilingual reference lists; each list has its own table.</summary>
internal abstract class NamedLookupConfiguration<TLookup> : IEntityTypeConfiguration<TLookup>
    where TLookup : NamedLookup
{
    protected abstract string TableName { get; }

    /// <summary>The schema of the table; the catalog lists live in the catalog schema.</summary>
    protected virtual string Schema => Schemas.Catalog;

    /// <summary>Initial rows: (id, Arabic name, English name). Fixed ids keep the seed stable across migrations.</summary>
    protected virtual IReadOnlyList<(int Id, string Arabic, string English)> Seed => [];

    public void Configure(EntityTypeBuilder<TLookup> builder)
    {
        builder.ToTable(TableName, Schema);
        builder.HasKey(l => l.Id);
        builder.Property(l => l.NameArabic).HasMaxLength(NamedLookup.NameMaxLength).IsRequired();
        builder.Property(l => l.NameEnglish).HasMaxLength(NamedLookup.NameMaxLength).IsRequired();
        builder.HasIndex(l => l.NameArabic).IsUnique();
        builder.HasIndex(l => l.NameEnglish).IsUnique();

        builder.HasData(Seed.Select(s => new { s.Id, NameArabic = s.Arabic, NameEnglish = s.English, IsActive = true }));
    }
}

internal sealed class CategoryConfiguration : NamedLookupConfiguration<Category>
{
    protected override string TableName => "Categories";

    protected override IReadOnlyList<(int Id, string Arabic, string English)> Seed =>
    [
        (1, "أدوية عامة", "General medicines"),
    ];
}

internal sealed class UnitOfMeasureConfiguration : NamedLookupConfiguration<UnitOfMeasure>
{
    protected override string TableName => "UnitsOfMeasure";

    protected override IReadOnlyList<(int Id, string Arabic, string English)> Seed =>
    [
        (1, "حبة", "Tablet"),
        (2, "كبسولة", "Capsule"),
        (3, "شريط", "Strip"),
        (4, "علبة", "Box"),
        (5, "باكت", "Pack"),
        (6, "زجاجة", "Bottle"),
        (7, "أمبولة", "Ampoule"),
        (8, "أنبوب", "Tube"),
        (9, "كيس", "Sachet"),
        (10, "قطعة", "Piece"),
    ];
}

internal sealed class DosageFormConfiguration : NamedLookupConfiguration<DosageForm>
{
    protected override string TableName => "DosageForms";

    protected override IReadOnlyList<(int Id, string Arabic, string English)> Seed =>
    [
        (1, "أقراص", "Tablets"),
        (2, "كبسولات", "Capsules"),
        (3, "شراب", "Syrup"),
        (4, "حقن", "Injection"),
        (5, "كريم", "Cream"),
        (6, "مرهم", "Ointment"),
        (7, "قطرة", "Drops"),
        (8, "بخاخ", "Spray"),
        (9, "تحاميل", "Suppositories"),
        (10, "مسحوق", "Powder"),
        (11, "أخرى", "Other"),
    ];
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", Schemas.Catalog);
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TradeNameArabic).HasMaxLength(Product.TradeNameMaxLength);
        builder.Property(p => p.TradeNameEnglish).HasMaxLength(Product.TradeNameMaxLength);
        builder.Property(p => p.ScientificName).HasMaxLength(Product.ScientificNameMaxLength);
        builder.Property(p => p.Strength).HasMaxLength(Product.StrengthMaxLength);
        builder.Property(p => p.Manufacturer).HasMaxLength(Product.ManufacturerMaxLength);
        builder.Property(p => p.Notes).HasMaxLength(Product.NotesMaxLength);
        builder.Property(p => p.SearchText).HasMaxLength(Product.SearchTextMaxLength).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.BaseUnit);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_TradeName", "[TradeNameArabic] IS NOT NULL OR [TradeNameEnglish] IS NOT NULL");
            t.HasCheckConstraint("CK_Products_MinimumStock", "[MinimumStock] IS NULL OR [MinimumStock] >= 0");
            t.HasCheckConstraint("CK_Products_NearExpiryDays", "[NearExpiryDays] IS NULL OR [NearExpiryDays] > 0");
        });

        // Reference lists are never deleted (only deactivated), so deletes are restricted.
        builder.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DosageForm>().WithMany().HasForeignKey(p => p.DosageFormId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Units).WithOne().HasForeignKey(u => u.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Units).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => new { p.IsActive, p.TradeNameEnglish });
    }
}

internal sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> builder)
    {
        builder.ToTable("ProductUnits", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("CK_ProductUnits_Factor", "[Factor] >= 1 AND ([IsBase] = 0 OR [Factor] = 1)");
            t.HasCheckConstraint("CK_ProductUnits_Price", "[DefaultSellingPrice] IS NULL OR [DefaultSellingPrice] >= 0");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Barcode).HasMaxLength(ProductUnit.BarcodeMaxLength);

        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(u => u.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

        // "One base unit" and "no repeated unit" per product are enforced by the Product aggregate, not by
        // unique indexes: EF saves each changed row as a separate statement, so swapping the base unit (or
        // the units of two rows) would pass through a momentary duplicate and be rejected by the database.
        builder.HasIndex(u => u.ProductId);

        // Optional, but unique across all products when present.
        builder.HasIndex(u => u.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
    }
}
