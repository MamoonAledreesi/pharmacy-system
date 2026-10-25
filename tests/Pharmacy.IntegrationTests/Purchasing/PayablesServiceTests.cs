using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Common;
using Pharmacy.Application.Purchasing;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Purchasing;

namespace Pharmacy.IntegrationTests.Purchasing;

public sealed class PayablesServiceTests : PurchasingTestBase
{
    private async Task<int> OweAsync(decimal boxCost = 100m, int boxes = 3)
    {
        var supplier = await CreateSupplierAsync();
        await BuyAsync([BoxLine(boxes, boxCost)], supplier);
        return supplier;
    }

    private Task<Domain.Common.Result<int>> PayAsync(int supplier, decimal amount, Guid? operationId = null) =>
        Payables(s => s.PayAsync(new PaySupplierRequest(operationId ?? Guid.NewGuid(), supplier, amount, "settlement")));

    [Fact]
    public async Task Payment_ReducesTheDebtAndTheDrawer_AndIsAudited()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);

        var paid = await PayAsync(supplier, 120m);

        paid.IsSuccess.ShouldBeTrue(paid.Error?.Code);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(180m);
        (await DrawerAsync()).ShouldBe(880m);
        var cash = (await CashMovementsAsync()).Single();
        cash.Type.ShouldBe(CashMovementType.SupplierPayment);
        cash.ReferenceId.ShouldBe(paid.Value);
        (await EntriesAsync(supplier)).Last().Type.ShouldBe(SupplierEntryType.Payment);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SupplierPaid);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task Payment_OfTheWholeDebt_LeavesZero()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);

        (await PayAsync(supplier, 300m)).IsSuccess.ShouldBeTrue();

        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task Payment_MoreThanOwed_IsRejectedAndChangesNothing()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);

        var result = await PayAsync(supplier, 300.01m);

        ShouldFailWith(result, PurchasingErrors.PaymentExceedsBalance);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(300m);
        (await DrawerAsync()).ShouldBe(1000m);
        (await CashMovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Payment_WithoutAnOpenShift_IsRejected()
    {
        var supplier = await OweAsync();

        var result = await PayAsync(supplier, 50m);

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(300m);
    }

    [Fact]
    public async Task Payment_MoreThanTheDrawerHolds_IsRejected()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(40m);

        var result = await PayAsync(supplier, 50m);

        ShouldFailWith(result, CashErrors.InsufficientCash);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(300m);
    }

    [Fact]
    public async Task Payment_RepeatedWithTheSameOperationId_IsAppliedOnce()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);
        var operationId = Guid.NewGuid();

        var first = await PayAsync(supplier, 100m, operationId);
        var second = await PayAsync(supplier, 100m, operationId);

        second.Value.ShouldBe(first.Value);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(200m);
        (await CashMovementsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Payment_WithoutPermission_IsForbidden()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);
        SignIn(Permissions.SuppliersView, Permissions.CashView);

        ShouldFailWith(await PayAsync(supplier, 10m), CommonErrors.Forbidden);
    }

    [Fact]
    public async Task ReversingAPayment_ReturnsTheCashAndTheDebt_Once()
    {
        var supplier = await OweAsync();
        await OpenShiftAsync(1000m);
        var payment = (await PayAsync(supplier, 100m)).Value;
        var operationId = Guid.NewGuid();

        var reversed = await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(operationId, payment, "typed twice")));
        var repeated = await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(operationId, payment, "typed twice")));
        var again = await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(Guid.NewGuid(), payment, "other")));

        reversed.IsSuccess.ShouldBeTrue(reversed.Error?.Code);
        repeated.IsSuccess.ShouldBeTrue();
        ShouldFailWith(again, PurchasingErrors.PaymentAlreadyReversed);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(300m);
        (await DrawerAsync()).ShouldBe(1000m);
        (await CashMovementsAsync()).Select(m => m.Type).ShouldBe([CashMovementType.SupplierPayment, CashMovementType.SupplierPaymentReversal]);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SupplierPaymentReversed);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task ReversingAPayment_NeedsAReason_AndAnOpenShift()
    {
        var supplier = await OweAsync();
        var shiftId = await OpenShiftAsync(1000m);
        var payment = (await PayAsync(supplier, 100m)).Value;

        var noReason = await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(Guid.NewGuid(), payment, " ")));

        ShouldFailWith(noReason, PurchasingErrors.ReasonRequired);

        // Close the shift: with no open drawer the cash cannot come back, so nothing is reversed.
        await Services.InScopeAsync(s => s.GetRequiredService<CashService>()
            .CloseShiftAsync(new CloseShiftRequest(Guid.NewGuid(), shiftId, 900m, null)));
        var closed = await Payables(s => s.ReversePaymentAsync(new ReversePaymentRequest(Guid.NewGuid(), payment, "mistake")));

        ShouldFailWith(closed, CashErrors.NoOpenShift);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(200m);
    }

    [Fact]
    public async Task OpeningBalance_IsRecordedOnce()
    {
        var supplier = await CreateSupplierAsync();

        var first = await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 500m, "from the old ledger")));
        var second = await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 100m, null)));

        first.IsSuccess.ShouldBeTrue(first.Error?.Code);
        ShouldFailWith(second, PurchasingErrors.OpeningBalanceAlreadySet);
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(500m);
        (await AuditActionsAsync()).ShouldContain(AuditActions.SupplierOpeningBalance);
        await AssertStatementMatchesBalanceAsync(supplier);
    }

    [Fact]
    public async Task OpeningBalance_RepeatedWithTheSameOperationId_IsNotAnError()
    {
        var supplier = await CreateSupplierAsync();
        var operationId = Guid.NewGuid();

        await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(operationId, supplier, 500m, null)));
        var repeat = await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(operationId, supplier, 500m, null)));

        repeat.IsSuccess.ShouldBeTrue();
        (await SupplierAsync(supplier)).CurrentBalance.ShouldBe(500m);
    }

    [Fact]
    public async Task OpeningBalance_WithoutPermission_IsForbidden()
    {
        var supplier = await CreateSupplierAsync();
        SignIn(Permissions.SuppliersView, Permissions.PayablesPay);

        var result = await Payables(s => s.SetOpeningBalanceAsync(new OpeningBalanceRequest(Guid.NewGuid(), supplier, 5m, null)));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }
}
