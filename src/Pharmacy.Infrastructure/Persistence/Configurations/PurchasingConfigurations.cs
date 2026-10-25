using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("DocumentSequences", Schemas.Config);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Prefix).HasMaxLength(DocumentSequence.PrefixMaxLength).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        // One sequence per kind of document and year; the database refuses a second one created by a racing request.
        builder.HasIndex(s => new { s.Prefix, s.Year }).IsUnique();
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", Schemas.Purchasing, t =>
            t.HasCheckConstraint("CK_Suppliers_Balance", "[CurrentBalance] >= 0"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(Supplier.NameMaxLength).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(Supplier.PhoneMaxLength);
        builder.Property(s => s.Address).HasMaxLength(Supplier.AddressMaxLength);
        builder.Property(s => s.Notes).HasMaxLength(Supplier.NotesMaxLength);
        builder.Property(s => s.SearchText).HasMaxLength(Supplier.NameMaxLength + Supplier.PhoneMaxLength + 1).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class SupplierAccountEntryConfiguration : IEntityTypeConfiguration<SupplierAccountEntry>
{
    public void Configure(EntityTypeBuilder<SupplierAccountEntry> builder)
    {
        builder.ToTable("SupplierAccountEntries", Schemas.Payables, t =>
        {
            t.HasCheckConstraint("CK_SupplierAccountEntries_Amount", "[Amount] <> 0");
            t.HasCheckConstraint("CK_SupplierAccountEntries_BalanceAfter", "[BalanceAfter] >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.UserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(SupplierAccountEntry.NoteMaxLength);
        builder.Property(e => e.ReferenceType).HasMaxLength(SupplierAccountEntry.ReferenceTypeMaxLength);

        // The statement is append-only: a supplier can never be deleted out from under it.
        builder.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.SupplierId, e.Id });
        builder.HasIndex(e => new { e.ReferenceType, e.ReferenceId }).HasFilter("[ReferenceType] IS NOT NULL");
        builder.HasIndex(e => e.OperationId).IsUnique().HasFilter("[OperationId] IS NOT NULL");
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("SupplierPayments", Schemas.Payables, t =>
        {
            t.HasCheckConstraint("CK_SupplierPayments_Amount", "[Amount] > 0");
            t.HasCheckConstraint(
                "CK_SupplierPayments_Reversal",
                "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) "
                + "OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.UserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(p => p.Note).HasMaxLength(SupplierAccountEntry.NoteMaxLength);
        builder.Property(p => p.ReversedByUserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength);
        builder.Property(p => p.ReverseReason).HasMaxLength(SupplierAccountEntry.NoteMaxLength);

        builder.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.SupplierId, p.Id });
        builder.HasIndex(p => p.OperationId).IsUnique();
        builder.HasIndex(p => p.ReverseOperationId).IsUnique().HasFilter("[ReverseOperationId] IS NOT NULL");
    }
}

internal sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("PurchaseInvoices", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("CK_PurchaseInvoices_Amounts", "[Total] >= 0 AND [PaidCash] >= 0 AND [OnCredit] >= 0 AND [PaidCash] + [OnCredit] = [Total]");

            // Debt can only be owed to a registered supplier.
            t.HasCheckConstraint("CK_PurchaseInvoices_CreditNeedsSupplier", "[OnCredit] = 0 OR [SupplierId] IS NOT NULL");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Number).HasMaxLength(PurchaseInvoice.NumberMaxLength).IsRequired();
        builder.Property(i => i.SupplierName).HasMaxLength(PurchaseInvoice.SupplierNameMaxLength);
        builder.Property(i => i.SupplierInvoiceNumber).HasMaxLength(PurchaseInvoice.SupplierInvoiceNumberMaxLength);
        builder.Property(i => i.Note).HasMaxLength(PurchaseInvoice.NoteMaxLength);
        builder.Property(i => i.UserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength).IsRequired();

        builder.HasOne<Supplier>().WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => i.OperationId).IsUnique();
        builder.HasIndex(i => i.InvoiceDate);
        builder.HasIndex(i => new { i.SupplierId, i.InvoiceDate });
    }
}

internal sealed class PurchaseInvoiceLineConfiguration : IEntityTypeConfiguration<PurchaseInvoiceLine>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLine> builder)
    {
        builder.ToTable("PurchaseInvoiceLines", Schemas.Purchasing, t =>
        {
            t.HasCheckConstraint("CK_PurchaseInvoiceLines_Quantities", "[Quantity] > 0 AND [BaseQuantity] > 0 AND [UnitFactor] > 0");
            t.HasCheckConstraint("CK_PurchaseInvoiceLines_Costs", "[CostPerUnit] >= 0 AND [BaseUnitCost] >= 0 AND [LineTotal] >= 0");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.CostPerUnit).HasPrecision(18, 4);
        builder.Property(l => l.BaseUnitCost).HasPrecision(18, 4);
        builder.Property(l => l.BatchNumber).HasMaxLength(Batch.BatchNumberMaxLength).IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductUnit>().WithMany().HasForeignKey(l => l.ProductUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Batch>().WithMany().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.InvoiceId);
        builder.HasIndex(l => l.ProductId);
    }
}
