using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Authorization;
using Pharmacy.Application.Receivables;
using Pharmacy.Application.Sales;
using Pharmacy.Application.Users;
using Pharmacy.Domain.Sales;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.IntegrationTests.Purchasing;

namespace Pharmacy.IntegrationTests.Sales;

/// <summary>
/// The purchasing test database (one expiry-tracked product: strip, box of 3 strips) plus customers, a shift, and real
/// user accounts that act as supervisors. The signed-in user is a seller who may NOT give high discounts or sell
/// below cost, so those paths need a supervisor unless a test signs in with <see cref="Manager"/>.
/// </summary>
public abstract class SalesTestBase : PurchasingTestBase
{
    protected const string Password = "Pass1234";
    protected const int DefaultCustomerId = 1;

    protected static readonly string[] Seller =
    [
        Permissions.CatalogView, Permissions.InventoryView, Permissions.SalesView, Permissions.SalesCreate,
        Permissions.CustomersView, Permissions.CustomersManage, Permissions.ReceivablesSetOpeningBalance,
        Permissions.CashView, Permissions.CashOpenShift, Permissions.SettingsManage, Permissions.InventoryOpeningStock,
        Permissions.CatalogManage, Permissions.InventoryViewCost,
    ];

    protected static readonly string[] Manager = [.. Seller, Permissions.SalesGrantHighDiscount, Permissions.SalesSellBelowCost, Permissions.SalesViewCost];

    protected override string[] InitialPermissions => Seller;

    protected Task<T> Sales<T>(Func<SalesService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SalesService>()));

    protected Task<T> SalesQuery<T>(Func<SalesQueryService, Task<T>> call) =>
        Services.InScopeAsync(s => call(s.GetRequiredService<SalesQueryService>()));

    /// <summary>Opening stock of the standard product: <paramref name="strips"/> strips costing <paramref name="costPerStrip"/> each.</summary>
    protected async Task<int> StockAsync(int strips, decimal costPerStrip = 5m, string batch = "B1", DateOnly? expiry = null)
    {
        var result = await OpeningAsync(strips, costPerStrip, batch, expiry ?? InFuture);
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected async Task<int> CreateCustomerAsync(string name = "Ahmed Ali")
    {
        var result = await Services.InScopeAsync(s => s.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput(name, null, null, null, true)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    protected SaleLineRequest Strips(int quantity, decimal price, int? productId = null, int? unitId = null) =>
        new(productId ?? ProductId, unitId ?? StripId, quantity, price);

    protected SaleRequest Sale(
        IReadOnlyList<SaleLineRequest> lines,
        int customerId = DefaultCustomerId,
        decimal paid = 0m,
        decimal? discountAmount = null,
        decimal? discountPercent = null,
        string? belowCostReason = null,
        SupervisorCredentials? supervisor = null,
        Guid? operationId = null) =>
        new(operationId ?? Guid.NewGuid(), customerId, lines, discountAmount, discountPercent, paid, null, belowCostReason, supervisor);

    /// <summary>A sale paid in full in cash: opens a shift when none is open.</summary>
    protected async Task<int> SellAsync(IReadOnlyList<SaleLineRequest> lines, decimal paid)
    {
        var result = await Sales(s => s.CreateAsync(Sale(lines, paid: paid)));
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    /// <summary>Creates a real user in a built-in role (so the supervisor check has someone to verify) and returns their credentials.</summary>
    protected async Task<SupervisorCredentials> CreateSupervisorAsync(string userName, string role)
    {
        var admin = Services.CurrentUser;
        var (id, name, perms) = (admin.UserId, admin.UserName, admin.Permissions.ToArray());

        // Setting up accounts needs Users.Manage and an administrator to start from; then the test user is restored.
        if (!await Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Users.AnyAsync()))
        {
            await Services.InScopeAsync(s => s.GetRequiredService<IAccountService>().CompleteSetupAsync("admin", "Admin", Password));
        }

        admin.SignInAsAdministrator(1, "admin");
        var created = await Services.InScopeAsync(s => s.GetRequiredService<IUserManagementService>()
            .CreateUserAsync(new(userName, userName, Password, [role])));
        created.IsSuccess.ShouldBeTrue(created.Error?.Code);
        admin.SignInAs(id, name ?? "user", isAdministrator: false, perms);
        return new SupervisorCredentials(userName, Password);
    }

    protected Task<List<SalesInvoice>> InvoicesOfSalesAsync() =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().SalesInvoices.AsNoTracking()
            .Include(i => i.Lines).ThenInclude(l => l.Allocations).OrderBy(i => i.Id).ToListAsync());

    protected Task<List<Domain.Receivables.CustomerAccountEntry>> CustomerEntriesAsync(int customerId) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().CustomerAccountEntries.AsNoTracking()
            .Where(e => e.CustomerId == customerId).OrderBy(e => e.Id).ToListAsync());

    protected Task<decimal> CustomerBalanceAsync(int customerId) =>
        Services.InScopeAsync(s => s.GetRequiredService<PharmacyDbContext>().Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.CurrentBalance).SingleAsync());
}
