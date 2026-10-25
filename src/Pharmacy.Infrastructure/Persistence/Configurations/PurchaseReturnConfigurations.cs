using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.ToTable("PurchaseReturns", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint(
                "CK_PurchaseReturns_Amounts",
                "[Total] > 0 AND [DebtReduction] >= 0 AND [CashRefund] >= 0 AND [DebtReduction] + [CashRefund] = [Total]");

            // What the pharmacy owes can only come down for a registered supplier.
            t.HasCheckConstraint("CK_PurchaseReturns_DebtNeedsSupplier", "[DebtReduction] = 0 OR [SupplierId] IS NOT NULL");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Number).HasMaxLength(PurchaseReturn.NumberMaxLength).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(PurchaseReturn.ReasonMaxLength).IsRequired();
        builder.Property(r => r.UserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength).IsRequired();

        builder.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => r.OperationId).IsUnique();
        builder.HasIndex(r => r.InvoiceId);
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}

internal sealed class PurchaseReturnLineConfiguration : IEntityTypeConfiguration<PurchaseReturnLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnLine> builder)
    {
        builder.ToTable("PurchaseReturnLines", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("CK_PurchaseReturnLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
            t.HasCheckConstraint("CK_PurchaseReturnLines_Value", "[Value] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.HasOne<PurchaseInvoiceLine>().WithMany().HasForeignKey(l => l.InvoiceLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(l => l.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.ReturnId);
        builder.HasIndex(l => l.InvoiceLineId);
    }
}
