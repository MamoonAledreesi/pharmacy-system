using Microsoft.EntityFrameworkCore;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Domain.Common;

namespace Pharmacy.Application.Auditing;

public sealed record AuditLogFilter(DateTime? FromUtc, DateTime? ToUtc, string? UserName, string? Action);

public sealed record AuditLogRow(long Id, DateTime OccurredAtUtc, string UserName, string Action, string? Target, string? Details);

public sealed class AuditLogQueryService(IPharmacyDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 200;

    public async Task<Result<PagedResult<AuditLogRow>>> SearchAsync(
        AuditLogFilter filter, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!currentUser.HasPermission(Permissions.AuditLogView))
        {
            return Result.Failure<PagedResult<AuditLogRow>>(new Error(CommonErrors.Forbidden));
        }

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        pageIndex = Math.Max(pageIndex, 0);

        var query = db.AuditLogEntries.AsNoTracking();
        if (filter.FromUtc is { } from)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(e => e.OccurredAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var userName = filter.UserName.Trim();
            query = query.Where(e => e.UserName.Contains(userName));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(e => e.Action == filter.Action);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(e => new AuditLogRow(e.Id, e.OccurredAtUtc, e.UserName, e.Action, e.Target, e.Details))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<AuditLogRow>(items, total));
    }
}
