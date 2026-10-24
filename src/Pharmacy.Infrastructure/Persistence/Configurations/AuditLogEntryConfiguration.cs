using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Auditing;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog", Schemas.Audit);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserName).HasMaxLength(AuditLogEntry.UserNameMaxLength).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(AuditLogEntry.ActionMaxLength).IsRequired();
        builder.Property(e => e.Target).HasMaxLength(AuditLogEntry.TargetMaxLength);
        // Removes the 256-character string convention (nvarchar(max)): before/after snapshots can be long.
        builder.Property(e => e.Details).Metadata.SetMaxLength(null);

        // The viewer lists newest first and filters by date range and action.
        builder.HasIndex(e => e.OccurredAtUtc);
        builder.HasIndex(e => new { e.Action, e.OccurredAtUtc });
    }
}
