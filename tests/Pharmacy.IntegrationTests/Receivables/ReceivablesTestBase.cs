using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Receivables;
using Pharmacy.Domain.Common;
using Pharmacy.Domain.Receivables;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.IntegrationTests.Cash;

namespace Pharmacy.IntegrationTests.Receivables;

/// <summary>A migrated throwaway database and a signed-in user who may manage customers, collect debts, and use the cash register.</summary>
public abstract class ReceivablesTestBase : CashTestBase
{
    protected static readonly string[] Collector =
    [
        Permissions.CustomersView, Permissions.CustomersManage, Permissions.ReceivablesCollect,
        Permissions.ReceivablesReverse, Permissions.ReceivablesSetOpeningBalance,
        Permissions.CashView, Permissions.CashOpenShift, Permissions.CashManualMovement,
    ];

    protected override string[] InitialPermissions => Collector;

    protected Task<T> Customers<T>(Func<CustomerService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<CustomerService>()));

    protected Task<T> Receivables<T>(Func<ReceivablesService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<ReceivablesService>()));

    protected Task<T> CustomerQuery<T>(Func<CustomerQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<CustomerQueryService>()));

    protected async Task<int> CreateCustomerAsync(string name = "Ahmed Ali", string? phone = "0100")
    {
        var result = await Customers(s => s.CreateAsync(new CustomerInput(name, phone, null, null, true)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    /// <summary>A customer who owes the given amount through an opening balance.</summary>
    protected async Task<int> CreateDebtorAsync(decimal owes, string name = "Ahmed Ali")
    {
        var id = await CreateCustomerAsync(name, phone: null);
        var result = await Receivables(s => s.SetOpeningBalanceAsync(new CustomerOpeningBalanceRequest(Guid.NewGuid(), id, owes, null)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return id;
    }

    protected Task<Domain.Common.Result<int>> CollectAsync(int customerId, decimal amount, Guid? operationId = null) =>
        Receivables(s => s.CollectAsync(new CollectDebtRequest(operationId ?? Guid.NewGuid(), customerId, amount, "payment")));

    protected Task<Customer> CustomerAsync(int id) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Customers.AsNoTracking().SingleAsync(x => x.Id == id));

    protected Task<List<CustomerAccountEntry>> EntriesAsync(int customerId) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CustomerAccountEntries.AsNoTracking()
            .Where(e => e.CustomerId == customerId).OrderBy(e => e.Id).ToListAsync());

    protected async Task<decimal> DrawerAsync() =>
        await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashShifts.AsNoTracking()
            .Where(x => x.IsOpen).Select(x => x.CurrentBalance).SingleAsync());

    /// <summary>A customer balance must equal the sum of the statement, and every line must carry the running balance.</summary>
    protected async Task AssertStatementMatchesBalanceAsync(int customerId)
    {
        var running = 0m;
        foreach (var entry in await EntriesAsync(customerId))
        {
            running += entry.Amount;
            entry.BalanceAfter.ShouldBe(running);
        }

        (await CustomerAsync(customerId)).CurrentBalance.ShouldBe(running);
    }

    protected static void ShouldFailWith(Result result, string code) => result.Error?.Code.ShouldBe(code);
}
