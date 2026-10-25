using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Receivables;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.IntegrationTests.Sales;

public sealed class SalesReturnServiceTests : ReturnsTestBase
{
    [Fact]
    public async Task ReturningACashSale_PaysTheCashBackAndPutsTheGoodsOnTheShelf()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);

        var line = await LineIdAsync(invoice);
        var result = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3)])));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.Number.ShouldBe($"SR-{DateTime.Now.Year}-000001");
        salesReturn.Total.ShouldBe(45m);
        salesReturn.CashRefund.ShouldBe(45m);
        salesReturn.DebtReduction.ShouldBe(0m);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
        var movement = (await MovementsAsync()).Last();
        movement.Type.ShouldBe(StockMovementType.SaleReturn);
        movement.ReferenceId.ShouldBe(result.Value);

        (await DrawerAsync()).ShouldBe(100m);
        (await CashMovementsAsync()).Last().Type.ShouldBe(CashMovementType.SaleRefund);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SaleReturned);
    }

    [Fact]
    public async Task DamagedGoods_GoToQuarantine_NotBackOnTheShelf()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 2, sellable: false)])));

        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(7);
        batch.QuantityQuarantined.ShouldBe(2);
        (await MovementsAsync()).Last().Type.ShouldBe(StockMovementType.SaleReturnQuarantined);
    }

    [Fact]
    public async Task GoodsThatExpiredMeanwhile_AreNeverPutBackOnTheShelf()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        await ExpireBatchAsync((await BatchesAsync()).Single().Id);

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3, sellable: true)])));

        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(7);
        batch.QuantityQuarantined.ShouldBe(3);
    }

    [Fact]
    public async Task APartialReturn_IsWorthTheNetPriceOfWhatCameBack_AfterTheDiscount()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(3, 10m)], paid: 27m, discountAmount: 3m)))).Value;
        var line = await LineIdAsync(invoice);

        var one = await Returns(s => s.PreviewAsync(Return(invoice, [Back(line, 1)])));

        one.Value.Total.ShouldBe(9m);
        one.Value.CashRefund.ShouldBe(9m);
    }

    [Fact]
    public async Task SeveralPartialReturns_AddUpToExactlyWhatTheLineWasSoldFor()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        // 3 strips at 10 with a discount of 0.10 make a line worth 29.90: a third is 9.9666..., so the cents must be settled.
        var sold = await Sales(s => s.CreateAsync(Sale([Strips(3, 10m)], paid: 29.9m, discountAmount: 0.1m)));
        var line = await LineIdAsync(sold.Value);

        var values = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var result = await Returns(s => s.CreateAsync(Return(sold.Value, [Back(line, 1)])));
            result.IsSuccess.ShouldBeTrue(result.Error?.Code);
            values.Add((await ReturnsOfSalesAsync()).Last().Total);
        }

        values.Sum().ShouldBe(29.9m);
        values.ShouldBe([9.97m, 9.97m, 9.96m]);
        var tooMuch = await Returns(s => s.CreateAsync(Return(sold.Value, [Back(line, 1)])));
        ShouldFailWith(tooMuch, SalesErrors.ReturnQuantityExceeds);
    }

    [Fact]
    public async Task ReturningACreditSale_ReducesTheCustomerDebtFirst()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 0m)))).Value;
        await OpenShiftAsync(0m);

        var line = await LineIdAsync(invoice);
        var result = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 2)])));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.DebtReduction.ShouldBe(40m);
        salesReturn.CashRefund.ShouldBe(0m);
        (await CustomerBalanceAsync(customer)).ShouldBe(40m);
        (await CustomerEntriesAsync(customer)).Last().Type.ShouldBe(CustomerEntryType.SaleReturn);
        (await CashMovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AMixedSale_ReturnedInFull_ReducesTheDebtByTheCreditPartAndRefundsTheRestInCash()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 30m)))).Value;

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 4)])));

        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.DebtReduction.ShouldBe(50m);
        salesReturn.CashRefund.ShouldBe(30m);
        (await CustomerBalanceAsync(customer)).ShouldBe(0m);
        (await DrawerAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task TheCreditPartOfAnInvoice_IsReducedOnlyOnce_AcrossSeveralReturns()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        // Total 80: 30 in cash and 50 on credit. Four returns of 20 each.
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 30m)))).Value;
        var line = await LineIdAsync(invoice);

        foreach (var _ in Enumerable.Range(0, 4))
        {
            (await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)])))).IsSuccess.ShouldBeTrue();
        }

        var all = await ReturnsOfSalesAsync();
        all.Select(r => r.DebtReduction).ShouldBe([20m, 20m, 10m, 0m]);
        all.Select(r => r.CashRefund).ShouldBe([0m, 0m, 10m, 20m]);
        (await CustomerBalanceAsync(customer)).ShouldBe(0m);
    }

    [Fact]
    public async Task ACustomerWhoAlreadyPaidTheirDebt_GetsCashBack_AndTheirBalanceStaysZero()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(2, 20m)], customerId: customer, paid: 0m)))).Value;
        await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .CollectAsync(new CollectDebtRequest(Guid.NewGuid(), customer, 40m, null)));

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 2)])));

        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.DebtReduction.ShouldBe(0m);
        salesReturn.CashRefund.ShouldBe(40m);
        (await CustomerBalanceAsync(customer)).ShouldBe(0m);
    }

    [Fact]
    public async Task TheDebtReduction_NeverExceedsWhatTheCustomerOwesNow()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 0m)))).Value;
        await Services.InScopeAsync(s => s.GetRequiredService<ReceivablesService>()
            .CollectAsync(new CollectDebtRequest(Guid.NewGuid(), customer, 60m, null)));

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 4)])));

        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.DebtReduction.ShouldBe(20m);
        salesReturn.CashRefund.ShouldBe(60m);
        (await CustomerBalanceAsync(customer)).ShouldBe(0m);
    }

    [Fact]
    public async Task GoodsGoBackToTheBatchesTheyCameFrom_TheLastTakenFirst()
    {
        await StockAsync(2, costPerStrip: 5m, batch: "SOON", expiry: Today.AddDays(200));
        await StockAsync(5, costPerStrip: 7m, batch: "LATER", expiry: Today.AddDays(300));
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(4, 20m)], paid: 80m);

        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3)])));

        var allocations = (await ReturnsOfSalesAsync()).Single().Lines.Single().Allocations;
        allocations.Select(a => (a.BaseQuantity, a.UnitCost)).ShouldBe([(2, 7m), (1, 5m)]);
        (await BatchesAsync()).Select(b => b.QuantityAvailable).ShouldBe([1, 5]);
    }

    [Fact]
    public async Task AReturnInADifferentUnit_IsConvertedToBaseUnits()
    {
        await StockAsync(12);
        await OpenShiftAsync(0m);
        var invoice = await SellAsync([Strips(2, 40m, unitId: BoxId)], paid: 80m);
        var line = await LineIdAsync(invoice);

        var box = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1, unitId: BoxId)])));
        var tooManyStrips = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 4)])));
        var threeStrips = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3)])));

        box.IsSuccess.ShouldBeTrue(box.Error?.Code);
        ShouldFailWith(tooManyStrips, SalesErrors.ReturnQuantityExceeds);
        threeStrips.IsSuccess.ShouldBeTrue(threeStrips.Error?.Code);
        (await ReturnsOfSalesAsync()).Select(r => r.Total).ShouldBe([40m, 40m]);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(12);
    }

    [Fact]
    public async Task ReturningMoreThanWasSold_IsRejectedAndChangesNothing()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);

        var line = await LineIdAsync(invoice);
        var result = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 4)])));

        ShouldFailWith(result, SalesErrors.ReturnQuantityExceeds);
        (await ReturnsOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(7);
        (await DrawerAsync()).ShouldBe(145m);
    }

    [Fact]
    public async Task ACashRefund_NeedsAnOpenShift_AndTheDrawerToHoldTheMoney()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);
        // The sale money is taken out of the drawer by hand, so it cannot be paid back.
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Cash.CashService>()
            .RecordManualMovementAsync(new Pharmacy.Application.Cash.ManualCashRequest(Guid.NewGuid(), Pharmacy.Application.Cash.CashDirection.Out, 40m, "bank")));

        var poor = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3)])));

        ShouldFailWith(poor, CashErrors.InsufficientCash);
        (await ReturnsOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(7);
    }

    [Fact]
    public async Task ACashRefund_WithoutAnOpenShift_IsRolledBack()
    {
        await StockAsync(10);
        var shift = await OpenShiftAsync(0m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Cash.CashService>()
            .CloseShiftAsync(new Pharmacy.Application.Cash.CloseShiftRequest(Guid.NewGuid(), shift, 45m, null)));

        var result = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 3)])));

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await ReturnsOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(7);
    }

    [Fact]
    public async Task ARepeatedSubmission_PostsTheReturnOnce()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);
        var request = Return(invoice, [Back(line, 1)]);

        var first = await Returns(s => s.CreateAsync(request));
        var second = await Returns(s => s.CreateAsync(request));

        second.Value.ShouldBe(first.Value);
        (await ReturnsOfSalesAsync()).Count.ShouldBe(1);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(8);
        (await CashMovementsAsync()).Count(m => m.Type == CashMovementType.SaleRefund).ShouldBe(1);
    }

    [Fact]
    public async Task AfterTheReturnPeriod_OnlyAUserWithTheLateReturnPermissionMayReturn_WithAReason()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);
        await AgeInvoiceAsync(invoice, 20);

        var normal = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)], reason: "changed mind")));
        SignIn(LateReturner);
        var noReason = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)])));
        var late = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)], reason: "customer was ill")));

        ShouldFailWith(normal, SalesErrors.ReturnWindowPassed);
        ShouldFailWith(noReason, SalesErrors.LateReturnReasonRequired);
        late.IsSuccess.ShouldBeTrue(late.Error?.Code);
        var salesReturn = (await ReturnsOfSalesAsync()).Single();
        salesReturn.IsLate.ShouldBeTrue();
        salesReturn.Reason.ShouldBe("customer was ill");
        (await AuditActionsAsync()).ShouldContain(AuditActions.SaleLateReturn);
    }

    [Fact]
    public async Task TheReturnPeriod_FollowsTheSetting()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        await AgeInvoiceAsync(invoice, 20);
        var settings = (await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Settings.PharmacySettingsService>().GetForEditAsync())).Value;
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Settings.PharmacySettingsService>()
            .UpdateAsync(settings.Values with { ReturnWindowDays = 30 }, settings.RowVersion));

        var line = await LineIdAsync(invoice);
        var result = await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)])));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await ReturnsOfSalesAsync()).Single().IsLate.ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewShowsTheSplit_AndChangesNothing()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);
        var invoice = (await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 30m)))).Value;

        var line = await LineIdAsync(invoice);
        var preview = (await Returns(s => s.PreviewAsync(Return(invoice, [Back(line, 4)])))).Value;

        preview.Total.ShouldBe(80m);
        preview.DebtReduction.ShouldBe(50m);
        preview.CashRefund.ShouldBe(30m);
        preview.IsLate.ShouldBeFalse();
        (await ReturnsOfSalesAsync()).ShouldBeEmpty();
        (await CustomerBalanceAsync(customer)).ShouldBe(50m);
    }

    [Fact]
    public async Task InvalidRequests_AreRejected()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);

        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, []))), SalesErrors.ReturnNoLines);
        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1), Back(line, 1)]))), SalesErrors.DuplicateReturnLine);
        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, [Back(9999, 1)]))), SalesErrors.UnknownInvoiceLine);
        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 0)]))), InventoryErrors.InvalidQuantity);
        ShouldFailWith(await Returns(s => s.CreateAsync(Return(9999, [Back(line, 1)]))), SalesErrors.UnknownInvoice);
        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1, unitId: 9999)]))), InventoryErrors.UnknownUnit);
    }

    [Fact]
    public async Task Returning_WithoutPermission_IsForbidden()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(3, 15m)], paid: 45m);
        var line = await LineIdAsync(invoice);
        SignIn(Permissions.SalesView);

        ShouldFailWith(await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1)]))), CommonErrors.Forbidden);
        ShouldFailWith(await Returns(s => s.PreviewAsync(Return(invoice, [Back(line, 1)]))), CommonErrors.Forbidden);
    }

    [Fact]
    public async Task StockAndCash_AfterReturns_MatchTheirLedgers()
    {
        await StockAsync(10);
        await OpenShiftAsync(100m);
        var invoice = await SellAsync([Strips(5, 15m)], paid: 75m);
        var line = await LineIdAsync(invoice);
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 2)])));
        await Returns(s => s.CreateAsync(Return(invoice, [Back(line, 1, sellable: false)])));

        var movements = await MovementsAsync();
        var batch = (await BatchesAsync()).Single();
        batch.QuantityAvailable.ShouldBe(movements.Sum(m => m.AvailableDelta));
        batch.QuantityQuarantined.ShouldBe(movements.Sum(m => m.QuarantineDelta));
        batch.QuantityAvailable.ShouldBe(7);
        batch.QuantityQuarantined.ShouldBe(1);
        (await DrawerAsync()).ShouldBe(100m + (await CashMovementsAsync()).Sum(m => m.Amount));
    }
}
