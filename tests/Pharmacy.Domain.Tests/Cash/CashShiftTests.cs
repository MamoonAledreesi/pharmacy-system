using Pharmacy.Domain.Cash;

namespace Pharmacy.Domain.Tests.Cash;

public class CashShiftTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly CashContext _context = new(_now, UserId: 1, "cashier", Note: "reason");

    private static CashShift NewShift(decimal opening = 100m) =>
        CashShift.Open(_now, 1, "cashier", opening, previousCountedBalance: opening, note: null, operationId: null).Value;

    [Fact]
    public void Open_StartsWithTheOpeningBalanceAsTheExpectedCash()
    {
        var shift = NewShift(250m);

        shift.IsOpen.ShouldBeTrue();
        shift.CurrentBalance.ShouldBe(250m);
    }

    [Fact]
    public void Open_WithABalanceDifferentFromTheLastCount_NeedsANote()
    {
        var withoutNote = CashShift.Open(_now, 1, "cashier", 90m, 100m, note: "  ", operationId: null);
        var withNote = CashShift.Open(_now, 1, "cashier", 90m, 100m, note: "float reduced", operationId: null);

        withoutNote.Error!.Code.ShouldBe(CashErrors.OpeningDifferenceNoteRequired);
        withNote.IsSuccess.ShouldBeTrue();
        withNote.Value.OpeningNote.ShouldBe("float reduced");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.005)]
    [InlineData(2_000_000_000)]
    public void Open_RejectsInvalidOpeningBalances(double value)
    {
        var result = CashShift.Open(_now, 1, "cashier", (decimal)value, (decimal)value, null, null);

        result.Error!.Code.ShouldBe(CashErrors.InvalidAmount);
    }

    [Fact]
    public void ReceiveAndPay_MoveTheBalanceAndRecordTheRunningBalance()
    {
        var shift = NewShift(100m);

        var received = shift.Receive(CashMovementType.ManualIn, 50m, _context).Value;
        var paid = shift.Pay(CashMovementType.ManualOut, 30m, _context).Value;

        received.Amount.ShouldBe(50m);
        received.BalanceAfter.ShouldBe(150m);
        paid.Amount.ShouldBe(-30m);
        paid.BalanceAfter.ShouldBe(120m);
        shift.CurrentBalance.ShouldBe(120m);
    }

    [Fact]
    public void Pay_MoreThanTheDrawerHolds_IsRejectedAndChangesNothing()
    {
        var shift = NewShift(100m);

        var result = shift.Pay(CashMovementType.ManualOut, 100.01m, _context);

        result.Error!.Code.ShouldBe(CashErrors.InsufficientCash);
        shift.CurrentBalance.ShouldBe(100m);
    }

    [Fact]
    public void Pay_TheWholeDrawer_LeavesZero()
    {
        var shift = NewShift(100m);

        shift.Pay(CashMovementType.ManualOut, 100m, _context).IsSuccess.ShouldBeTrue();

        shift.CurrentBalance.ShouldBe(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    public void Receive_RejectsZeroNegativeAndSubCentAmounts(double amount)
    {
        var result = NewShift().Receive(CashMovementType.ManualIn, (decimal)amount, _context);

        result.Error!.Code.ShouldBe(CashErrors.InvalidAmount);
    }

    [Fact]
    public void Receive_WithAPaymentType_IsAProgrammingError() =>
        Should.Throw<ArgumentOutOfRangeException>(() => NewShift().Receive(CashMovementType.ManualOut, 1m, _context));

    [Fact]
    public void Close_RecordsTheCountAndTheDifference()
    {
        var shift = NewShift(100m);
        shift.Receive(CashMovementType.ManualIn, 20m, _context);

        var result = shift.Close(_now.AddHours(8), 1, "cashier", countedBalance: 118m, note: "two notes torn", forced: false, operationId: null);

        result.IsSuccess.ShouldBeTrue();
        shift.IsOpen.ShouldBeFalse();
        shift.CountedBalance.ShouldBe(118m);
        shift.Difference.ShouldBe(-2m);
        shift.WasForceClosed.ShouldBeFalse();
    }

    [Fact]
    public void Close_WithADifferenceAndNoNote_IsRejected()
    {
        var shift = NewShift(100m);

        var result = shift.Close(_now, 1, "cashier", 99m, note: null, forced: false, operationId: null);

        result.Error!.Code.ShouldBe(CashErrors.CloseDifferenceNoteRequired);
        shift.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void Close_ExactCountNeedsNoNote()
    {
        var shift = NewShift(100m);

        shift.Close(_now, 1, "cashier", 100m, note: null, forced: false, operationId: null).IsSuccess.ShouldBeTrue();

        shift.Difference.ShouldBe(0m);
    }

    [Fact]
    public void ForcedClose_AlwaysNeedsAReason()
    {
        var shift = NewShift(100m);

        var withoutReason = shift.Close(_now, 2, "manager", 100m, note: null, forced: true, operationId: null);
        var withReason = shift.Close(_now, 2, "manager", 100m, note: "cashier left", forced: true, operationId: null);

        withoutReason.Error!.Code.ShouldBe(CashErrors.ForceCloseReasonRequired);
        withReason.IsSuccess.ShouldBeTrue();
        shift.WasForceClosed.ShouldBeTrue();
    }

    [Fact]
    public void ClosedShift_RejectsFurtherMovementsAndAnotherClose()
    {
        var shift = NewShift(100m);
        shift.Close(_now, 1, "cashier", 100m, null, false, null);

        shift.Receive(CashMovementType.ManualIn, 5m, _context).Error!.Code.ShouldBe(CashErrors.ShiftClosed);
        shift.Close(_now, 1, "cashier", 100m, null, false, null).Error!.Code.ShouldBe(CashErrors.ShiftClosed);
    }
}
