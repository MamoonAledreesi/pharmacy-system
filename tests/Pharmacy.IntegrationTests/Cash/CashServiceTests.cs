using Pharmacy.Application.Authorization;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Cash;
using Pharmacy.Domain.Cash;
using Pharmacy.Application.Common;

namespace Pharmacy.IntegrationTests.Cash;

public sealed class CashServiceTests : CashTestBase
{
    [Fact]
    public async Task OpenShift_CreatesTheShiftAndAuditsIt()
    {
        var id = await OpenAsync(150m, "float");

        var shift = await ShiftAsync(id);
        shift.IsOpen.ShouldBeTrue();
        shift.OpeningBalance.ShouldBe(150m);
        shift.CurrentBalance.ShouldBe(150m);
        shift.OpenedByUserName.ShouldBe("cashier");
        (await AuditActionsAsync()).ShouldContain(AuditActions.CashShiftOpened);
    }

    [Fact]
    public async Task OpenShift_WhileAnotherIsOpen_IsRejected()
    {
        await OpenAsync();

        var second = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), 10m, "x")));

        second.Error!.Code.ShouldBe(CashErrors.ShiftAlreadyOpen);
    }

    [Fact]
    public async Task OpenShift_SubmittedTwiceWithTheSameOperationId_ReturnsTheSameShift()
    {
        var operationId = Guid.NewGuid();

        var first = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(operationId, 100m, "float")));
        var second = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(operationId, 100m, "float")));

        second.Value.ShouldBe(first.Value);
    }

    [Fact]
    public async Task OpenShift_WithoutPermission_IsForbidden()
    {
        SignIn(8, "viewer", Permissions.CashView);

        var result = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), 0m, null)));

        result.Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    [Fact]
    public async Task NextShift_MustExplainAnOpeningBalanceThatDiffersFromTheLastCount()
    {
        var first = await OpenAsync(100m);
        (await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), first, 100m, null)))).IsSuccess.ShouldBeTrue();

        var withoutNote = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), 80m, null)));
        var asCounted = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), 100m, null)));

        withoutNote.Error!.Code.ShouldBe(CashErrors.OpeningDifferenceNoteRequired);
        asCounted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ManualMovements_UpdateTheDrawerAndTheLedger()
    {
        var id = await OpenAsync(100m);

        (await ManualAsync(CashDirection.In, 50m)).IsSuccess.ShouldBeTrue();
        (await ManualAsync(CashDirection.Out, 30m)).IsSuccess.ShouldBeTrue();

        (await ShiftAsync(id)).CurrentBalance.ShouldBe(120m);
        (await MovementsAsync()).Select(m => m.Amount).ShouldBe([50m, -30m]);
        (await AuditActionsAsync()).Count(a => a == AuditActions.CashManualMovement).ShouldBe(2);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task ManualMovement_WithoutAnOpenShift_IsRejected()
    {
        var result = await ManualAsync(CashDirection.In, 10m);

        result.Error!.Code.ShouldBe(CashErrors.NoOpenShift);
    }

    [Fact]
    public async Task ManualOut_BeyondTheDrawer_IsRejected()
    {
        await OpenAsync(100m);

        var result = await ManualAsync(CashDirection.Out, 100.01m);

        result.Error!.Code.ShouldBe(CashErrors.InsufficientCash);
        (await MovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ManualMovement_NeedsANote()
    {
        await OpenAsync();

        var result = await ManualAsync(CashDirection.In, 5m, note: "  ");

        result.Error!.Code.ShouldBe(CashErrors.NoteRequired);
    }

    [Fact]
    public async Task ManualMovement_RepeatedWithTheSameOperationId_IsAppliedOnce()
    {
        await OpenAsync(100m);
        var operationId = Guid.NewGuid();

        await ManualAsync(CashDirection.In, 25m, operationId: operationId);
        var repeat = await ManualAsync(CashDirection.In, 25m, operationId: operationId);

        repeat.IsSuccess.ShouldBeTrue();
        (await MovementsAsync()).Count.ShouldBe(1);
        await AssertLedgerMatchesShiftsAsync();
    }

    [Fact]
    public async Task ManualMovement_WithoutPermission_IsForbidden()
    {
        await OpenAsync();
        SignIn(8, "viewer", Permissions.CashView, Permissions.CashOpenShift);

        var result = await ManualAsync(CashDirection.In, 5m);

        result.Error!.Code.ShouldBe(CommonErrors.Forbidden);
    }

    [Fact]
    public async Task CloseShift_RecordsTheCountAndTheShortage()
    {
        var id = await OpenAsync(100m);
        await ManualAsync(CashDirection.In, 20m);

        var closed = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 115m, "five missing")));

        closed.IsSuccess.ShouldBeTrue();
        var shift = await ShiftAsync(id);
        shift.IsOpen.ShouldBeFalse();
        shift.CountedBalance.ShouldBe(115m);
        shift.Difference.ShouldBe(-5m);
        (await AuditActionsAsync()).ShouldContain(AuditActions.CashShiftClosed);
    }

    [Fact]
    public async Task CloseShift_WithADifferenceAndNoNote_IsRejectedAndTheShiftStaysOpen()
    {
        var id = await OpenAsync(100m);

        var result = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 90m, null)));

        result.Error!.Code.ShouldBe(CashErrors.CloseDifferenceNoteRequired);
        (await ShiftAsync(id)).IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task CloseShift_RepeatedWithTheSameOperationId_Succeeds_ButAnotherCloseIsRejected()
    {
        var id = await OpenAsync(100m);
        var operationId = Guid.NewGuid();

        await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(operationId, id, 100m, null)));
        var repeat = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(operationId, id, 100m, null)));
        var other = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 100m, null)));

        repeat.IsSuccess.ShouldBeTrue();
        other.Error!.Code.ShouldBe(CashErrors.ShiftClosed);
    }

    [Fact]
    public async Task ClosingSomeoneElsesShift_NeedsForceClosePermission()
    {
        var id = await OpenAsync(100m);
        SignIn(8, "other", Permissions.CashView, Permissions.CashOpenShift);

        var denied = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 100m, "reason")));

        denied.Error!.Code.ShouldBe(CommonErrors.Forbidden);
        (await ShiftAsync(id)).IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task ForceClose_NeedsAReason_AndIsMarkedAndAudited()
    {
        var id = await OpenAsync(100m);
        SignIn(9, "manager", Permissions.CashView, Permissions.CashForceClose);

        var withoutReason = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 100m, null)));
        var closed = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), id, 100m, "cashier went home")));

        withoutReason.Error!.Code.ShouldBe(CashErrors.ForceCloseReasonRequired);
        closed.IsSuccess.ShouldBeTrue();
        var shift = await ShiftAsync(id);
        shift.WasForceClosed.ShouldBeTrue();
        shift.ClosedByUserName.ShouldBe("manager");
        (await AuditActionsAsync()).ShouldContain(AuditActions.CashShiftForceClosed);
    }

    [Fact]
    public async Task CloseShift_ForAnUnknownShift_IsRejected()
    {
        var result = await Cash(s => s.CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), 999, 0m, null)));

        result.Error!.Code.ShouldBe(CashErrors.UnknownShift);
    }
}
