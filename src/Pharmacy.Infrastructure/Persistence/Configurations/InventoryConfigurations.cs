using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Inventory;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batches", Schemas.Inventory, t =>
        {
            // The last line of defence: whatever the code does, stock can never be stored as negative.
            t.HasCheckConstraint("CK_Batches_QuantityAvailable", "[QuantityAvailable] >= 0");
            t.HasCheckConstraint("CK_Batches_QuantityQuarantined", "[QuantityQuarantined] >= 0");
            t.HasCheckConstraint("CK_Batches_UnitCost", "[UnitCost] >= 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.BatchNumber).HasMaxLength(Batch.BatchNumberMaxLength).IsRequired();
        builder.Property(b => b.ExpiryDate).HasColumnType("date");
        builder.Property(b => b.UnitCost).HasPrecision(18, Batch.UnitCostDecimals);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasOne<Product>().WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);

        // A batch is identified by product, number, expiry, and cost. SQL Server unique indexes treat two NULL
        // expiry dates as equal, so products without expiry are covered too. HasFilter(null) removes the
        // "WHERE ExpiryDate IS NOT NULL" filter that EF would otherwise add for a nullable column,
        // which would leave exactly those products without protection.
        builder.HasIndex(b => new { b.ProductId, b.BatchNumber, b.ExpiryDate, b.UnitCost }).IsUnique().HasFilter(null);

        // First-expiring-first lookups and the near-expiry/expired reports.
        builder.HasIndex(b => new { b.ProductId, b.ExpiryDate });
        builder.HasIndex(b => b.ExpiryDate);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("CK_StockMovements_NotEmpty", "[AvailableDelta] <> 0 OR [QuarantineDelta] <> 0");
            t.HasCheckConstraint("CK_StockMovements_Balances", "[AvailableAfter] >= 0 AND [QuarantineAfter] >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.UnitCost).HasPrecision(18, Batch.UnitCostDecimals);
        builder.Property(m => m.UserName).HasMaxLength(StockMovement.UserNameMaxLength).IsRequired();
        builder.Property(m => m.ReasonCode).HasMaxLength(StockMovement.ReasonCodeMaxLength);
        builder.Property(m => m.Note).HasMaxLength(StockReasons.NoteMaxLength);
        builder.Property(m => m.ReferenceType).HasMaxLength(StockMovement.ReferenceTypeMaxLength);

        // The ledger is append-only: batches and products can never be deleted out from under it.
        builder.HasOne(m => m.Batch).WithMany().HasForeignKey(m => m.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.BatchId, m.Id });
        builder.HasIndex(m => new { m.ProductId, m.OccurredAtUtc });
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId }).HasFilter("[ReferenceType] IS NOT NULL");

        // The same user operation can touch each batch once per movement type, so a repeated submission is
        // rejected by the database even if the application-level check races.
        builder.HasIndex(m => new { m.OperationId, m.BatchId, m.Type }).IsUnique().HasFilter("[OperationId] IS NOT NULL");
    }
}
