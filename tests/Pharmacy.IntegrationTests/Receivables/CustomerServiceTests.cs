using Pharmacy.Application.Auditing;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Common;
using Pharmacy.Application.Receivables;
using Pharmacy.Domain.Receivables;

namespace Pharmacy.IntegrationTests.Receivables;

public sealed class CustomerServiceTests : ReceivablesTestBase
{
    private const int DefaultCustomerId = 1;

    [Fact]
    public async Task TheDefaultCashCustomer_ExistsAfterMigration()
    {
        var customer = (await CustomerQuery(s => s.GetAsync(DefaultCustomerId))).Value;

        customer.IsDefaultCash.ShouldBeTrue();
        customer.IsActive.ShouldBeTrue();
        customer.Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task TheDefaultCashCustomer_IsEditable_AcceptsDebt_ButCannotBeDeactivated()
    {
        var loaded = (await CustomerQuery(s => s.GetAsync(DefaultCustomerId))).Value;

        var renamed = await Customers(s => s.UpdateAsync(DefaultCustomerId, new CustomerInput("Walk-in", "0100", null, null, true), loaded.RowVersion));
        var debt = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(Guid.NewGuid(), DefaultCustomerId, 50m, null)));
        var reloaded = (await CustomerQuery(s => s.GetAsync(DefaultCustomerId))).Value;
        var deactivated = await Customers(s => s.UpdateAsync(DefaultCustomerId, new CustomerInput("Walk-in", "0100", null, null, false), reloaded.RowVersion));

        renamed.IsSuccess.ShouldBeTrue(renamed.Error?.Code);
        debt.IsSuccess.ShouldBeTrue(debt.Error?.Code);
        ShouldFailWith(deactivated, ReceivablesErrors.DefaultCustomerCannotBeDeactivated);
        (await CustomerAsync(DefaultCustomerId)).Name.ShouldBe("Walk-in");
        (await CustomerAsync(DefaultCustomerId)).IsDefaultCash.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_StoresTheCustomerAndAuditsIt()
    {
        var id = await CreateCustomerAsync("Ahmed Ali");

        var customer = await CustomerAsync(id);
        customer.Name.ShouldBe("Ahmed Ali");
        customer.IsDefaultCash.ShouldBeFalse();
        (await AuditActionsAsync()).ShouldContain(AuditActions.CustomerCreated);
    }

    [Fact]
    public async Task Create_AllowsTheSameNameTwice()
    {
        await CreateCustomerAsync("Ahmed Ali", "0100");

        var second = await Customers(s => s.CreateAsync(new CustomerInput("Ahmed Ali", "0200", null, null, true)));

        second.IsSuccess.ShouldBeTrue(second.Error?.Code);
    }

    [Fact]
    public async Task Create_WithoutAName_IsRejected()
    {
        var result = await Customers(s => s.CreateAsync(new CustomerInput(" ", null, null, null, true)));

        ShouldFailWith(result, ReceivablesErrors.CustomerNameRequired);
    }

    [Fact]
    public async Task Update_ChangesTheDetails_AndRefusesAStaleVersion()
    {
        var id = await CreateCustomerAsync();
        var loaded = (await CustomerQuery(s => s.GetAsync(id))).Value;

        var first = await Customers(s => s.UpdateAsync(id, new CustomerInput("Ahmed Ali", "0111", "Cairo", null, true), loaded.RowVersion));
        var stale = await Customers(s => s.UpdateAsync(id, new CustomerInput("Ahmed Ali", "0222", null, null, true), loaded.RowVersion));

        first.IsSuccess.ShouldBeTrue(first.Error?.Code);
        ShouldFailWith(stale, CommonErrors.ConcurrencyConflict);
        (await CustomerAsync(id)).Phone.ShouldBe("0111");
        (await AuditActionsAsync()).ShouldContain(AuditActions.CustomerUpdated);
    }

    [Fact]
    public async Task Manage_WithoutPermission_IsForbidden()
    {
        SignIn(8, "viewer", Permissions.CustomersView);

        var result = await Customers(s => s.CreateAsync(new CustomerInput("X", null, null, null, true)));

        ShouldFailWith(result, CommonErrors.Forbidden);
    }

    [Fact]
    public async Task Search_FindsByNormalizedNameOrPhone_AndListsTheDefaultCustomerFirst()
    {
        await CreateCustomerAsync("أحمد علي", "0100");
        await CreateCustomerAsync("Sara", "0555");

        var byArabic = (await CustomerQuery(s => s.SearchAsync(new CustomerQuery("احمد", false, 0, 25)))).Value;
        var byPhone = (await CustomerQuery(s => s.SearchAsync(new CustomerQuery("0555", false, 0, 25)))).Value;
        var all = (await CustomerQuery(s => s.SearchAsync(new CustomerQuery(null, false, 0, 25)))).Value;

        byArabic.Items.Select(i => i.Name).ShouldBe(["أحمد علي"]);
        byPhone.Items.Select(i => i.Name).ShouldBe(["Sara"]);
        all.Items.First().IsDefaultCash.ShouldBeTrue();
        all.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task PhoneOwner_IsReportedForADuplicatePhone_ExceptTheCustomerThemself()
    {
        var id = await CreateCustomerAsync("Ahmed Ali", "0100");

        var other = (await CustomerQuery(s => s.FindPhoneOwnerAsync("0100", null))).Value;
        var self = (await CustomerQuery(s => s.FindPhoneOwnerAsync("0100", id))).Value;
        var none = (await CustomerQuery(s => s.FindPhoneOwnerAsync("0999", null))).Value;

        other.ShouldBe("Ahmed Ali");
        self.ShouldBeNull();
        none.ShouldBeNull();
    }

    [Fact]
    public async Task Statement_ListsNewestFirstWithRunningBalancesAndCollectionNumbers_AndMarksReversals()
    {
        var customer = await CreateDebtorAsync(100m);
        await OpenAsync(0m, note: null);
        var collection = (await CollectAsync(customer, 30m)).Value;
        await Receivables(s => s.ReverseCollectionAsync(new ReverseCollectionRequest(Guid.NewGuid(), collection, "typed twice")));

        var page = (await CustomerQuery(s => s.GetStatementAsync(new StatementQuery(customer, 0, 25)))).Value;

        page.Items.Select(e => e.Type).ShouldBe(
            [CustomerEntryType.CollectionReversal, CustomerEntryType.Collection, CustomerEntryType.OpeningBalance]);
        page.Items.Select(e => e.BalanceAfter).ShouldBe([100m, 70m, 100m]);
        page.Items.Single(e => e.Type == CustomerEntryType.Collection).CollectionReversed.ShouldBeTrue();
        page.Items.Single(e => e.Type == CustomerEntryType.Collection).ReferenceNumber.ShouldStartWith("RC-");
    }

    [Fact]
    public async Task Queries_WithoutViewPermission_AreForbidden()
    {
        SignIn(8, "nobody");

        (await CustomerQuery(s => s.SearchAsync(new CustomerQuery(null, false, 0, 25)))).IsFailure.ShouldBeTrue();
        (await CustomerQuery(s => s.GetAsync(DefaultCustomerId))).IsFailure.ShouldBeTrue();
    }
}
