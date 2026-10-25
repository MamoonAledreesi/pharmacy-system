using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Sales;

namespace Pharmacy.Domain.Tests.Sales;

public class ReturnPricingTests
{
    [Fact]
    public void AReturnOfPartOfALine_IsWorthItsShareOfTheNetPrice() =>
        ReturnPricing.LineValue(27m, 3, 0, 0m, 1).ShouldBe(9m);

    [Fact]
    public void ReturningWhatIsLeftOfALine_GivesExactlyWhatIsLeftOfItsValue()
    {
        var first = ReturnPricing.LineValue(29.9m, 3, 0, 0m, 1);
        var second = ReturnPricing.LineValue(29.9m, 3, 1, first, 1);
        var third = ReturnPricing.LineValue(29.9m, 3, 2, first + second, 1);

        new[] { first, second, third }.ShouldBe([9.97m, 9.97m, 9.96m]);
        (first + second + third).ShouldBe(29.9m);
    }

    [Fact]
    public void ReturningTheWholeLineAtOnce_IsTheWholeNetPrice() =>
        ReturnPricing.LineValue(29.9m, 3, 0, 0m, 3).ShouldBe(29.9m);

    [Theory]
    [InlineData(80, 50, 50, 50, 30)]    // mixed sale, returned in full: the credit part off the debt, the rest in cash
    [InlineData(40, 80, 80, 40, 0)]     // credit sale: all of it comes off the debt
    [InlineData(40, 80, 0, 0, 40)]      // the customer already paid everything: cash
    [InlineData(80, 80, 20, 20, 60)]    // the customer owes less than the invoice credit now
    [InlineData(20, 10, 100, 10, 10)]   // earlier returns used up most of the invoice credit
    [InlineData(20, 0, 100, 0, 20)]     // nothing of the credit part is left: all cash
    public void TheRefund_TakesTheDebtFirst_ButNeverMoreThanTheCreditLeftOrTheBalance(
        double amount, double creditLeft, double balance, double debt, double cash)
    {
        var split = ReturnPricing.Split((decimal)amount, (decimal)creditLeft, (decimal)balance);

        split.DebtReduction.ShouldBe((decimal)debt);
        split.CashRefund.ShouldBe((decimal)cash);
        (split.DebtReduction + split.CashRefund).ShouldBe((decimal)amount);
    }

    [Fact]
    public void ANegativeCreditLeft_IsTreatedAsNothing()
    {
        var split = ReturnPricing.Split(20m, -5m, 100m);

        split.DebtReduction.ShouldBe(0m);
        split.CashRefund.ShouldBe(20m);
    }
}

public class SalesReturnTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

    private static NewReturnLine Line(decimal value, int quantity = 1) =>
        new(1, 1, 1, 1, quantity, quantity, value, [new NewReturnAllocation(1, 1, quantity, 5m, Quarantined: false)]);

    private static Pharmacy.Domain.Common.Result<SalesReturn> Create(
        decimal debt, decimal cash, string? reason = null, bool late = false, params NewReturnLine[] lines) =>
        SalesReturn.Create(
            "SR-2026-000001", 1, 1, lines.Length == 0 ? [Line(30m)] : lines, new RefundSplit(debt, cash), reason, late,
            Guid.NewGuid(), _now, 1, "cashier");

    [Fact]
    public void TheTotal_IsTheSumOfTheLines_AndEqualsTheSettlement()
    {
        var salesReturn = Create(debt: 10m, cash: 35m, lines: [Line(20m), Line(25m)]).Value;

        salesReturn.Total.ShouldBe(45m);
        salesReturn.DebtReduction.ShouldBe(10m);
        salesReturn.CashRefund.ShouldBe(35m);
        salesReturn.Lines.Count.ShouldBe(2);
        salesReturn.Lines.First().Allocations.Single().UnitCost.ShouldBe(5m);
    }

    [Fact]
    public void ASettlementThatDoesNotAddUp_IsRejected() =>
        Create(debt: 10m, cash: 10m).Error!.Code.ShouldBe(SalesErrors.InvalidReturnAmount);

    [Fact]
    public void AReturnWithoutLines_IsRejected() =>
        SalesReturn.Create("N", 1, 1, [], new RefundSplit(0m, 0m), null, false, Guid.NewGuid(), _now, 1, "u")
            .Error!.Code.ShouldBe(SalesErrors.ReturnNoLines);

    [Fact]
    public void ALateReturn_NeedsAReason()
    {
        Create(0m, 30m, reason: " ", late: true).Error!.Code.ShouldBe(SalesErrors.LateReturnReasonRequired);

        var withReason = Create(0m, 30m, reason: " customer was ill ", late: true).Value;

        withReason.IsLate.ShouldBeTrue();
        withReason.Reason.ShouldBe("customer was ill");
    }

    [Fact]
    public void AnOrdinaryReturn_NeedsNoReason() =>
        Create(0m, 30m).Value.Reason.ShouldBeNull();
}

public class ReturnFromCustomerTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MovementContext _context = new(_now, 1, "cashier");

    private static Batch SoldBatch()
    {
        var batch = Batch.Create(1, "B1", new DateOnly(2028, 1, 31), 5m, _now).Value;
        batch.Receive(StockMovementType.OpeningStock, 10, _context);
        batch.Deduct(StockMovementType.Sale, 4, _context);
        return batch;
    }

    [Fact]
    public void SellableGoods_GoBackOnTheShelf()
    {
        var batch = SoldBatch();

        var movement = batch.ReturnFromCustomer(3, toQuarantine: false, _context).Value;

        batch.QuantityAvailable.ShouldBe(9);
        batch.QuantityQuarantined.ShouldBe(0);
        movement.Type.ShouldBe(StockMovementType.SaleReturn);
        movement.AvailableDelta.ShouldBe(3);
    }

    [Fact]
    public void DamagedGoods_GoToQuarantine()
    {
        var batch = SoldBatch();

        var movement = batch.ReturnFromCustomer(3, toQuarantine: true, _context).Value;

        batch.QuantityAvailable.ShouldBe(6);
        batch.QuantityQuarantined.ShouldBe(3);
        movement.Type.ShouldBe(StockMovementType.SaleReturnQuarantined);
        movement.QuarantineDelta.ShouldBe(3);
    }

    [Fact]
    public void AReturnOfNothing_IsRejected() =>
        SoldBatch().ReturnFromCustomer(0, toQuarantine: false, _context).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
}

public class ReturnWindowSettingTests
{
    [Fact]
    public void TheDefaultPeriod_IsFourteenDays() =>
        Pharmacy.Domain.Settings.PharmacySettings.CreateDefault().ReturnWindowDays.ShouldBe(14);

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(365)]
    public void AValidPeriod_IsStored(int days)
    {
        var settings = Pharmacy.Domain.Settings.PharmacySettings.CreateDefault();

        settings.Update(Pharmacy.Domain.Settings.PharmacySettings.Defaults with { ReturnWindowDays = days }).IsSuccess.ShouldBeTrue();

        settings.ReturnWindowDays.ShouldBe(days);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void AnInvalidPeriod_IsRejected(int days)
    {
        var settings = Pharmacy.Domain.Settings.PharmacySettings.CreateDefault();

        settings.Update(Pharmacy.Domain.Settings.PharmacySettings.Defaults with { ReturnWindowDays = days })
            .Error!.Code.ShouldBe(Pharmacy.Domain.Settings.SettingsErrors.InvalidReturnWindowDays);
        settings.ReturnWindowDays.ShouldBe(14);
    }
}
