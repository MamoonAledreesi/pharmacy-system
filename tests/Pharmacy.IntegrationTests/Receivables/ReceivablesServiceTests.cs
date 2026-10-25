using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Receivables;
using Pharmacy.Domain.Cash;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.IntegrationTests.Receivables;

public sealed class ReceivablesServiceTests : ReceivablesTestBase
{
    [Fact]
    public async Task Collection_ReducesTheDebtAndFillsTheDrawer_AndIsAudited()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);

        var collected = await CollectAsync(customer, 120m);

        collected.IsSuccess.ShouldBeTrue(collected.Error?.Code);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(180m);
        (await DrawerAsync()).ShouldBe(220m);
        var cash = (await MovementsAsync()).Single();
        cash.Type.ShouldBe(CashMovementType.DebtCollection);
        cash.Amount.ShouldBe(120m);
        cash.ReferenceId.ShouldBe(collected.Value);
        (await EntriesAsync(customer)).Last().Type.ShouldBe(CustomerEntryType.Collection);
        (await AuditActionsAsync()).ShouldContain(AuditActions.DebtCollected);
        await AssertStatementMatchesBalanceAsync(customer);
    }

    [Fact]
    public async Task CollectionNumbers_AreSequential()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(0m, note: null);

        await CollectAsync(customer, 10m);
        await CollectAsync(customer, 10m);

        var page = (await CustomerQuery(s => s.GetStatementAsync(new StatementQuery(customer, 0, 25)))).Value;
        page.Items.Where(e => e.Type == CustomerEntryType.Collection).Select(e => e.ReferenceNumber)
            .ShouldBe([$"RC-{DateTime.Now.Year}-000002", $"RC-{DateTime.Now.Year}-000001"]);
    }

    [Fact]
    public async Task Collection_OfTheWholeDebt_LeavesZero()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(0m, note: null);

        (await CollectAsync(customer, 300m)).IsSuccess.ShouldBeTrue();

        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task Collection_MoreThanOwed_IsRejectedAndChangesNothing()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);

        var result = await CollectAsync(customer, 300.01m);

        ShouldFailWith(result, ReceivablesErrors.CollectionExceedsBalance);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(300m);
        (await DrawerAsync()).ShouldBe(100m);
        (await MovementsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Collection_WithoutAnOpenShift_IsRejected()
    {
        var customer = await CreateDebtorAsync(300m);

        var result = await CollectAsync(customer, 50m);

        ShouldFailWith(result, CashErrors.NoOpenShift);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(300m);
    }

    [Fact]
    public async Task Collection_RepeatedWithTheSameOperationId_IsAppliedOnce()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);
        var operationId = Guid.NewGuid();

        var first = await CollectAsync(customer, 100m, operationId);
        var second = await CollectAsync(customer, 100m, operationId);

        second.Value.ShouldBe(first.Value);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(200m);
        (await MovementsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Collection_WithoutPermission_IsForbidden()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);
        SignIn(8, "viewer", Permissions.CustomersView, Permissions.CashView);

        ShouldFailWith(await CollectAsync(customer, 10m), CommonErrors.Forbidden);
    }

    [Fact]
    public async Task ReversingACollection_TakesTheCashBackAndRestoresTheDebt_Once()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);
        var collection = (await CollectAsync(customer, 100m)).Value;
        var operationId = Guid.NewGuid();

        var reversed = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(operationId, collection, "wrong customer")));
        var repeated = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(operationId, collection, "wrong customer")));
        var again = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), collection, "other")));

        reversed.IsSuccess.ShouldBeTrue(reversed.Error?.Code);
        repeated.IsSuccess.ShouldBeTrue();
        ShouldFailWith(again, ReceivablesErrors.CollectionAlreadyReversed);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(300m);
        (await DrawerAsync()).ShouldBe(100m);
        (await MovementsAsync()).Select(m => m.Type).ShouldBe([CashMovementType.DebtCollection, CashMovementType.DebtCollectionReversal]);
        (await AuditActionsAsync()).ShouldContain(AuditActions.CollectionReversed);
        await AssertStatementMatchesBalanceAsync(customer);
    }

    [Fact]
    public async Task ReversingACollection_NeedsAReason_AndEnoughCashInTheDrawer()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(0m, note: null);
        var collection = (await CollectAsync(customer, 100m)).Value;

        var noReason = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), collection, " ")));
        // The money was taken out by hand, so the drawer can no longer give it back.
        await ManualAsync(CashDirection.Out, 60m);
        var short_ = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), collection, "mistake")));

        ShouldFailWith(noReason, ReceivablesErrors.ReasonRequired);
        ShouldFailWith(short_, CashErrors.InsufficientCash);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(200m);
    }

    [Fact]
    public async Task Reversal_WithoutPermission_IsForbidden()
    {
        var customer = await CreateDebtorAsync(300m);
        await OpenAsync(100m);
        var collection = (await CollectAsync(customer, 100m)).Value;
        SignIn(8, "collector", Permissions.CustomersView, Permissions.ReceivablesCollect, Permissions.CashView);

        var result = await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), collection, "x")));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }

    [Fact]
    public async Task OpeningBalance_IsRecordedOnce()
    {
        var customer = await CreateCustomerAsync();

        var first = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(Guid.NewGuid(), customer, 500m, "old ledger")));
        var second = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(Guid.NewGuid(), customer, 100m, null)));

        first.IsSuccess.ShouldBeTrue(first.Error?.Code);
        ShouldFailWith(second, ReceivablesErrors.OpeningBalanceAlreadySet);
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(500m);
        (await AuditActionsAsync()).ShouldContain(AuditActions.CustomerOpeningBalance);
        await AssertStatementMatchesBalanceAsync(customer);
    }

    [Fact]
    public async Task OpeningBalance_RepeatedWithTheSameOperationId_IsNotAnError()
    {
        var customer = await CreateCustomerAsync();
        var operationId = Guid.NewGuid();

        await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(operationId, customer, 500m, null)));
        var repeat = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(operationId, customer, 500m, null)));

        repeat.IsSuccess.ShouldBeTrue();
        (await CustomerAsync(customer)).CurrentBalance.ShouldBe(500m);
    }

    [Fact]
    public async Task OpeningBalance_WithoutPermission_IsForbidden()
    {
        var customer = await CreateCustomerAsync();
        SignIn(8, "viewer", Permissions.CustomersView, Permissions.ReceivablesCollect);

        var result = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(Guid.NewGuid(), customer, 5m, null)));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }
}
