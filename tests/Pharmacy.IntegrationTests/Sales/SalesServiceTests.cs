using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Sales;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Receivables;
using Pharmacy.Domain.Sales;

namespace Pharmacy.IntegrationTests.Sales;

public sealed class SalesServiceTests : SalesTestBase
{
    [Fact]
    public async Task CashSale_TakesStockReceivesCashAndRecordsTheCostOfWhatWasSold()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(100m);

        var id = await SellAsync([Strips(3, 15m)], paid: 45m);

        var invoice = (await InvoicesOfSalesAsync()).Single();
        invoice.Id.ShouldBe(id);
        invoice.Number.ShouldBe($"SI-{DateTime.Now.Year}-000001");
        invoice.Subtotal.ShouldBe(45m);
        invoice.Total.ShouldBe(45m);
        invoice.PaidCash.ShouldBe(45m);
        invoice.OnCredit.ShouldBe(0m);
        var line = invoice.Lines.Single();
        line.BaseQuantity.ShouldBe(3);
        line.CostTotal.ShouldBe(30m);
        line.Allocations.Single().UnitCost.ShouldBe(10m);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(7);
        var movement = (await MovementsAsync()).Last();
        movement.Type.ShouldBe(StockMovementType.Sale);
        movement.ReferenceId.ShouldBe(id);

        (await DrawerAsync()).ShouldBe(145m);
        var cash = (await CashMovementsAsync()).Single();
        cash.Type.ShouldBe(CashMovementType.SalePayment);
        cash.Amount.ShouldBe(45m);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SaleCreated);
    }

    [Fact]
    public async Task SellingABox_TakesThreeStrips()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        await SellAsync([Strips(2, 40m, unitId: BoxId)], paid: 80m);

        var line = (await InvoicesOfSalesAsync()).Single().Lines.Single();
        line.BaseQuantity.ShouldBe(6);
        line.UnitFactor.ShouldBe(3);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task ASaleBiggerThanTheFirstBatch_IsTakenFromTheNextExpiringBatches()
    {
        await StockAsync(2, costPerStrip: 10m, batch: "SOON", expiry: Today.AddDays(50));
        await StockAsync(5, costPerStrip: 12m, batch: "LATER", expiry: Today.AddDays(200));
        await OpenShiftAsync(0m);

        await SellAsync([Strips(4, 20m)], paid: 80m);

        var line = (await InvoicesOfSalesAsync()).Single().Lines.Single();
        line.Allocations.Select(a => (a.BaseQuantity, a.UnitCost)).ShouldBe([(2, 10m), (2, 12m)]);
        line.Allocations.First().IsNearExpiry.ShouldBeTrue();
        line.Allocations.Last().IsNearExpiry.ShouldBeFalse();
        line.CostTotal.ShouldBe(44m);
        (await BatchesAsync()).Select(b => b.QuantityAvailable).ShouldBe([0, 3]);
    }

    [Fact]
    public async Task ExpiredBatches_AreNeverSold()
    {
        await StockAsync(5, batch: "OLD", expiry: Today.AddDays(-1));
        await StockAsync(1, batch: "GOOD", expiry: InFuture);
        await OpenShiftAsync(0m);

        var tooMany = await Sales(s => s.CreateAsync(Sale([Strips(2, 20m)], paid: 40m)));
        var enough = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], paid: 20m)));

        ShouldFailWith(tooMany, InventoryErrors.InsufficientStock);
        enough.IsSuccess.ShouldBeTrue(enough.Error?.Code);
        (await BatchesAsync()).Select(b => b.QuantityAvailable).ShouldBe([5, 0]);
    }

    [Fact]
    public async Task NotEnoughStock_IsRejectedAndChangesNothing()
    {
        await StockAsync(2);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(3, 20m)], paid: 60m)));

        ShouldFailWith(result, InventoryErrors.InsufficientStock);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(2);
        (await CashMovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoLinesOfTheSameProduct_TakeStockInTurn()
    {
        await StockAsync(5);
        await OpenShiftAsync(0m);

        await SellAsync([Strips(2, 20m), Strips(3, 20m)], paid: 100m);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(0);
        (await InvoicesOfSalesAsync()).Single().Lines.Count.ShouldBe(2);
    }

    [Fact]
    public async Task TwoLinesAskingForMoreThanIsThere_AreRejected()
    {
        await StockAsync(4);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(2, 20m), Strips(3, 20m)], paid: 100m)));

        ShouldFailWith(result, InventoryErrors.InsufficientStock);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task Discount_IsSpreadOverTheLinesAndAddsUpExactly()
    {
        await StockAsync(3);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 10m), Strips(1, 10m), Strips(1, 10m)], paid: 29m, discountAmount: 1m)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var invoice = (await InvoicesOfSalesAsync()).Last();
        invoice.DiscountAmount.ShouldBe(1m);
        // A third of a pound does not divide into cents: the odd cent goes to the first of the equal lines.
        invoice.Lines.Select(l => l.DiscountShare).ShouldBe([0.34m, 0.33m, 0.33m]);
        invoice.Lines.Sum(l => l.DiscountShare).ShouldBe(1m);
        invoice.Lines.Sum(l => l.NetTotal).ShouldBe(invoice.Total);
        invoice.Total.ShouldBe(29m);
    }

    [Fact]
    public async Task DiscountByPercent_IsStoredAsAnAmount()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 95m, discountPercent: 5m)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var invoice = (await InvoicesOfSalesAsync()).Single();
        invoice.DiscountAmount.ShouldBe(5m);
        invoice.DiscountPercent.ShouldBe(5m);
    }

    [Fact]
    public async Task ADiscountAboveTheLimit_NeedsASupervisor()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        var alone = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m)));

        ShouldFailWith(alone, SalesErrors.HighDiscountNeedsApproval);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
    }

    [Fact]
    public async Task ASupervisorWithThePermission_ApprovesAHighDiscount()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var pharmacist = await CreateSupervisorAsync("pharm1", SystemRoles.Pharmacist);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m, supervisor: pharmacist)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await InvoicesOfSalesAsync()).Single().DiscountApprovedBy.ShouldBe("pharm1");
        (await AuditActionsAsync()).ShouldContain(AuditActions.SaleHighDiscount);
    }

    [Fact]
    public async Task ASupervisorWithoutThePermission_IsNotAccepted()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var cashier = await CreateSupervisorAsync("till1", SystemRoles.Cashier);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m, supervisor: cashier)));

        ShouldFailWith(result, SalesErrors.SupervisorNotAccepted);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongSupervisorPassword_IsNotAccepted_AndCountsTowardTheirLockout()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var pharmacist = await CreateSupervisorAsync("pharm1", SystemRoles.Pharmacist);
        var wrong = pharmacist with { Password = "Wrong1234" };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            ShouldFailWith(await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m, supervisor: wrong))), SalesErrors.SupervisorNotAccepted);
        }

        // Five wrong passwords locked the account, so even the right password is refused now.
        var locked = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m, supervisor: pharmacist)));
        ShouldFailWith(locked, SalesErrors.SupervisorNotAccepted);
    }

    [Fact]
    public async Task AnUnknownSupervisor_IsNotAccepted()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale(
            [Strips(10, 10m)], paid: 80m, discountPercent: 20m, supervisor: new SupervisorCredentials("nobody", Password))));

        ShouldFailWith(result, SalesErrors.SupervisorNotAccepted);
    }

    [Fact]
    public async Task AManagerWithThePermissions_NeedsNoSupervisor()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        SignIn(Manager);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await InvoicesOfSalesAsync()).Single().DiscountApprovedBy.ShouldBeNull();
    }

    [Fact]
    public async Task TheDiscountLimit_FollowsTheSetting()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var settings = await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Settings.PharmacySettingsService>().GetForEditAsync());
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Settings.PharmacySettingsService>()
            .UpdateAsync(settings.Value.Values with { MaxDiscountPercent = 25m }, settings.Value.RowVersion));

        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 10m)], paid: 80m, discountPercent: 20m)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
    }

    [Fact]
    public async Task SellingBelowCost_NeedsAReason()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(0m);
        SignIn(Manager);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 8m)], paid: 8m)));

        ShouldFailWith(result, SalesErrors.BelowCostReasonRequired);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SellingBelowCost_NeedsASupervisorWhenTheSellerMayNot()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(0m);

        var alone = await Sales(s => s.CreateAsync(Sale([Strips(1, 8m)], paid: 8m, belowCostReason: "near expiry clearance")));

        ShouldFailWith(alone, SalesErrors.BelowCostNeedsApproval);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
    }

    [Fact]
    public async Task ASupervisor_ApprovesASaleBelowCost_AndItIsRecorded()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(0m);
        var pharmacist = await CreateSupervisorAsync("pharm1", SystemRoles.Pharmacist);

        var result = await Sales(s => s.CreateAsync(Sale(
            [Strips(1, 8m)], paid: 8m, belowCostReason: "near expiry clearance", supervisor: pharmacist)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var invoice = (await InvoicesOfSalesAsync()).Single();
        invoice.BelowCostApprovedBy.ShouldBe("pharm1");
        invoice.BelowCostReason.ShouldBe("near expiry clearance");
        invoice.Lines.Single().IsBelowCost.ShouldBeTrue();
        (await AuditActionsAsync()).ShouldContain(AuditActions.SaleBelowCost);
    }

    [Fact]
    public async Task AManagerSellingBelowCost_NeedsOnlyAReason()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(0m);
        SignIn(Manager);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 8m)], paid: 8m, belowCostReason: "damaged box")));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var invoice = (await InvoicesOfSalesAsync()).Single();
        invoice.BelowCostApprovedBy.ShouldBeNull();
        invoice.BelowCostReason.ShouldBe("damaged box");
    }

    [Fact]
    public async Task ADiscountThatPushesALineBelowCost_CountsAsBelowCost()
    {
        await StockAsync(10, costPerStrip: 10m);
        await OpenShiftAsync(0m);
        SignIn(Manager);

        // 10 at price 11 is 110 gross; a 10% discount makes it 99, below the cost of 100.
        var result = await Sales(s => s.CreateAsync(Sale([Strips(10, 11m)], paid: 99m, discountPercent: 10m, belowCostReason: "promotion")));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await InvoicesOfSalesAsync()).Single().Lines.Single().IsBelowCost.ShouldBeTrue();
    }

    [Fact]
    public async Task CreditSale_AddsToWhatTheCustomerOwes_AndNeedsNoShift()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();

        var result = await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 0m)));

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        (await CustomerBalanceAsync(customer)).ShouldBe(80m);
        var entry = (await CustomerEntriesAsync(customer)).Single();
        entry.Type.ShouldBe(CustomerEntryType.Sale);
        entry.ReferenceId.ShouldBe(result.Value);
        (await CashMovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task MixedSale_SplitsBetweenTheDrawerAndTheCustomer()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await OpenShiftAsync(100m);

        await Sales(s => s.CreateAsync(Sale([Strips(4, 20m)], customerId: customer, paid: 30m)));

        (await DrawerAsync()).ShouldBe(130m);
        (await CustomerBalanceAsync(customer)).ShouldBe(50m);
        (await InvoicesOfSalesAsync()).Single().OnCredit.ShouldBe(50m);
    }

    [Fact]
    public async Task PayingCashForANewSale_DoesNotLowerAnOlderDebt()
    {
        await StockAsync(10);
        var customer = await CreateCustomerAsync();
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Receivables.ReceivablesService>()
            .SetOpeningBalanceAsync(new Pharmacy.Application.Receivables.CustomerOpeningBalanceRequest(Guid.NewGuid(), customer, 500m, null)));
        await OpenShiftAsync(0m);

        await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], customerId: customer, paid: 20m)));

        (await CustomerBalanceAsync(customer)).ShouldBe(500m);
    }

    [Fact]
    public async Task CreditForTheDefaultCashCustomer_IsRejected()
    {
        await StockAsync(10);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], customerId: DefaultCustomerId, paid: 0m)));

        ShouldFailWith(result, SalesErrors.CreditNeedsRegisteredCustomer);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CashSale_WithoutAnOpenShift_IsRolledBackCompletely()
    {
        await StockAsync(10);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(2, 20m)], paid: 40m)));

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await InvoicesOfSalesAsync()).ShouldBeEmpty();
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
        (await MovementsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task PaidCashAboveTheTotal_IsRejected()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], paid: 20.01m)));

        ShouldFailWith(result, SalesErrors.InvalidPaidAmount);
    }

    [Fact]
    public async Task ADiscountEqualToTheSubtotal_IsRejected()
    {
        await StockAsync(10);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], discountAmount: 20m)));

        ShouldFailWith(result, SalesErrors.DiscountTooLarge);
    }

    [Fact]
    public async Task RepeatedSubmission_PostsTheSaleOnce()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var request = Sale([Strips(2, 20m)], paid: 40m);

        var first = await Sales(s => s.CreateAsync(request));
        var second = await Sales(s => s.CreateAsync(request));

        second.Value.ShouldBe(first.Value);
        (await InvoicesOfSalesAsync()).Count.ShouldBe(1);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(8);
        (await CashMovementsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task InvoiceNumbers_AreSequential()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);

        await SellAsync([Strips(1, 20m)], 20m);
        await SellAsync([Strips(1, 20m)], 20m);

        (await InvoicesOfSalesAsync()).Select(i => i.Number).ShouldBe(
            [$"SI-{DateTime.Now.Year}-000001", $"SI-{DateTime.Now.Year}-000002"]);
    }

    [Fact]
    public async Task InactiveProductsAndCustomers_CannotBeSold()
    {
        await StockAsync(10);
        await OpenShiftAsync(0m);
        var customer = await CreateCustomerAsync();
        var loaded = (await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Receivables.CustomerQueryService>().GetAsync(customer))).Value;
        await Services.InScopeAsync(s => s.GetRequiredService<Pharmacy.Application.Receivables.CustomerService>()
            .UpdateAsync(customer, new Pharmacy.Application.Receivables.CustomerInput(loaded.Name, null, null, null, false), loaded.RowVersion));

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)], customerId: customer, paid: 20m)));

        ShouldFailWith(result, SalesErrors.InactiveCustomer);
    }

    [Fact]
    public async Task APriceOfZero_IsRejected()
    {
        await StockAsync(10);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 0m)])));

        ShouldFailWith(result, SalesErrors.InvalidPrice);
    }

    [Fact]
    public async Task Selling_WithoutPermission_IsForbidden()
    {
        await StockAsync(10);
        SignIn(Permissions.SalesView);

        var result = await Sales(s => s.CreateAsync(Sale([Strips(1, 20m)])));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }
}
