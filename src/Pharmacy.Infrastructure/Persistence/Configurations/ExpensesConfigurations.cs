using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pharmacy.Domain.Expenses;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseCategoryConfiguration : NamedLookupConfiguration<ExpenseCategory>
{
    protected override string TableName => "ExpenseCategories";

    protected override string Schema => Schemas.Expenses;

    protected override IReadOnlyList<(int Id, string Arabic, string English)> Seed =>
    [
        (1, "إيجار", "Rent"),
        (2, "كهرباء", "Electricity"),
        (3, "ماء", "Water"),
        (4, "اتصالات وإنترنت", "Phone and internet"),
        (5, "رواتب وأجور", "Salaries and wages"),
        (6, "نظافة", "Cleaning"),
        (7, "صيانة", "Maintenance"),
        (8, "مستلزمات مكتبية", "Office supplies"),
        (9, "نقل وتوصيل", "Transport and delivery"),
        (10, "رسوم حكومية", "Government fees"),
        (11, "أخرى", "Other"),
    ];
}

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", Schemas.Expenses, t =>
        {
            t.HasCheckConstraint("CK_Expenses_Amount", "[Amount] > 0");
            t.HasCheckConstraint(
                "CK_Expenses_Reversal",
                "([IsReversed] = 0 AND [ReversedAtUtc] IS NULL AND [ReverseReason] IS NULL) "
                + "OR ([IsReversed] = 1 AND [ReversedAtUtc] IS NOT NULL AND [ReverseReason] IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Number).HasMaxLength(Expense.NumberMaxLength).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(Expense.DescriptionMaxLength).IsRequired();
        builder.Property(e => e.Payee).HasMaxLength(Expense.PayeeMaxLength);
        builder.Property(e => e.UserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength).IsRequired();
        builder.Property(e => e.ReversedByUserName).HasMaxLength(SupplierAccountEntry.UserNameMaxLength);
        builder.Property(e => e.ReverseReason).HasMaxLength(Expense.ReasonMaxLength);
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.Number).IsUnique();
        builder.HasIndex(e => e.OperationId).IsUnique();
        builder.HasIndex(e => e.ReverseOperationId).IsUnique().HasFilter("[ReverseOperationId] IS NOT NULL");
        builder.HasIndex(e => e.ExpenseDate);
        builder.HasIndex(e => new { e.CategoryId, e.ExpenseDate });
    }
}
