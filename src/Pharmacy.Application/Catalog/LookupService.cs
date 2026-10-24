using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Catalog;

public sealed record LookupItem(int Id, string NameArabic, string NameEnglish, bool IsActive)
{
    public string DisplayName(bool arabic) => arabic ? NameArabic : NameEnglish;
}

/// <summary>
/// Maintains one bilingual reference list (categories, units of measure, or dosage forms).
/// Reading needs Catalog.View; changes need Catalog.Manage and are audited.
/// </summary>
public sealed class LookupService<TLookup>(IPharmacyDbContext db, ICurrentUser currentUser, IAuditLog auditLog)
    where TLookup : NamedLookup, new()
{
    public async Task<Result<IReadOnlyList<LookupItem>>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogView))
        {
            return Result.Failure<IReadOnlyList<LookupItem>>(new Error(CommonErrors.Forbidden));
        }

        var query = db.Set<TLookup>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive);
        }

        IReadOnlyList<LookupItem> items = await query
            .OrderBy(l => l.Id)
            .Select(l => new LookupItem(l.Id, l.NameArabic, l.NameEnglish, l.IsActive))
            .ToListAsync(cancellationToken);
        return Result.Success(items);
    }

    public async Task<Result<int>> CreateAsync(string nameArabic, string nameEnglish, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return Result.Failure<int>(new Error(CommonErrors.Forbidden));
        }

        var item = new TLookup();
        var result = item.Update(nameArabic, nameEnglish, isActive: true);
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.Error!);
        }

        if (await IsNameTakenAsync(item, cancellationToken))
        {
            return Result.Failure<int>(new Error(CatalogErrors.NameTaken));
        }

        db.Set<TLookup>().Add(item);
        auditLog.Record(AuditActions.LookupCreated, Target(item), new { item.NameArabic, item.NameEnglish });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(item.Id);
    }

    public async Task<Result> UpdateAsync(
        int id, string nameArabic, string nameEnglish, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!currentUser.HasPermission(Permissions.CatalogManage))
        {
            return Result.Failure(new Error(CommonErrors.Forbidden));
        }

        var item = await db.Set<TLookup>().SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(new Error(CommonErrors.NotFound));
        }

        var before = new { item.NameArabic, item.NameEnglish, item.IsActive };
        var result = item.Update(nameArabic, nameEnglish, isActive);
        if (result.IsFailure)
        {
            return result;
        }

        if (await IsNameTakenAsync(item, cancellationToken))
        {
            return Result.Failure(new Error(CatalogErrors.NameTaken));
        }

        auditLog.Record(AuditActions.LookupUpdated, Target(item),
            new { Before = before, After = new { item.NameArabic, item.NameEnglish, item.IsActive } });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // The database collation is case-insensitive, so "Box" and "box" count as the same name.
    private Task<bool> IsNameTakenAsync(TLookup item, CancellationToken cancellationToken) =>
        db.Set<TLookup>().AnyAsync(
            l => l.Id != item.Id && (l.NameArabic == item.NameArabic || l.NameEnglish == item.NameEnglish),
            cancellationToken);

    private static string Target(TLookup item) => $"{typeof(TLookup).Name}: {item.NameEnglish}";
}
