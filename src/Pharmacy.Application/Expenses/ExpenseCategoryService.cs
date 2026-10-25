using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Expenses;

namespace Pharmacy.Application.Expenses;

/// <summary>
/// Maintains the list of expense categories. Reading needs Expenses.View or Expenses.Create (the form must list them);
/// changes need Expenses.Manage and are audited. Categories are deactivated, never deleted.
/// </summary>
public sealed class ExpenseCategoryService(IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog)
{
    public async Task<Result<IReadOnlyList<LookupItem>>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.ExpensesView) && !currentUser.HasPermission(Permissions.ExpensesCreate))
        {
            return Result.Failure<IReadOnlyList<LookupItem>>(new Error(CommonErrors.Forbidden));
        }

        var query = db.ExpenseCategories.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        IReadOnlyList<LookupItem> items = await query
            .OrderBy(c => c.Id)
            .Select(c => new LookupItem(c.Id, c.NameArabic, c.NameEnglish, c.IsActive))
            .ToListAsync(cancellationToken);
        return Result.Success(items);
    }

    public async Task<Result<int>> CreateAsync(string nameArabic, string nameEnglish, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.ExpensesManage))
        {
            return Result.Failure<int>(new Error(CommonErrors.Forbidden));
        }

        var category = new ExpenseCategory();
        var result = category.Update(nameArabic, nameEnglish, isActive: true);
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.Error!);
        }

        return await db.RunGuardedAsync(async () =>
        {
            if (await IsNameTakenAsync(category, cancellationToken))
            {
                return Result.Failure<int>(new Error(CatalogErrors.NameTaken));
            }

            db.ExpenseCategories.Add(category);
            auditLog.Record(AuditActions.ExpenseCategoryCreated, category.NameEnglish, new { category.NameArabic, category.NameEnglish });
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(category.Id);
        }, Result.Failure<int>);
    }

    public async Task<Result> UpdateAsync(
        int id, string nameArabic, string nameEnglish, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.ExpensesManage))
        {
            return Result.Failure(new Error(CommonErrors.Forbidden));
        }

        return await db.RunGuardedAsync(async () =>
        {
            var category = await db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (category is null)
            {
                return Result.Failure(new Error(CommonErrors.NotFound));
            }

            var before = new { category.NameArabic, category.NameEnglish, category.IsActive };
            var result = category.Update(nameArabic, nameEnglish, isActive);
            if (result.IsFailure)
            {
                return result;
            }

            if (await IsNameTakenAsync(category, cancellationToken))
            {
                return Result.Failure(new Error(CatalogErrors.NameTaken));
            }

            auditLog.Record(AuditActions.ExpenseCategoryUpdated, category.NameEnglish,
                new { Before = before, After = new { category.NameArabic, category.NameEnglish, category.IsActive } });
            return Result.Success();
        }, Result.Failure);
    }

    // The database collation is case-insensitive, so "Rent" and "rent" count as the same name.
    private Task<bool> IsNameTakenAsync(ExpenseCategory category, CancellationToken cancellationToken) =>
        db.ExpenseCategories.AnyAsync(
            c => c.Id != category.Id && (c.NameArabic == category.NameArabic || c.NameEnglish == category.NameEnglish),
            cancellationToken);
}
