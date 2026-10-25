using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class PurchaseReturnServiceTests : PurchaseReturnsTestBase
{
    [Fact]
    public async Task ReturningACashPurchase_TakesTheGoodsOutAndReceivesTheCash()
    {
        await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], paidCash: 240m);
        var line = await LineIdAsync(invoice);
        var batch = (await BatchesAsync()).Single();

        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 6)])));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var purchaseReturn = (await ReturnsOfPurchasesAsync()).Single();
        purchaseReturn.Number.ShouldBe($"PR-{DateTime.Now.Year}-000001");
        purchaseReturn.Total.ShouldBe(240m);
        purchaseReturn.CashRefund.ShouldBe(240m);
        purchaseReturn.DebtReduction.ShouldBe(0m);
        purchaseReturn.SupplierId.ShouldBeNull();
        purchaseReturn.Lines.Single().BatchId.ShouldBe(batch.Id);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(0);
        var movement = (await MovementsAsync()).Last();
        movement.Type.ShouldBe(StockMovementType.PurchaseReturn);
        movement.ReferenceId.ShouldBe(result.Value);

        (await DrawerAsync()).ShouldBe(1000m);
        (await CashMovementsAsync()).Last().Type.ShouldBe(CashMovementType.PurchaseRefund);
        (await AuditActionsAsync()).ShouldContain(AuditActions.PurchaseReturned);
    }

    [Fact]
    public async Task ReturningACreditPurchase_ReducesWhatIsOwed_AndNeedsNoShift()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier);
        var line = await LineIdAsync(invoice);

        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1, unitId: BoxId)])));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var purchaseReturn = (await ReturnsOfPurchasesAsync()).Single();
        purchaseReturn.Total.ShouldBe(120m);
        purchaseReturn.DebtReduction.ShouldBe(120m);
        purchaseReturn.CashRefund.ShouldBe(0m);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(120m);
        (await EntriesAsync(supplier)).Last().Type.ShouldBe(SupplierEntryType.PurchaseReturn);
        (await CashMovementsAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(3);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task AMixedPurchase_ReturnedInFull_ReducesTheCreditPartAndReceivesTheRestInCash()
    {
        var supplier = await CreateSupplierAsync();
        await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier, paidCash: 100m);
        var line = await LineIdAsync(invoice);

        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 6)])));

        var purchaseReturn = (await ReturnsOfPurchasesAsync()).Single();
        purchaseReturn.DebtReduction.ShouldBe(140m);
        purchaseReturn.CashRefund.ShouldBe(100m);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
        (await DrawerAsync()).ShouldBe(1000m);
    }

    [Fact]
    public async Task TheCreditPartOfAnInvoice_IsReducedOnlyOnce_AcrossSeveralReturns()
    {
        var supplier = await CreateSupplierAsync();
        await OpenShiftAsync(1000m);
        // Total 360: 220 in cash and 140 on credit. Three returns of one box (120) each.
        var invoice = await BuyAsync([BoxLine(3, 120m)], supplier, paidCash: 220m);
        var line = await LineIdAsync(invoice);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            (await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1, unitId: BoxId)])))).IsSuccess.ShouldBeTrue();
        }

        var all = await ReturnsOfPurchasesAsync();
        all.Select(r => r.DebtReduction).ShouldBe([120m, 20m, 0m]);
        all.Select(r => r.CashRefund).ShouldBe([0m, 100m, 120m]);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task ASupplierWhoWasAlreadyPaid_RefundsInCash_AndTheBalanceStaysZero()
    {
        var supplier = await CreateSupplierAsync();
        await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier);
        await Payables(s => s.PayAsync(new PaySupplierRequest(Guid.NewGuid(), supplier, 240m, null)));
        var line = await LineIdAsync(invoice);

        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 6)])));

        var purchaseReturn = (await ReturnsOfPurchasesAsync()).Single();
        purchaseReturn.DebtReduction.ShouldBe(0m);
        purchaseReturn.CashRefund.ShouldBe(240m);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
        (await DrawerAsync()).ShouldBe(1000m);
    }

    [Fact]
    public async Task SeveralPartialReturns_AddUpToExactlyTheLineTotal()
    {
        var supplier = await CreateSupplierAsync();
        // One box (3 strips) for 100: a third is 33.333..., so the cents must be settled.
        var invoice = await BuyAsync([BoxLine(1, 100m)], supplier);
        var line = await LineIdAsync(invoice);

        foreach (var _ in Enumerable.Range(0, 3))
        {
            (await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1)])))).IsSuccess.ShouldBeTrue();
        }

        (await ReturnsOfPurchasesAsync()).Select(r => r.Total).ShouldBe([33.33m, 33.33m, 33.34m]);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task GoodsCanGoBackFromQuarantine()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier);
        var line = await LineIdAsync(invoice);
        await QuarantineAsync((await BatchesAsync()).Single().Id, 3);

        var tooMany = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 4, fromQuarantine: true)])));
        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 3, fromQuarantine: true)])));

        ShouldFailWith(tooMany, PurchasingErrors.BatchStockTooLow);
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(3);
        batch.QuantityQuarantined.ShouldBe(0);
        (await MovementsAsync()).Last().Type.ShouldBe(StockMovementType.PurchaseReturnQuarantined);
    }

    [Fact]
    public async Task OnlyWhatTheBatchStillHolds_CanGoBack()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier);
        var line = await LineIdAsync(invoice);
        // Four of the six strips were sold or lost since.
        var batchId = (await BatchesAsync()).Single().Id;
        await Inventory(s => s.AdjustAsync(AdjustmentDirection.Decrease,
            new StockActionRequest(Guid.NewGuid(), batchId, StripId, 4, StockReasons.CountDifference, null)));

        var tooMany = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 3)])));
        var enough = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])));

        ShouldFailWith(tooMany, PurchasingErrors.BatchStockTooLow);
        enough.IsSuccess.ShouldBeTrue(enough.Error?.Code);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(0);
    }

    [Fact]
    public async Task ReturningMoreThanWasReceived_IsRejected()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(1, 120m)], supplier);
        var line = await LineIdAsync(invoice);
        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])));

        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])));

        ShouldFailWith(result, PurchasingErrors.ReturnQuantityExceeds);
        (await ReturnsOfPurchasesAsync()).Count.ShouldBe(1);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(1);
    }

    [Fact]
    public async Task ACashRefund_NeedsAnOpenShift_AndChangesNothingWithout()
    {
        var shift = await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], paidCash: 240m);
        var line = await LineIdAsync(invoice);
        await Services.InScopeAsync(s => s.GetRequiredService<CashService>()
            .CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), shift, 760m, null)));

        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 6)])));

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await ReturnsOfPurchasesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(6);
    }

    [Fact]
    public async Task TheReason_IsRequired()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(1, 120m)], supplier);
        var line = await LineIdAsync(invoice);

        var result = await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1)], reason: "  ")));

        ShouldFailWith(result, PurchasingErrors.ReturnReasonRequired);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(3);
    }

    [Fact]
    public async Task ARepeatedSubmission_PostsTheReturnOnce()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier);
        var request = SendBack(invoice, [Out(await LineIdAsync(invoice), 1)]);

        var first = await PurchaseReturns(s => s.CreateAsync(request));
        var second = await PurchaseReturns(s => s.CreateAsync(request));

        second.Value.ShouldBe(first.Value);
        (await ReturnsOfPurchasesAsync()).Count.ShouldBe(1);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(5);
    }

    [Fact]
    public async Task PreviewShowsTheSplit_AndChangesNothing()
    {
        var supplier = await CreateSupplierAsync();
        await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], supplier, paidCash: 100m);

        var line = await LineIdAsync(invoice);
        var preview = (await PurchaseReturns(s => s.PreviewAsync(SendBack(invoice, [Out(line, 6)])))).Value;

        preview.Total.ShouldBe(240m);
        preview.DebtReduction.ShouldBe(140m);
        preview.CashRefund.ShouldBe(100m);
        (await ReturnsOfPurchasesAsync()).ShouldBeEmpty();
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(140m);
    }

    [Fact]
    public async Task InvalidRequests_AreRejected()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(1, 120m)], supplier);
        var line = await LineIdAsync(invoice);

        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, []))), PurchasingErrors.ReturnNoLines);
        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1), Out(line, 1)]))), PurchasingErrors.DuplicateReturnLine);
        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(9999, 1)]))), PurchasingErrors.UnknownInvoiceLine);
        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 0)]))), InventoryErrors.InvalidQuantity);
        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(9999, [Out(line, 1)]))), PurchasingErrors.UnknownInvoice);
        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1, unitId: 9999)]))), InventoryErrors.UnknownUnit);
    }

    [Fact]
    public async Task Returning_WithoutPermission_IsForbidden()
    {
        var supplier = await CreateSupplierAsync();
        var invoice = await BuyAsync([BoxLine(1, 120m)], supplier);
        var line = await LineIdAsync(invoice);
        SignIn(Permissions.PurchasingView);

        ShouldFailWith(await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1)]))), CommonErrors.Forbidden);
        ShouldFailWith(await PurchaseReturns(s => s.PreviewAsync(SendBack(invoice, [Out(line, 1)]))), CommonErrors.Forbidden);
    }

    [Fact]
    public async Task StockAndCash_AfterReturns_MatchTheirLedgers()
    {
        await OpenShiftAsync(1000m);
        var invoice = await BuyAsync([BoxLine(2, 120m)], paidCash: 240m);
        var line = await LineIdAsync(invoice);
        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 2)])));
        await PurchaseReturns(s => s.CreateAsync(SendBack(invoice, [Out(line, 1, unitId: BoxId)])));

        var movements = await MovementsAsync();
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(movements.Sum(m => m.AvailableDelta));
        batch.QuantityAvailable.ShouldBe(1);
        (await DrawerAsync()).ShouldBe(1000m - 240m + (await CashMovementsAsync()).Where(m => m.Type == CashMovementType.PurchaseRefund).Sum(m => m.Amount));
    }
}
