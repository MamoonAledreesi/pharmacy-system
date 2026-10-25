using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <summary>The id of the default cash customer; sales select it when nobody is named.</summary>
    public const int DefaultCashCustomerId = 1;

    private const string DefaultCashCustomerName = "عميل نقدي / Cash customer";

    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", Schemas.Receivables, t =>
            t.HasCheckConstraint("CK_Customers_Balance", "[CurrentBalance] >= 0"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(Customer.PhoneMaxLength);
        builder.Property(c => c.Address).HasMaxLength(Customer.AddressMaxLength);
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);
        builder.Property(c => c.SearchText).HasMaxLength(Customer.NameMaxLength + Customer.PhoneMaxLength + 1).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.SearchText);

        // There is exactly one default cash customer; the database refuses a second.
        builder.HasIndex(c => c.IsDefaultCash).IsUnique().HasFilter("[IsDefaultCash] = 1").HasDatabaseName("UX_Customers_OneDefaultCash");

        builder.HasData(new
        {
            Id = DefaultCashCustomerId,
            Name = DefaultCashCustomerName,
            IsActive = true,
            IsDefaultCash = true,
            CurrentBalance = 0m,
            SearchText = Customer.SearchTextOf(DefaultCashCustomerName, null),
        });
    }
}

internal sealed class CustomerAccountEntryConfiguration : IEntityTypeConfiguration<CustomerAccountEntry>
{
    public void Configure(EntityTypeBuilder<CustomerAccountEntry> builder)
    {
        builder.ToTable("CustomerAccountEntries", Schemas.Receivables, t =>
        {
            t.HasCheckConstraint("CK_CustomerAccountEntries_Amount", "[Amount] <> 0");
            t.HasCheckConstraint("CK_CustomerAccountEntries_BalanceAfter", "[BalanceAfter] >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.UserName).HasMaxLength(CustomerAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(CustomerAccountEntry.NoteMaxLength);
        builder.Property(e => e.ReferenceType).HasMaxLength(CustomerAccountEntry.ReferenceTypeMaxLength);

        // The statement is append-only: a customer can never be deleted out from under it.
        builder.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CustomerId, e.Id });
        builder.HasIndex(e => new { e.ReferenceType, e.ReferenceId }).HasFilter("[ReferenceType] IS NOT NULL");
        builder.HasIndex(e => e.OperationId).IsUnique().HasFilter("[OperationId] IS NOT NULL");
    }
}

internal sealed class DebtCollectionConfiguration : IEntityTypeConfiguration<DebtCollection>
{
    public void Configure(EntityTypeBuilder<DebtCollection> builder)
    {
        builder.ToTable("DebtCollections", Schemas.Receivables, t =>
        {
            t.HasCheckConstraint("CK_DebtCollections_Amount", "[Amount] > 0");
            t.HasCheckConstraint(
                "CK_DebtCollections_Reversal",
                "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) "
                + "OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Number).HasMaxLength(DebtCollection.NumberMaxLength).IsRequired();
        builder.Property(c => c.UserName).HasMaxLength(CustomerAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(c => c.Note).HasMaxLength(CustomerAccountEntry.NoteMaxLength);
        builder.Property(c => c.ReversedByUserName).HasMaxLength(CustomerAccountEntry.UserNameMaxLength);
        builder.Property(c => c.ReverseReason).HasMaxLength(CustomerAccountEntry.NoteMaxLength);

        builder.HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CustomerId, c.Id });
        builder.HasIndex(c => c.Number).IsUnique();
        builder.HasIndex(c => c.OperationId).IsUnique();
        builder.HasIndex(c => c.ReverseOperationId).IsUnique().HasFilter("[ReverseOperationId] IS NOT NULL");
    }
}

