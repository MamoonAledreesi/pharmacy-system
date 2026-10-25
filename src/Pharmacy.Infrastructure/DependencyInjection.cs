using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pharmacy.Application.Abstractions;
using Pharmacy.Application.Reports;
using Pharmacy.Application.Sales;
using Pharmacy.Application.Users;
using Pharmacy.Infrastructure.Identity;
using Pharmacy.Infrastructure.Persistence;
using Pharmacy.Infrastructure.Reporting;

namespace Pharmacy.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PharmacyDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        // No EnableRetryOnFailure: financial use cases open explicit transactions, which a
        // retrying execution strategy rejects. The app runs on a LAN next to its database.
        services.AddDbContext<PharmacyDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IPharmacyDbContext>(provider => provider.GetRequiredService<PharmacyDbContext>());

        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ISupervisorVerifier, SupervisorVerifier>();
        services.AddSingleton<IReportExporter, ReportExporter>();

        return services;
    }

    /// <summary>
    /// Registers ASP.NET Core Identity stores and the approved password/lockout policy.
    /// The web host adds the sign-in manager and cookies on the returned builder.
    /// </summary>
    public static IdentityBuilder AddPharmacyIdentity(this IServiceCollection services)
    {
        return services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;

                options.Password.RequiredLength = UserRules.PasswordMinLength;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = UserRules.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = UserRules.LockoutDuration;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<PharmacyDbContext>()
            .AddPasswordValidator<PasswordLetterValidator>()
            .AddClaimsPrincipalFactory<PharmacyUserClaimsPrincipalFactory>();
    }
}

