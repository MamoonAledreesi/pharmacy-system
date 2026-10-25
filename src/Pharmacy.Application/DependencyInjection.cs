using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Auditing;
using Pharmacy.Application.Cash;
using Pharmacy.Application.Catalog;
using Pharmacy.Application.Expenses;
using Pharmacy.Application.Inventory;
using Pharmacy.Application.Purchasing;
using Pharmacy.Application.Receivables;
using Pharmacy.Application.Sales;
using Pharmacy.Application.Settings;

namespace Pharmacy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<AuditLogQueryService>();

        services.AddSingleton<PharmacySettingsState>();
        services.AddScoped<PharmacySettingsService>();

        services.AddScoped(typeof(LookupService<>));
        services.AddScoped<ProductService>();
        services.AddScoped<ProductQueryService>();

        services.AddScoped<InventoryLedger>();
        services.AddScoped<InventoryService>();
        services.AddScoped<InventoryQueryService>();

        services.AddScoped<CashLedger>();
        services.AddScoped<CashService>();
        services.AddScoped<CashQueryService>();

        services.AddScoped<SupplierService>();
        services.AddScoped<SupplierQueryService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<PurchaseQueryService>();
        services.AddScoped<PurchaseReturnService>();
        services.AddScoped<PurchaseReturnQueryService>();
        services.AddScoped<PayablesService>();

        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerQueryService>();
        services.AddScoped<ReceivablesService>();

        services.AddScoped<SalesService>();
        services.AddScoped<SalesQueryService>();
        services.AddScoped<SalesReturnService>();
        services.AddScoped<SalesReturnQueryService>();

        services.AddScoped<ExpenseService>();
        services.AddScoped<ExpenseCategoryService>();
        services.AddScoped<ExpenseQueryService>();

        return services;
    }
}
