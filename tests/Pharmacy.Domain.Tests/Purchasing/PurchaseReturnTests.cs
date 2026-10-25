using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.Domain.Tests.Purchasing;

public class PurchaseReturnTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);

    private static NewPurchaseReturnLine Line(decimal value, int quantity = 1, bool quarantine = false) =>
        new(1, 1, 1, 1, quantity, quantity, 1, quarantine, value);

    private static Result<PurchaseReturn> Create(
        decimal debt, decimal cash, string? reason = "damaged", int? supplierId = 5, params NewPurchaseReturnLine[] lines) =>
        PurchaseReturn.Create(
            "PR-2026-000001", 1, supplierId, lines.Length == 0 ? [Line(30m)] : lines, new RefundSplit(debt, cash), reason,
            Guid.NewGuid(), _now, 1, "keeper");

    [Fact]
    public void TheTotal_IsTheSumOfTheLines_AndEqualsTheSettlement()
    {
        var purchaseReturn = Create(debt: 10m, cash: 35m, lines: [Line(20m), Line(25m, quarantine: true)]).Value;

        purchaseReturn.Total.ShouldBe(45m);
        purchaseReturn.DebtReduction.ShouldBe(10m);
        purchaseReturn.CashRefund.ShouldBe(35m);
        purchaseReturn.Lines.Select(l => l.FromQuarantine).ShouldBe([false, true]);
    }

    [Fact]
    public void ASettlementThatDoesNotAddUp_IsRejected() =>
        Create(debt: 10m, cash: 10m).Error!.Code.ShouldBe(PurchasingErrors.InvalidReturnAmount);

    [Fact]
    public void WhatIsOwed_CanOnlyComeDownForARegisteredSupplier() =>
        Create(debt: 30m, cash: 0m, supplierId: null).Error!.Code.ShouldBe(PurchasingErrors.InvalidReturnAmount);

    [Fact]
    public void AnUnregisteredSeller_RefundsEverythingInCash() =>
        Create(debt: 0m, cash: 30m, supplierId: null).IsSuccess.ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void TheReason_IsRequired(string? reason) =>
        Create(0m, 30m, reason).Error!.Code.ShouldBe(PurchasingErrors.ReturnReasonRequired);

    [Fact]
    public void AReturnWithoutLines_IsRejected() =>
        PurchaseReturn.Create("N", 1, 1, [], new RefundSplit(0m, 0m), "x", Guid.NewGuid(), _now, 1, "u")
            .Error!.Code.ShouldBe(PurchasingErrors.ReturnNoLines);
}

public class ReturnToSupplierTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MovementContext _context = new(_now, 1, "keeper");

    private static Batch HeldBatch()
    {
        var batch = Batch.Create(1, "B1", new DateOnly(2028, 1, 31), 5m, _now).Value;
        batch.Receive(StockMovementType.Purchase, 10, _context);
        batch.Quarantine(4, _context);
        return batch;
    }

    [Fact]
    public void GoodsGoBackFromTheShelf()
    {
        var batch = HeldBatch();

        var movement = batch.ReturnToSupplier(5, fromQuarantine: false, _context).Value;

        batch.QuantityAvailable.ShouldBe(1);
        batch.QuantityQuarantined.ShouldBe(4);
        movement.Type.ShouldBe(StockMovementType.PurchaseReturn);
        movement.AvailableDelta.ShouldBe(-5);
    }

    [Fact]
    public void GoodsGoBackFromQuarantine()
    {
        var batch = HeldBatch();

        var movement = batch.ReturnToSupplier(4, fromQuarantine: true, _context).Value;

        batch.QuantityAvailable.ShouldBe(6);
        batch.QuantityQuarantined.ShouldBe(0);
        movement.Type.ShouldBe(StockMovementType.PurchaseReturnQuarantined);
        movement.QuarantineDelta.ShouldBe(-4);
    }

    [Fact]
    public void MoreThanTheBatchHolds_IsRejectedAndChangesNothing()
    {
        var batch = HeldBatch();

        batch.ReturnToSupplier(7, fromQuarantine: false, _context).Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        batch.ReturnToSupplier(5, fromQuarantine: true, _context).Error!.Code.ShouldBe(InventoryErrors.InsufficientQuarantined);

        batch.QuantityAvailable.ShouldBe(6);
        batch.QuantityQuarantined.ShouldBe(4);
    }
}

public class SupplierPurchaseReturnTests
{
    private static readonly AccountContext _context = new(new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc), 1, "keeper");

    [Fact]
    public void APurchaseReturn_TakesWhatIsOwedDown_AndNeverBelowZero()
    {
        var supplier = Supplier.Create("Al-Noor", null, null, null).Value;
        supplier.Increase(SupplierEntryType.Invoice, 100m, _context);

        var entry = supplier.Decrease(SupplierEntryType.PurchaseReturn, 40m, _context).Value;
        var tooMuch = supplier.Decrease(SupplierEntryType.PurchaseReturn, 60.01m, _context);

        entry.Amount.ShouldBe(-40m);
        entry.BalanceAfter.ShouldBe(60m);
        tooMuch.Error!.Code.ShouldBe(PurchasingErrors.PaymentExceedsBalance);
        supplier.CurrentBalance.ShouldBe(60m);
    }
}
