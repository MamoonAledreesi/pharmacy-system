using Pharmacy.Domain.Inventory;

namespace Pharmacy.Domain.Tests.Inventory;

public class BatchTests
{
    private static readonly DateTime _now = new(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MovementContext _context = new(_now, UserId: 1, "pharmacist", ReasonCode: StockReasons.Damaged);

    private static Batch NewBatch(decimal cost = 10m) =>
        Batch.Create(1, "B-100", new DateOnly(2027, 5, 31), cost, _now).Value;

    [Fact]
    public void Create_TrimsTheNumberAndStartsEmpty()
    {
        var batch = Batch.Create(1, "  B-100  ", null, 2.5m, _now).Value;

        batch.BatchNumber.ShouldBe("B-100");
        batch.QuantityAvailable.ShouldBe(0);
        batch.QuantityQuarantined.ShouldBe(0);
        batch.ExpiryDate.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RequiresABatchNumber(string number)
    {
        Batch.Create(1, number, null, 1m, _now).Error!.Code.ShouldBe(InventoryErrors.BatchNumberRequired);
    }

    [Fact]
    public void Create_RejectsInvalidValues()
    {
        Batch.Create(1, new string('x', Batch.BatchNumberMaxLength + 1), null, 1m, _now).Error!.Code.ShouldBe(InventoryErrors.BatchNumberTooLong);
        Batch.Create(1, "B", null, -0.01m, _now).Error!.Code.ShouldBe(InventoryErrors.InvalidUnitCost);
        Batch.Create(1, "B", null, 1.00001m, _now).Error!.Code.ShouldBe(InventoryErrors.InvalidUnitCost);
        Batch.Create(1, "B", null, Batch.MaxUnitCost + 1, _now).Error!.Code.ShouldBe(InventoryErrors.InvalidUnitCost);
        Batch.Create(1, "B", new DateOnly(1990, 1, 1), 1m, _now).Error!.Code.ShouldBe(InventoryErrors.ExpiryOutOfRange);
    }

    [Theory]
    [InlineData(33, 3, 11)]
    [InlineData(10, 3, 3.3333)]
    [InlineData(20, 3, 6.6667)]
    [InlineData(5, 1, 5)]
    public void UnitCostFromPackCost_RoundsToFourDecimals(double packCost, int factor, double expected)
    {
        Batch.UnitCostFromPackCost((decimal)packCost, factor).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Receive_AddsStock_AndWritesALedgerEntryWithTheBalancesAfter()
    {
        var batch = NewBatch(cost: 12.5m);

        var movement = batch.Receive(StockMovementType.OpeningStock, 30, _context).Value;

        batch.QuantityAvailable.ShouldBe(30);
        movement.AvailableDelta.ShouldBe(30);
        movement.QuarantineDelta.ShouldBe(0);
        movement.AvailableAfter.ShouldBe(30);
        movement.UnitCost.ShouldBe(12.5m);
        movement.UserName.ShouldBe("pharmacist");
        movement.OccurredAtUtc.ShouldBe(_now);
    }

    [Fact]
    public void Deduct_CannotTakeMoreThanIsAvailable_AndChangesNothingWhenItFails()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 5, _context);

        var result = batch.Deduct(StockMovementType.Sale, 6, _context);

        result.Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        result.Error.Arguments.ShouldBe([5]);
        batch.QuantityAvailable.ShouldBe(5);
    }

    [Fact]
    public void Deduct_TheWholeQuantity_LeavesZero()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 5, _context);

        batch.Deduct(StockMovementType.Sale, 5, _context).IsSuccess.ShouldBeTrue();

        batch.QuantityAvailable.ShouldBe(0);
    }

    [Fact]
    public void Quarantine_MovesStockBetweenTheTwoBalances_SoTheTotalIsUnchanged()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 10, _context);

        var movement = batch.Quarantine(4, _context).Value;

        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((6, 4));
        (movement.AvailableDelta, movement.QuarantineDelta).ShouldBe((-4, 4));
        (movement.AvailableAfter, movement.QuarantineAfter).ShouldBe((6, 4));
    }

    [Fact]
    public void Release_ReturnsQuarantinedStockToSale()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 10, _context);
        batch.Quarantine(4, _context);

        batch.Release(3, _context).IsSuccess.ShouldBeTrue();

        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((9, 1));
    }

    [Fact]
    public void CannotQuarantineMoreThanAvailable_NorReleaseMoreThanQuarantined()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 10, _context);
        batch.Quarantine(4, _context);

        batch.Quarantine(7, _context).Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        batch.Release(5, _context).Error!.Code.ShouldBe(InventoryErrors.InsufficientQuarantined);
        batch.WriteOffQuarantined(5, _context).Error!.Code.ShouldBe(InventoryErrors.InsufficientQuarantined);
        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((6, 4));
    }

    [Fact]
    public void WriteOffQuarantined_RemovesOnlyFromTheQuarantineBalance()
    {
        var batch = NewBatch();
        batch.Receive(StockMovementType.OpeningStock, 10, _context);
        batch.Quarantine(4, _context);

        var movement = batch.WriteOffQuarantined(4, _context).Value;

        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((6, 0));
        (movement.AvailableDelta, movement.QuarantineDelta).ShouldBe((0, -4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RejectsNonPositiveQuantities(int quantity)
    {
        var batch = NewBatch();

        batch.Receive(StockMovementType.OpeningStock, quantity, _context).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
        batch.Deduct(StockMovementType.Sale, quantity, _context).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
        batch.Quarantine(quantity, _context).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
    }

    [Fact]
    public void RejectsQuantitiesThatCouldOverflowTheBalance()
    {
        var batch = NewBatch();

        batch.Receive(StockMovementType.OpeningStock, Batch.MaxOperationQuantity + 1, _context).Error!.Code
            .ShouldBe(InventoryErrors.QuantityTooLarge);

        // 21 receipts of 100 million fit in an int (2.1 billion); the 22nd is refused instead of wrapping around.
        for (var i = 0; i < 21; i++)
        {
            batch.Receive(StockMovementType.OpeningStock, Batch.MaxOperationQuantity, _context).IsSuccess.ShouldBeTrue();
        }

        batch.Receive(StockMovementType.OpeningStock, Batch.MaxOperationQuantity, _context).Error!.Code
            .ShouldBe(InventoryErrors.QuantityTooLarge);
        batch.QuantityAvailable.ShouldBe(2_100_000_000);
    }

    [Fact]
    public void WrongMovementTypes_AreProgrammingErrors()
    {
        var batch = NewBatch();

        Should.Throw<ArgumentOutOfRangeException>(() => batch.Receive(StockMovementType.Sale, 1, _context));
        Should.Throw<ArgumentOutOfRangeException>(() => batch.Deduct(StockMovementType.OpeningStock, 1, _context));
    }

    [Fact]
    public void TheBalancesAlwaysEqualTheSumOfTheirMovements()
    {
        var batch = NewBatch();
        var movements = new List<StockMovement>();
        var random = new Random(1234);

        for (var i = 0; i < 500; i++)
        {
            var quantity = random.Next(1, 40);
            var result = random.Next(6) switch
            {
                0 => batch.Receive(StockMovementType.Purchase, quantity, _context),
                1 => batch.Deduct(StockMovementType.Sale, quantity, _context),
                2 => batch.Quarantine(quantity, _context),
                3 => batch.Release(quantity, _context),
                4 => batch.WriteOffQuarantined(quantity, _context),
                _ => batch.Deduct(StockMovementType.WriteOff, quantity, _context),
            };

            if (result.IsSuccess)
            {
                movements.Add(result.Value);
            }

            batch.QuantityAvailable.ShouldBeGreaterThanOrEqualTo(0);
            batch.QuantityQuarantined.ShouldBeGreaterThanOrEqualTo(0);
        }

        movements.Count.ShouldBeGreaterThan(100);
        batch.QuantityAvailable.ShouldBe(movements.Sum(m => m.AvailableDelta));
        batch.QuantityQuarantined.ShouldBe(movements.Sum(m => m.QuarantineDelta));
        movements[^1].AvailableAfter.ShouldBe(batch.QuantityAvailable);
        movements[^1].QuarantineAfter.ShouldBe(batch.QuantityQuarantined);
    }
}
