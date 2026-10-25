using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class SupplierServiceTests : PurchasingTestBase
{
    [Fact]
    public async Task Create_StoresTheSupplierAndAuditsIt()
    {
        var id = await CreateSupplierAsync("Al-Noor Pharma");

        var supplier = await SupplierAsync(id);
        supplier.Name.ShouldBe("Al-Noor Pharma");
        supplier.IsActive.ShouldBeTrue();
        supplier.CurrentBalance.ShouldBe(0m);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SupplierCreated);
    }

    [Fact]
    public async Task Create_WithADuplicateName_IsRejected()
    {
        await CreateSupplierAsync("Al-Noor Pharma");

        var second = await Suppliers(s => s.CreateAsync(new SupplierInput("Al-Noor Pharma", null, null, null, true)));

        ShouldFailWith(second, PurchasingErrors.DuplicateSupplierName);
    }

    [Fact]
    public async Task Create_WithoutAName_IsRejected()
    {
        var result = await Suppliers(s => s.CreateAsync(new SupplierInput("  ", null, null, null, true)));

        ShouldFailWith(result, PurchasingErrors.SupplierNameRequired);
    }

    [Fact]
    public async Task Update_ChangesTheDetails_AndRefusesAStaleVersion()
    {
        var id = await CreateSupplierAsync();
        var loaded = (await SupplierQuery(s => s.GetAsync(id))).Value;

        var first = await Suppliers(s => s.UpdateAsync(id, new SupplierInput("Al-Noor Pharma", "0111", "Cairo", null, true), loaded.RowVersion));
        var stale = await Suppliers(s => s.UpdateAsync(id, new SupplierInput("Al-Noor Pharma", "0222", null, null, true), loaded.RowVersion));

        first.IsSuccess.ShouldBeTrue(first.Error?.Code);
        ShouldFailWith(stale, CommonErrors.ConcurrencyConflict);
        (await SupplierAsync(id)).Phone.ShouldBe("0111");
        (await AuditActionsAsync()).ShouldContain(AuditActions.SupplierUpdated);
    }

    [Fact]
    public async Task Manage_WithoutPermission_IsForbidden()
    {
        SignIn(Permissions.SuppliersView);

        var result = await Suppliers(s => s.CreateAsync(new SupplierInput("X", null, null, null, true)));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }
}
