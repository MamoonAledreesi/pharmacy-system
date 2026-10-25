using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Cash;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> builder)
    {
        builder.ToTable("CashShifts", Schemas.Cash, t =>
        {
            t.HasCheckConstraint("CK_CashShifts_Balances", "[OpeningBalance] >= 0 AND [CurrentBalance] >= 0 AND [PreviousCountedBalance] >= 0");

            // A shift is either open (nothing about closing exists) or closed (everything about closing exists).
            t.HasCheckConstraint(
                "CK_CashShifts_OpenOrClosed",
                "([IsOpen] = 1 AND [ClosedAtUtc] IS NULL AND [CountedBalance] IS NULL AND [Difference] IS NULL) " +
                "OR ([IsOpen] = 0 AND [ClosedAtUtc] IS NOT NULL AND [CountedBalance] IS NOT NULL AND [Difference] IS NOT NULL)");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.OpenedByUserName).HasMaxLength(CashMovement.UserNameMaxLength).IsRequired();
        builder.Property(s => s.ClosedByUserName).HasMaxLength(CashMovement.UserNameMaxLength);
        builder.Property(s => s.OpeningNote).HasMaxLength(CashMovement.NoteMaxLength);
        builder.Property(s => s.CloseNote).HasMaxLength(CashMovement.NoteMaxLength);
        builder.Property(s => s.RowVersion).IsRowVersion();

        // The drawer is single: the database itself refuses a second open shift, even for two simultaneous requests.
        builder.HasIndex(s => s.IsOpen).IsUnique().HasFilter("[IsOpen] = 1").HasDatabaseName("UX_CashShifts_OneOpen");

        builder.HasIndex(s => s.OpenOperationId).IsUnique().HasFilter("[OpenOperationId] IS NOT NULL");
        builder.HasIndex(s => s.CloseOperationId).IsUnique().HasFilter("[CloseOperationId] IS NOT NULL");
        builder.HasIndex(s => s.OpenedAtUtc);
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("CashMovements", Schemas.Cash, t =>
        {
            t.HasCheckConstraint("CK_CashMovements_Amount", "[Amount] <> 0");
            t.HasCheckConstraint("CK_CashMovements_BalanceAfter", "[BalanceAfter] >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.UserName).HasMaxLength(CashMovement.UserNameMaxLength).IsRequired();
        builder.Property(m => m.Note).HasMaxLength(CashMovement.NoteMaxLength);
        builder.Property(m => m.ReferenceType).HasMaxLength(CashMovement.ReferenceTypeMaxLength);

        // The ledger is append-only: a shift can never be deleted out from under it.
        builder.HasOne(m => m.Shift).WithMany().HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ShiftId, m.Id });
        builder.HasIndex(m => m.OccurredAtUtc);
        builder.HasIndex(m => new { m.ReferenceType, m.ReferenceId }).HasFilter("[ReferenceType] IS NOT NULL");

        // A repeated submission of the same operation is rejected by the database even if the application check races.
        builder.HasIndex(m => m.OperationId).IsUnique().HasFilter("[OperationId] IS NOT NULL");
    }
}
