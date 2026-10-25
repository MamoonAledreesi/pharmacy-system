using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Common;
using Pharmacy.Application.Inventory;
using Pharmacy.Domain.Catalog;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Inventory;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Inventory;

public sealed class InventoryServiceTests : InventoryTestBase
{
    private static readonly DateOnly _expiry = new(2028, 5, 31);

    [Fact]
    public async Task Migration_SeedsTheInventoryPermissionsAndNearExpirySetting()
    {
        var grants = await Services.InScopeAsync(s =>
        {
            var db = s.GetRequiredService<PharmacyDbContext>();
            return db.RoleClaims
                .Where(c => c.ClaimValue!.StartsWith("Inventory."))
                .Join(db.Roles, c => c.RoleId, r => r.Id, (c, r) => r.Name + ":" + c.ClaimValue)
                .ToListAsync();
        });

        grants.Where(g => g.EndsWith(":Inventory.View")).Count().ShouldBe(4);
        grants.ShouldContain("StoreKeeper:Inventory.OpeningStock");
        grants.ShouldNotContain(g => g.StartsWith("Cashier:") && g.EndsWith("ViewCost"));
        grants.ShouldContain("Pharmacist:Inventory.ViewCost");
        grants.ShouldContain("Accountant:Inventory.ViewCost");

        var settings = await Services.InScopeAsync(s => s.GetRequiredService<Application.Settings.PharmacySettingsService>().GetCurrentAsync());
        settings.NearExpiryDays.ShouldBe(90);
    }

    [Fact]
    public async Task OpeningStock_InBoxes_IsStoredInBaseUnits_WithTheCostOfOneBaseUnit()
    {
        var result = await OpeningAsync(quantity: 2, cost: 33m, batchNumber: "B-100", _expiry, unitId: BoxId);

        result.IsSuccess.ShouldBeTrue();
        var batch = (await BatchesAsync()).Single();
        (batch.BatchNumber, batch.ExpiryDate, batch.QuantityAvailable, batch.UnitCost).ShouldBe(("B-100", _expiry, 6, 11m));

        var movement = (await MovementsAsync()).Single();
        (movement.Type, movement.AvailableDelta, movement.AvailableAfter, movement.UnitCost, movement.UserName)
            .ShouldBe((StockMovementType.OpeningStock, 6, 6, 11m, "keeper"));
        (await AuditActionsAsync()).ShouldContain(AuditActions.StockOpening);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task ACostThatDoesNotDivideEvenly_IsRoundedToFourDecimals()
    {
        await OpeningAsync(1, cost: 10m, "B", _expiry, unitId: BoxId);

        (await BatchesAsync()).Single().UnitCost.ShouldBe(3.3333m);
    }

    [Fact]
    public async Task ReceivingTheSameBatchAgain_AddsToIt_ButADifferentCostIsASeparateBatch()
    {
        await OpeningAsync(10, 5m, "B-1", _expiry);
        await OpeningAsync(5, 5m, "b-1", _expiry);
        await OpeningAsync(7, 6m, "B-1", _expiry);
        await OpeningAsync(2, 5m, "B-1", _expiry.AddMonths(1));

        var batches = await BatchesAsync();

        batches.Count.ShouldBe(3);
        batches.Select(b => (b.UnitCost, b.ExpiryDate, b.QuantityAvailable))
            .ShouldBe([(5m, (DateOnly?)_expiry, 15), (6m, (DateOnly?)_expiry, 7), (5m, (DateOnly?)_expiry.AddMonths(1), 2)]);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task ABlankBatchNumber_GetsAnAutomaticNumber_AndJoinsAnAutomaticBatchOfTheSameExpiryAndCost()
    {
        await OpeningAsync(4, 5m, null, _expiry);
        await OpeningAsync(6, 5m, "  ", _expiry);
        await OpeningAsync(3, 5m, null, _expiry.AddMonths(2));
        await OpeningAsync(1, 5m, "REAL-9", _expiry);

        var batches = await BatchesAsync();

        batches.Select(b => (b.BatchNumber, b.QuantityAvailable)).ShouldBe([("AUTO-0001", 10), ("AUTO-0002", 3), ("REAL-9", 1)]);
    }

    [Fact]
    public async Task AProductWithoutExpiry_IgnoresTheDateAndNeverNeedsOne()
    {
        var (productId, stripId, _) = await CreateProductAsync("Bandage", tracksExpiry: false);

        (await OpeningAsync(5, 2m, "X", expiry: null, productId, stripId)).IsSuccess.ShouldBeTrue();
        (await OpeningAsync(5, 2m, "X", expiry: _expiry, productId, stripId)).IsSuccess.ShouldBeTrue();

        var batch = (await BatchesAsync(productId)).Single();
        (batch.ExpiryDate, batch.QuantityAvailable).ShouldBe((null, 10));
    }

    [Fact]
    public async Task AProductThatExpires_RequiresAnExpiryDate()
    {
        (await OpeningAsync(5, 2m, "X", expiry: null)).Error!.Code.ShouldBe(InventoryErrors.ExpiryRequired);

        (await BatchesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task OpeningStock_ValidatesItsInput_AndSavesNothingWhenItFails()
    {
        (await OpeningAsync(0, 2m, "X", _expiry)).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
        (await OpeningAsync(-4, 2m, "X", _expiry)).Error!.Code.ShouldBe(InventoryErrors.InvalidQuantity);
        (await OpeningAsync(5, -1m, "X", _expiry)).Error!.Code.ShouldBe(InventoryErrors.InvalidUnitCost);
        (await OpeningAsync(5, 2m, new string('x', 60), _expiry)).Error!.Code.ShouldBe(InventoryErrors.BatchNumberTooLong);
        (await OpeningAsync(5, 2m, "X", new DateOnly(1980, 1, 1))).Error!.Code.ShouldBe(InventoryErrors.ExpiryOutOfRange);
        (await OpeningAsync(5, 2m, "X", _expiry, productId: 999)).Error!.Code.ShouldBe(InventoryErrors.UnknownProduct);
        (await OpeningAsync(5, 2m, "X", _expiry, unitId: 999)).Error!.Code.ShouldBe(InventoryErrors.UnknownUnit);
        (await OpeningAsync(Batch.MaxOperationQuantity, 2m, "X", _expiry, unitId: BoxId)).Error!.Code.ShouldBe(InventoryErrors.QuantityTooLarge);

        (await BatchesAsync()).ShouldBeEmpty();
        (await MovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AUnitOfAnotherProduct_IsRejected()
    {
        var (_, otherStrip, _) = await CreateProductAsync("Other", tracksExpiry: true);

        (await OpeningAsync(5, 2m, "X", _expiry, unitId: otherStrip)).Error!.Code.ShouldBe(InventoryErrors.UnknownUnit);
    }

    [Fact]
    public async Task ADeactivatedProduct_CannotReceiveOpeningStock()
    {
        await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().SetActiveAsync(ProductId, false));

        (await OpeningAsync(5, 2m, "X", _expiry)).Error!.Code.ShouldBe(InventoryErrors.InactiveProduct);
    }

    [Fact]
    public async Task ASubmissionRepeatedWithTheSameOperationId_IsAppliedOnlyOnce()
    {
        var operation = Guid.NewGuid();

        var first = await OpeningAsync(10, 5m, "B-1", _expiry, operationId: operation);
        var second = await OpeningAsync(10, 5m, "B-1", _expiry, operationId: operation);

        second.Value.ShouldBe(first.Value);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(10);
        (await MovementsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AdjustmentsAndQuarantineAndWriteOff_RepeatedWithTheSameOperationId_AreAppliedOnlyOnce()
    {
        var batchId = (await OpeningAsync(100, 5m, "B-1", _expiry)).Value;
        var operation = Guid.NewGuid();
        var request = new StockActionRequest(operation, batchId, StripId, 10, StockReasons.Damaged, null);

        (await Inventory(s => s.QuarantineAsync(request))).IsSuccess.ShouldBeTrue();
        (await Inventory(s => s.QuarantineAsync(request))).IsSuccess.ShouldBeTrue();

        var batch = (await BatchesAsync()).Single();
        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((90, 10));
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task Adjustment_RequiresAReason_AndOtherNeedsANote()
    {
        var batchId = (await OpeningAsync(10, 5m, "B-1", _expiry)).Value;

        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, reason: null)).Error!.Code.ShouldBe(InventoryErrors.ReasonRequired);
        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, "Nonsense")).Error!.Code.ShouldBe(InventoryErrors.UnknownReason);
        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Expired)).Error!.Code.ShouldBe(InventoryErrors.UnknownReason);
        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Other)).Error!.Code.ShouldBe(InventoryErrors.NoteRequired);
        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Other, "recount")).IsSuccess.ShouldBeTrue();
        (await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Lost, new string('n', 400))).Error!.Code.ShouldBe(InventoryErrors.NoteTooLong);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(9);
    }

    [Fact]
    public async Task Adjustment_InBoxes_ChangesTheBatchByWholeBoxesOfBaseUnits_AndNeverBelowZero()
    {
        var batchId = (await OpeningAsync(10, 5m, "B-1", _expiry)).Value;

        (await Adjust(AdjustmentDirection.Decrease, batchId, 3, StockReasons.CountDifference, unitId: BoxId)).IsSuccess.ShouldBeTrue();
        var tooMuch = await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Lost, unitId: BoxId);
        (await Adjust(AdjustmentDirection.Increase, batchId, 2, StockReasons.CountDifference, unitId: BoxId)).IsSuccess.ShouldBeTrue();

        tooMuch.Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        tooMuch.Error.Arguments.ShouldBe([1]);
        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(7);
        (await MovementsAsync()).Select(m => m.AvailableDelta).ShouldBe([10, -9, 6]);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task Quarantine_RemovesStockFromSale_ReleaseReturnsIt_AndWriteOffDestroysIt()
    {
        var batchId = (await OpeningAsync(20, 5m, "B-1", _expiry)).Value;

        (await Quarantine(batchId, 8)).IsSuccess.ShouldBeTrue();
        (await ConsumeAsync(13)).Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        (await ConsumeAsync(12)).IsSuccess.ShouldBeTrue();
        (await Inventory(s => s.ReleaseFromQuarantineAsync(Action(batchId, 3, reason: null)))).IsSuccess.ShouldBeTrue();
        (await Inventory(s => s.WriteOffAsync(fromQuarantine: true, Action(batchId, 5, StockReasons.Damaged)))).IsSuccess.ShouldBeTrue();
        (await Inventory(s => s.WriteOffAsync(fromQuarantine: false, Action(batchId, 3, StockReasons.Lost)))).IsSuccess.ShouldBeTrue();

        var batch = (await BatchesAsync()).Single();
        (batch.QuantityAvailable, batch.QuantityQuarantined).ShouldBe((0, 0));
        (await MovementsAsync()).Select(m => m.Type).ShouldBe(
        [
            StockMovementType.OpeningStock, StockMovementType.QuarantineIn, StockMovementType.Sale,
            StockMovementType.QuarantineOut, StockMovementType.WriteOffQuarantined, StockMovementType.WriteOff,
        ]);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task WriteOffOfAnExpiredBatch_RecordsItsCost_ForTheLossReports()
    {
        var batchId = (await OpeningAsync(6, 4m, "OLD", Today.AddDays(-20))).Value;

        (await Inventory(s => s.WriteOffAsync(false, Action(batchId, 6, StockReasons.Expired)))).IsSuccess.ShouldBeTrue();

        var movement = (await MovementsAsync()).Last();
        (movement.Type, movement.AvailableDelta, movement.UnitCost, movement.ReasonCode).ShouldBe((StockMovementType.WriteOff, -6, 4m, StockReasons.Expired));
        (await AuditActionsAsync()).ShouldContain(AuditActions.StockWrittenOff);
    }

    [Fact]
    public async Task ExpiredStock_IsNeverSold_EvenWhenItIsTheOnlyStockLeft()
    {
        await OpeningAsync(50, 4m, "EXPIRED", Today.AddDays(-1));
        await OpeningAsync(5, 4m, "GOOD", Today.AddDays(200));

        (await ConsumeAsync(6)).Error!.Code.ShouldBe(InventoryErrors.InsufficientStock);
        var sold = (await ConsumeAsync(5)).Value;

        sold.Single().Quantity.ShouldBe(5);
        (await BatchesAsync()).Single(b => b.BatchNumber == "EXPIRED").QuantityAvailable.ShouldBe(50);
    }

    [Fact]
    public async Task Selling_UsesFefo_AndRecordsEachBatchesOwnCost()
    {
        await OpeningAsync(5, 10m, "LATE", Today.AddDays(300));
        await OpeningAsync(4, 8m, "SOON", Today.AddDays(30));

        var allocations = (await ConsumeAsync(6)).Value;

        allocations.Select(a => (a.Quantity, a.UnitCost, a.IsNearExpiry)).ShouldBe([(4, 8m, true), (2, 10m, false)]);
        (await BatchesAsync()).Select(b => (b.BatchNumber, b.QuantityAvailable)).ShouldBe([("LATE", 3), ("SOON", 0)]);
        (await MovementsAsync()).Where(m => m.Type == StockMovementType.Sale).Select(m => (m.AvailableDelta, m.UnitCost)).ShouldBe([(-4, 8m), (-2, 10m)]);
        await AssertLedgerMatchesBatchesAsync();
    }

    [Fact]
    public async Task AFailedSale_ChangesNothing_NotEvenTheBatchesItCouldHaveTaken()
    {
        await OpeningAsync(5, 10m, "A", Today.AddDays(30));
        await OpeningAsync(5, 10m, "B", Today.AddDays(60));

        (await ConsumeAsync(11)).IsFailure.ShouldBeTrue();

        (await BatchesAsync()).Select(b => b.QuantityAvailable).ShouldBe([5, 5]);
        (await MovementsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task UnknownBatch_AndAUnitOfAnotherProduct_AreRejected()
    {
        var batchId = (await OpeningAsync(5, 2m, "B", _expiry)).Value;
        var (_, otherStrip, _) = await CreateProductAsync("Other", tracksExpiry: true);

        (await Adjust(AdjustmentDirection.Increase, 999, 1, StockReasons.Lost)).Error!.Code.ShouldBe(InventoryErrors.UnknownBatch);
        (await Adjust(AdjustmentDirection.Increase, batchId, 1, StockReasons.Lost, unitId: otherStrip)).Error!.Code.ShouldBe(InventoryErrors.UnknownUnit);
    }

    [Fact]
    public async Task EveryOperation_RequiresItsOwnPermission()
    {
        var batchId = (await OpeningAsync(10, 2m, "B", _expiry)).Value;

        SignIn(Permissions.InventoryView, Permissions.CatalogView);
        var results = new Result[]
        {
            await OpeningAsync(1, 2m, "N", _expiry),
            await Adjust(AdjustmentDirection.Increase, batchId, 1, StockReasons.Lost),
            await Quarantine(batchId, 1),
            await Inventory(s => s.ReleaseFromQuarantineAsync(Action(batchId, 1, null))),
            await Inventory(s => s.WriteOffAsync(false, Action(batchId, 1, StockReasons.Lost))),
        };
        results.ShouldAllBe(r => r.Error!.Code == CommonErrors.Forbidden);

        // Adjust is not WriteOff and not OpeningStock: each is its own permission.
        SignIn(Permissions.InventoryAdjust);
        (await Adjust(AdjustmentDirection.Increase, batchId, 1, StockReasons.Lost)).IsSuccess.ShouldBeTrue();
        (await Inventory(s => s.WriteOffAsync(false, Action(batchId, 1, StockReasons.Lost)))).Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await OpeningAsync(1, 2m, "N", _expiry)).Error!.Code.ShouldBe(CommonErrors.Forbidden);

        (await BatchesAsync()).Single().QuantityAvailable.ShouldBe(11);
    }

    [Fact]
    public async Task EveryChange_IsAudited_AndTheLedgerRecordsWhoDidIt()
    {
        var batchId = (await OpeningAsync(20, 2m, "B", _expiry)).Value;
        await Adjust(AdjustmentDirection.Decrease, batchId, 1, StockReasons.Lost);
        await Quarantine(batchId, 2);
        await Inventory(s => s.ReleaseFromQuarantineAsync(Action(batchId, 1, null)));
        await Inventory(s => s.WriteOffAsync(true, Action(batchId, 1, StockReasons.Damaged)));

        (await AuditActionsAsync()).ShouldBe(
        [
            AuditActions.ProductCreated, AuditActions.StockOpening, AuditActions.StockAdjusted, AuditActions.StockQuarantined,
            AuditActions.StockReleased, AuditActions.StockWrittenOff,
        ]);
        (await MovementsAsync()).ShouldAllBe(m => m.UserId == 7 && m.UserName == "keeper");
    }

    [Fact]
    public async Task OnceAProductHasStock_ItsBaseUnitAndFactorsAreLocked_ButPricesAndNewUnitsAreNot()
    {
        var before = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(ProductId))).Value;
        before.UnitsLocked.ShouldBeFalse();
        await OpeningAsync(5, 2m, "B", _expiry);

        var locked = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(ProductId))).Value;
        locked.UnitsLocked.ShouldBeTrue();

        var changeFactor = locked.Units.Select(u => u.IsBase ? u : u with { Factor = 6 }).ToList();
        var changePrice = locked.Units.Select(u => u with { DefaultSellingPrice = 77m }).ToList();

        (await UpdateProductAsync(locked, changeFactor)).Error!.Code.ShouldBe(CatalogErrors.UnitsLockedByStock);
        (await UpdateProductAsync(locked, changePrice)).IsSuccess.ShouldBeTrue();
        var afterPrice = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(ProductId))).Value;
        var addUnit = afterPrice.Units.Append(new ProductUnitData(null, 5, false, 30, 300m, "BARCODE-30")).ToList();
        (await UpdateProductAsync(afterPrice, addUnit)).IsSuccess.ShouldBeTrue();

        var final = (await Services.InScopeAsync(s => s.GetRequiredService<ProductService>().GetForEditAsync(ProductId))).Value;
        final.Units.Select(u => (u.Factor, u.DefaultSellingPrice)).ShouldBe([(1, 77m), (3, 77m), (30, 300m)]);
    }

    private Task<Result> UpdateProductAsync(ProductForEdit product, IReadOnlyList<ProductUnitData> units) =>
        Services.InScopeAsync(s => s.GetRequiredService<ProductService>().UpdateAsync(product.Id, product.Details, units, product.RowVersion));

    private StockActionRequest Action(int batchId, int quantity, string? reason, string? note = null, int? unitId = null) =>
        new(Guid.NewGuid(), batchId, unitId ?? StripId, quantity, reason, note);

    private Task<Result> Adjust(
        AdjustmentDirection direction, int batchId, int quantity, string? reason, string? note = null, int? unitId = null) =>
        Inventory(s => s.AdjustAsync(direction, Action(batchId, quantity, reason, note, unitId)));

    private Task<Result> Quarantine(int batchId, int quantity) =>
        Inventory(s => s.QuarantineAsync(Action(batchId, quantity, StockReasons.Damaged)));
}
