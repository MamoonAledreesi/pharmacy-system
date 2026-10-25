using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Cash;
using Pharmacy.Domain.Cash;
using Pharmacy.Infrastructure.Persistence;

namespace Pharmacy.IntegrationTests.Cash;

/// <summary>A migrated throwaway database and a signed-in cashier with every cash permission except force-close.</summary>
public abstract class CashTestBase : IAsyncLifetime
{
    protected static readonly string[] Cashier =
    [
        Permissions.CashView, Permissions.CashOpenShift, Permissions.CashManualMovement,
    ];

    /// <summary>What the signed-in user may do at the start of each test; derived bases add the permissions of their module.</summary>
    protected virtual string[] InitialPermissions => Cashier;

    protected TestServices Services { get; } = new();

    public Task InitializeAsync() => InitializeCoreAsync();

    public Task DisposeAsync() => Services.DisposeAsync();

    protected void SignIn(int userId, string userName, params string[] permissions) =>
        Services.CurrentUser.SignInAs(userId, userName, isAdministrator: false, permissions);

    protected Task<T> Cash<T>(Func<CashService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<CashService>()));

    protected Task<T> Query<T>(Func<CashQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<CashQueryService>()));

    protected async Task<int> OpenAsync(decimal opening = 100m, string? note = "initial float")
    {
        var result = await Cash(s => s.OpenShiftAsync(new OpenShiftRequest(Guid.NewGuid(), opening, note)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected Task<Domain.Common.Result> ManualAsync(CashDirection direction, decimal amount, string note = "reason", Guid? operationId = null) =>
        Cash(s => s.RecordManualMovementAsync(new ManualCashRequest(operationId ?? Guid.NewGuid(), direction, amount, note)));

    protected Task<List<CashMovement>> MovementsAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync());

    protected Task<CashShift> ShiftAsync(int id) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashShifts.AsNoTracking().SingleAsync(x => x.Id == id));

    protected Task<List<string>> AuditActionsAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().AuditLogEntries.OrderBy(e => e.Id).Select(e => e.Action).ToListAsync());

    /// <summary>The drawer's expected cash must equal opening + the sum of its movements, for every shift.</summary>
    protected async Task AssertLedgerMatchesShiftsAsync()
    {
        var movements = await MovementsAsync();
        var shifts = await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CashShifts.AsNoTracking().ToListAsync());
        foreach (var shift in shifts)
        {
            var own = movements.Where(m => m.ShiftId == shift.Id).OrderBy(m => m.Id).ToList();
            shift.CurrentBalance.ShouldBe(shift.OpeningBalance + own.Sum(m => m.Amount));
            var running = shift.OpeningBalance;
            foreach (var movement in own)
            {
                running += movement.Amount;
                movement.BalanceAfter.ShouldBe(running);
            }
        }
    }

    private async Task InitializeCoreAsync()
    {
        await Services.InitializeAsync();
        SignIn(7, "cashier", InitialPermissions);
    }
}
