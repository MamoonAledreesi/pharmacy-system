using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("SalesReturns", Schemas.Sales, t =>
            t.HasCheckConstraint(
                "CK_SalesReturns_Amounts",
                "[Total] > 0 AND [DebtReduction] >= 0 AND [CashRefund] >= 0 AND [DebtReduction] + [CashRefund] = [Total]"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Number).HasMaxLength(SalesReturn.NumberMaxLength).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(SalesReturn.ReasonMaxLength);
        builder.Property(r => r.UserName).HasMaxLength(CustomerAccountEntry.UserNameMaxLength).IsRequired();

        builder.HasOne<SalesInvoice>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => r.OperationId).IsUnique();
        builder.HasIndex(r => r.InvoiceId);
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}

internal sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        builder.ToTable("SalesReturnLines", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("CK_SalesReturnLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
            t.HasCheckConstraint("CK_SalesReturnLines_Value", "[Value] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.HasMany(l => l.Allocations).WithOne().HasForeignKey(a => a.ReturnLineId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<SalesInvoiceLine>().WithMany().HasForeignKey(l => l.InvoiceLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(l => l.ProductUnitId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.ReturnId);
        builder.HasIndex(l => l.InvoiceLineId);
    }
}

internal sealed class SalesReturnAllocationConfiguration : IEntityTypeConfiguration<SalesReturnAllocation>
{
    public void Configure(EntityTypeBuilder<SalesReturnAllocation> builder)
    {
        builder.ToTable("SalesReturnAllocations", Schemas.Sales, t =>
            t.HasCheckConstraint("CK_SalesReturnAllocations_Amounts", "[BaseQuantity] > 0 AND [UnitCost] >= 0"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.UnitCost).HasPrecision(18, 4);

        builder.HasOne<SalesLineAllocation>().WithMany().HasForeignKey(a => a.InvoiceAllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany().HasForeignKey(a => a.BatchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.ReturnLineId);
        builder.HasIndex(a => a.InvoiceAllocationId);
    }
}
