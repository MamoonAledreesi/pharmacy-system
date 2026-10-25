using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("SalesInvoices", Schemas.Sales, t =>
        {
            t.HasCheckConstraint(
                "CK_SalesInvoices_Amounts",
                "[Subtotal] > 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] < [Subtotal] AND [Total] = [Subtotal] - [DiscountAmount] "
                + "AND [PaidCash] >= 0 AND [OnCredit] >= 0 AND [PaidCash] + [OnCredit] = [Total]");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Number).HasMaxLength(SalesInvoice.NumberMaxLength).IsRequired();
        builder.Property(i => i.DiscountPercent).HasPrecision(5, 2);
        builder.Property(i => i.Note).HasMaxLength(SalesInvoice.NoteMaxLength);
        builder.Property(i => i.UserName).HasMaxLength(CustomerAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(i => i.BelowCostApprovedBy).HasMaxLength(CustomerAccountEntry.UserNameMaxLength);
        builder.Property(i => i.BelowCostReason).HasMaxLength(SalesInvoice.ReasonMaxLength);
        builder.Property(i => i.DiscountApprovedBy).HasMaxLength(CustomerAccountEntry.UserNameMaxLength);
        builder.Ignore(i => i.HasBelowCostLines);

        builder.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => i.OperationId).IsUnique();
        builder.HasIndex(i => i.CreatedAtUtc);
        builder.HasIndex(i => new { i.CustomerId, i.CreatedAtUtc });
    }
}

internal sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        builder.ToTable("SalesInvoiceLines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("CK_SalesInvoiceLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
            t.HasCheckConstraint(
                "CK_SalesInvoiceLines_Amounts",
                "[UnitPrice] > 0 AND [LineSubtotal] > 0 AND [DiscountShare] >= 0 AND [NetTotal] = [LineSubtotal] - [DiscountShare] AND [CostTotal] >= 0");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Ignore(l => l.IsBelowCost);

        builder.HasMany(l => l.Allocations).WithOne().HasForeignKey(a => a.LineId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(l => l.ProductUnitId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.InvoiceId);
        builder.HasIndex(l => l.ProductId);
    }
}

internal sealed class SalesLineAllocationConfiguration : IEntityTypeConfiguration<SalesLineAllocation>
{
    public void Configure(EntityTypeBuilder<SalesLineAllocation> builder)
    {
        builder.ToTable("SalesLineAllocations", Schemas.Sales, t =>
            t.HasCheckConstraint("CK_SalesLineAllocations_Amounts", "[BaseQuantity] > 0 AND [UnitCost] >= 0"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.UnitCost).HasPrecision(18, 4);

        builder.HasOne<Batch>().WithMany().HasForeignKey(a => a.BatchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.LineId);
        builder.HasIndex(a => a.BatchId);
    }
}
