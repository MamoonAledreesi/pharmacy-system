using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Purchasing;

/// <summary>The purchasing test database with a signed-in user who may also send goods back to suppliers and move stock to quarantine.</summary>
public abstract class PurchaseReturnsTestBase : PurchasingTestBase
{
    protected static readonly string[] Returner = [.. Buyer, Permissions.PurchasingReturn, Permissions.InventoryAdjust];

    protected override string[] InitialPermissions => Returner;

    protected Task<T> PurchaseReturns<T>(Func<PurchaseReturnService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<PurchaseReturnService>()));

    protected Task<T> PurchaseReturnQuery<T>(Func<PurchaseReturnQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<PurchaseReturnQueryService>()));

    protected PurchaseReturnLineRequest Out(int invoiceLineId, int quantity, bool fromQuarantine = false, int? unitId = null) =>
        new(invoiceLineId, unitId ?? StripId, quantity, fromQuarantine);

    protected PurchaseReturnRequest SendBack(
        int invoiceId, IReadOnlyList<PurchaseReturnLineRequest> lines, string? reason = "damaged in transit", Guid? operationId = null) =>
        new(operationId ?? Guid.NewGuid(), invoiceId, lines, reason);

    protected async Task<int> LineIdAsync(int invoiceId, int index = 0) =>
        (await InvoicesAsync()).Single(i => i.Id == invoiceId).Lines.OrderBy(l => l.Id).ElementAt(index).Id;

    protected Task<List<PurchaseReturn>> ReturnsOfPurchasesAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().PurchaseReturns.AsNoTracking()
            .Include(r => r.Lines).OrderBy(r => r.Id).ToListAsync());

    protected async Task QuarantineAsync(int batchId, int strips)
    {
        var result = await Inventory(s => s.QuarantineAsync(new StockActionRequest(Guid.NewGuid(), batchId, StripId, strips, StockReasons.Damaged, null)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
    }
}
