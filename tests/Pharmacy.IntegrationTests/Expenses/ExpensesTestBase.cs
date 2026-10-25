using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Expenses;
using Pharmacy.Domain.Expenses;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.IntegrationTests.Cash;

namespace Pharmacy.IntegrationTests.Expenses;

/// <summary>A migrated throwaway database and a signed-in user who may record, reverse, and manage expenses and use the cash register.</summary>
public abstract class ExpensesTestBase : CashTestBase
{
    protected const int RentCategory = 1;
    protected const int ElectricityCategory = 2;

    protected static readonly string[] Accountant =
    [
        Permissions.ExpensesView, Permissions.ExpensesCreate, Permissions.ExpensesReverse, Permissions.ExpensesManage,
        Permissions.CashView, Permissions.CashOpenShift, Permissions.CashManualMovement,
    ];

    protected override string[] InitialPermissions => Accountant;

    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    protected Task<T> Expenses<T>(Func<ExpenseService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<ExpenseService>()));

    protected Task<T> Categories<T>(Func<ExpenseCategoryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<ExpenseCategoryService>()));

    protected Task<T> ExpenseQuery<T>(Func<ExpenseQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<ExpenseQueryService>()));

    protected RecordExpenseRequest Spend(
        decimal amount, int category = RentCategory, DateOnly? date = null, string? description = "monthly rent", string? payee = null, Guid? operationId = null) =>
        new(operationId ?? Guid.NewGuid(), category, amount, date ?? Today, description, payee);

    protected async Task<int> RecordAsync(decimal amount, int category = RentCategory, DateOnly? date = null, string description = "monthly rent")
    {
        var result = await Expenses(s => s.RecordAsync(Spend(amount, category, date, description)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected Task<List<Expense>> ExpensesInDbAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Expenses.AsNoTracking().OrderBy(e => e.Id).ToListAsync());

    protected async Task<decimal> DrawerAsync() =>
        await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashShifts.AsNoTracking()
            .Where(x => x.IsOpen).Select(x => x.CurrentBalance).SingleAsync());

    protected static void ShouldFailWith(Pharmacy.Domain.Common.Result result, string code) => result.Error?.Code.ShouldBe(code);
}
