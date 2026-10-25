using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Purchasing;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.IntegrationTests.Inventory;

namespace Pharmacy.IntegrationTests.Purchasing;

/// <summary>
/// The inventory test database (one expiry-tracked product: strip, box of 3 strips) with a signed-in user who may
/// buy, pay suppliers, and use the cash register.
/// </summary>
public abstract class PurchasingTestBase : InventoryTestBase
{
    protected static readonly string[] Buyer =
    [
        Permissions.CatalogView, Permissions.CatalogManage, Permissions.InventoryView, Permissions.InventoryViewCost,
        Permissions.SuppliersView, Permissions.SuppliersManage, Permissions.PurchasingView, Permissions.PurchasingCreate,
        Permissions.PayablesPay, Permissions.PayablesReverse, Permissions.PayablesSetOpeningBalance,
        Permissions.CashView, Permissions.CashOpenShift, Permissions.CashManualMovement, Permissions.SettingsManage,
    ];

    protected override string[] InitialPermissions => Buyer;

    protected static DateOnly InFuture => Today.AddDays(400);

    protected Task<T> Purchasing<T>(Func<PurchaseService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<PurchaseService>()));

    protected Task<T> Payables<T>(Func<PayablesService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<PayablesService>()));

    protected Task<T> Suppliers<T>(Func<SupplierService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SupplierService>()));

    protected Task<T> SupplierQuery<T>(Func<SupplierQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SupplierQueryService>()));

    protected Task<T> PurchaseQuery<T>(Func<PurchaseQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<PurchaseQueryService>()));

    protected async Task<int> CreateSupplierAsync(string name = "Al-Noor Pharma")
    {
        var result = await Suppliers(s => s.CreateAsync(new SupplierInput(name, "0100", null, null, true)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected async Task<int> OpenShiftAsync(decimal opening = 1000m)
    {
        var result = await Services.InScopeAsync(s => s.GetRequiredService<CashService>()
            .OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), opening, "float")));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    /// <summary>A line of boxes (3 strips each) at the given pack cost, with the standard future expiry.</summary>
    protected PurchaseLineRequest BoxLine(int boxes, decimal costPerBox, string? batch = "B1", DateOnly? expiry = null, int? productId = null, int? unitId = null) =>
        new(productId ?? ProductId, unitId ?? BoxId, boxes, costPerBox, batch, expiry ?? InFuture);

    protected PurchaseRequest Request(
        IReadOnlyList<PurchaseLineRequest> lines, int? supplierId = null, decimal paidCash = 0m, Guid? operationId = null, DateOnly? date = null) =>
        new(operationId ?? Guid.NewGuid(), supplierId, null, date ?? Today, "INV-77", paidCash, null, lines);

    protected async Task<int> BuyAsync(IReadOnlyList<PurchaseLineRequest> lines, int? supplierId = null, decimal paidCash = 0m)
    {
        var result = await Purchasing(s => s.CreateAsync(Request(lines, supplierId, paidCash)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected Task<Supplier> SupplierAsync(int id) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Suppliers.AsNoTracking().SingleAsync(x => x.Id == id));

    protected Task<List<SupplierAccountEntry>> EntriesAsync(int supplierId) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().SupplierAccountEntries.AsNoTracking()
            .Where(e => e.SupplierId == supplierId).OrderBy(e => e.Id).ToListAsync());

    protected Task<List<PurchaseInvoice>> InvoicesAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().PurchaseInvoices.AsNoTracking().Include(i => i.Lines).OrderBy(i => i.Id).ToListAsync());

    protected Task<List<CashMovement>> CashMovementsAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync());

    protected async Task<decimal> DrawerAsync() =>
        await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashShifts.AsNoTracking()
            .Where(x => x.IsOpen).Select(x => x.CurrentBalance).SingleAsync());

    /// <summary>A supplier's balance must equal the sum of its statement, and every line must carry the running balance.</summary>
    protected async Task AssertStatementMatchesBalanceAsync(int supplierId)
    {
        var entries = await EntriesAsync(supplierId);
        var running = 0m;
        foreach (var entry in entries)
        {
            running += entry.Amount;
            entry.BalanceAfter.ShouldBe(running);
        }

        (await SupplierAsync(supplierId)).CurrentBalance.ShouldBe(running);
    }

    protected static void ShouldFailWith(Result result, string code) => result.Error?.Code.ShouldBe(code);
}
