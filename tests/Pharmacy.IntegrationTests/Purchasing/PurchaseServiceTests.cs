using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class PurchaseServiceTests : PurchasingTestBase
{
    [Fact]
    public async Task CashPurchase_ReceivesStockPaysFromTheDrawerAndRecordsTheInvoice()
    {
        await OpenShiftAsync(1000m);

        var id = await BuyAsync([BoxLine(2, 120m)], paidCash: 240m);

        var invoice = (await InvoicesAsync()).Single();
        invoice.Id.ShouldBe(id);
        invoice.Number.ShouldBe($"PI-{Today.Year}-000001");
        invoice.Total.ShouldBe(240m);
        invoice.PaidCash.ShouldBe(240m);
        invoice.OnCredit.ShouldBe(0m);

        // 2 boxes = 6 strips; one strip costs 120 / 3 = 40.
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(6);
        batch.UnitCost.ShouldBe(40m);
        batch.BatchNumber.ShouldBe("B1");
        invoice.Lines.Single().BatchId.ShouldBe(batch.Id);

        var movement = (await MovementsAsync()).Single();
        movement.Type.ShouldBe(StockMovementType.Purchase);
        movement.ReferenceType.ShouldBe("PurchaseInvoice");
        movement.ReferenceId.ShouldBe(id);

        (await DrawerAsync()).ShouldBe(760m);
        var cash = (await CashMovementsAsync()).Single();
        cash.Type.ShouldBe(CashMovementType.PurchasePayment);
        cash.Amount.ShouldBe(-240m);
        cash.ReferenceId.ShouldBe(id);

        (await AuditActionsAsync()).ShouldContain(AuditActions.PurchaseCreated);
    }

    [Fact]
    public async Task CreditPurchase_AddsToWhatTheSupplierIsOwed_AndNeedsNoShift()
    {
        var supplier = await CreateSupplierAsync();

        var id = await BuyAsync([BoxLine(2, 120m)], supplier);

        var invoice = (await InvoicesAsync()).Single();
        invoice.OnCredit.ShouldBe(240m);
        invoice.PaidCash.ShouldBe(0m);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(240m);
        var entry = (await EntriesAsync(supplier)).Single();
        entry.Type.ShouldBe(SupplierEntryType.Invoice);
        entry.ReferenceId.ShouldBe(id);
        (await CashMovementsAsync()).ShouldBeEmpty();
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task MixedPurchase_SplitsBetweenTheDrawerAndTheSupplier()
    {
        var supplier = await CreateSupplierAsync();
        await OpenShiftAsync(1000m);

        await BuyAsync([BoxLine(2, 120m)], supplier, paidCash: 100m);

        (await DrawerAsync()).ShouldBe(900m);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(140m);
        (await InvoicesAsync()).Single().OnCredit.ShouldBe(140m);
    }

    [Fact]
    public async Task CreditPurchaseWithoutARegisteredSupplier_IsRejected()
    {
        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m)], supplierId: null, paidCash: 50m)));

        ShouldFailWith(result, PurchasingErrors.SupplierRequiredForCredit);
        (await InvoicesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CashPurchase_WithoutAnOpenShift_ChangesNothing()
    {
        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m)], paidCash: 100m)));

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await InvoicesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).ShouldBeEmpty();
        (await MovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CashPurchase_BiggerThanTheDrawer_IsRolledBackCompletely()
    {
        await OpenShiftAsync(100m);

        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(2, 120m)], paidCash: 240m)));

        ShouldFailWith(result, CashErrors.InsufficientCash);
        (await InvoicesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).ShouldBeEmpty();
        (await DrawerAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task ProductThatExpires_NeedsAnExpiryDate()
    {
        var supplier = await CreateSupplierAsync();

        var result = await Purchasing(s => s.CreateAsync(Request(
            [new PurchaseLineRequest(ProductId, BoxId, 1, 100m, "B1", null)], supplierId: supplier)));

        ShouldFailWith(result, InventoryErrors.ExpiryRequired);
    }

    [Fact]
    public async Task ExpiredStock_IsRejected()
    {
        var supplier = await CreateSupplierAsync();

        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m, expiry: Today.AddDays(-1))], supplier)));

        ShouldFailWith(result, PurchasingErrors.ExpiryInPast);
        (await BatchesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ProductWithoutExpiry_IsReceivedWithoutADate()
    {
        var (plain, _, plainBox) = await CreateProductAsync("Cotton wool", tracksExpiry: false);
        var supplier = await CreateSupplierAsync();

        var result = await Purchasing(s => s.CreateAsync(Request(
            [new PurchaseLineRequest(plain, plainBox, 1, 90m, "C1", null)], supplier)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await BatchesAsync(plain)).Single().ExpiryDate.ShouldBeNull();
    }

    [Fact]
    public async Task TwoLinesForTheSameBatch_TopUpOneBatch()
    {
        var supplier = await CreateSupplierAsync();

        await BuyAsync([BoxLine(1, 120m), BoxLine(2, 120m)], supplier);

        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(9);
        (await InvoicesAsync()).Single().Lines.Select(l => l.BatchId).ShouldAllBe(b => b == batch.Id);
    }

    [Fact]
    public async Task SameBatchNumberWithADifferentCost_CreatesAnotherBatch()
    {
        var supplier = await CreateSupplierAsync();

        await BuyAsync([BoxLine(1, 120m), BoxLine(1, 150m)], supplier);

        (await BatchesAsync()).Select(b => b.UnitCost).Order().ShouldBe([40m, 50m]);
    }

    [Fact]
    public async Task RepeatedSubmission_PostsTheInvoiceOnce()
    {
        var supplier = await CreateSupplierAsync();
        var request = Request([BoxLine(2, 120m)], supplier);

        var first = await Purchasing(s => s.CreateAsync(request));
        var second = await Purchasing(s => s.CreateAsync(request));

        second.Value.ShouldBe(first.Value);
        (await InvoicesAsync()).Count.ShouldBe(1);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(6);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(240m);
    }

    [Fact]
    public async Task InvoiceNumbers_AreSequentialPerYearOfTheInvoiceDate()
    {
        var supplier = await CreateSupplierAsync();

        await BuyAsync([BoxLine(1, 100m)], supplier);
        await BuyAsync([BoxLine(1, 100m, batch: "B2")], supplier);
        await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m, batch: "B3")], supplier, date: new DateOnly(Today.Year - 1, 12, 31))));

        (await InvoicesAsync()).Select(i => i.Number).ShouldBe(
            [$"PI-{Today.Year}-000001", $"PI-{Today.Year}-000002", $"PI-{Today.Year - 1}-000001"]);
    }

    [Fact]
    public async Task FutureInvoiceDate_IsRejected()
    {
        var supplier = await CreateSupplierAsync();

        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m)], supplier, date: Today.AddDays(1))));

        ShouldFailWith(result, PurchasingErrors.InvoiceDateInFuture);
    }

    [Fact]
    public async Task InactiveSupplier_CannotBeBoughtFrom()
    {
        var supplier = await CreateSupplierAsync();
        var detail = (await SupplierQuery(s => s.GetAsync(supplier))).Value;
        await Suppliers(s => s.UpdateAsync(supplier, new SupplierInput(detail.Name, null, null, null, false), detail.RowVersion));

        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m)], supplier)));

        ShouldFailWith(result, PurchasingErrors.InactiveSupplier);
    }

    [Fact]
    public async Task InvoiceWithoutLines_IsRejected()
    {
        var supplier = await CreateSupplierAsync();

        var result = await Purchasing(s => s.CreateAsync(Request([], supplier)));

        ShouldFailWith(result, PurchasingErrors.NoLines);
    }

    [Fact]
    public async Task User_WithoutPermission_IsForbidden()
    {
        var supplier = await CreateSupplierAsync();
        SignIn(Permissions.PurchasingView);

        var result = await Purchasing(s => s.CreateAsync(Request([BoxLine(1, 100m)], supplier)));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }
}
