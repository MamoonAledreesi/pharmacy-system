using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Sales;
using Pharmacy.Domain.Sales;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Sales;

/// <summary>The sales test database with a signed-in user who may take goods back (but not after the return period).</summary>
public abstract class ReturnsTestBase : SalesTestBase
{
    protected static readonly string[] Returner = [.. Seller, Permissions.SalesReturn];

    protected static readonly string[] LateReturner = [.. Returner, Permissions.SalesLateReturn];

    protected override string[] InitialPermissions => Returner;

    protected Task<T> Returns<T>(Func<SalesReturnService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SalesReturnService>()));

    protected Task<T> ReturnsQuery<T>(Func<SalesReturnQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SalesReturnQueryService>()));

    protected ReturnLineRequest Back(int invoiceLineId, int quantity, bool sellable = true, int? unitId = null) =>
        new(invoiceLineId, unitId ?? StripId, quantity, sellable);

    protected SalesReturnRequest Return(int invoiceId, IReadOnlyList<ReturnLineRequest> lines, string? reason = null, Guid? operationId = null) =>
        new(operationId ?? Guid.NewGuid(), invoiceId, lines, reason);

    protected async Task<int> LineIdAsync(int invoiceId, int index = 0) =>
        (await InvoicesOfSalesAsync()).Single(i => i.Id == invoiceId).Lines.OrderBy(l => l.Id).ElementAt(index).Id;

    protected Task<List<SalesReturn>> ReturnsOfSalesAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().SalesReturns.AsNoTracking()
            .Include(r => r.Lines).ThenInclude(l => l.Allocations).OrderBy(r => r.Id).ToListAsync());

    /// <summary>Makes a sale look as if it happened <paramref name="days"/> days ago.</summary>
    protected Task AgeInvoiceAsync(int invoiceId, int days) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales.SalesInvoices SET CreatedAtUtc = DATEADD(day, {-days}, CreatedAtUtc) WHERE Id = {invoiceId}"));

    protected Task ExpireBatchAsync(int batchId) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory.Batches SET ExpiryDate = DATEADD(day, -1, CAST(GETDATE() AS date)) WHERE Id = {batchId}"));
}
