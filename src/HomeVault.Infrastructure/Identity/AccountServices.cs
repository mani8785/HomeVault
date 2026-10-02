using HomeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVault.Infrastructure.Identity;

/// <summary>Registers Infrastructure-owned account persistence and Identity policy at a composition root.</summary>
public static class AccountServices
{
    /// <summary>Configures existing SQLite storage, explicit token protection and scoped account operations.</summary>
    /// <param name="services">Host or operator service collection.</param>
    /// <param name="databasePath">Absolute existing database path; no automatic migrations.</param>
    /// <param name="protection">Caller-owned validated persistent protection provider.</param>
    /// <returns>The supplied collection.</returns>
    /// <exception cref="ArgumentException">The database path is not absolute.</exception>
    /// <exception cref="ArgumentNullException">The protection provider is null.</exception>
    public static IServiceCollection AddHomeVaultAccounts(this IServiceCollection services, string databasePath, IDataProtectionProvider protection)
    {
        ArgumentNullException.ThrowIfNull(protection);
        if (!Path.IsPathFullyQualified(databasePath)) throw new ArgumentException("Absolute database path required.");
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        services.AddLogging();
        services.AddSingleton(protection);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<HomeVaultDbContext>(options => options.UseSqlite(connection));
        services.AddIdentityCore<HomeVaultUser>(options =>
        {
            options.Password.RequiredLength = 15;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<HomeVaultDbContext>()
          .AddTokenProvider<DataProtectorTokenProvider<HomeVaultUser>>(TokenOptions.DefaultProvider);
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromMinutes(30));
        // Identity V3: maintained PBKDF2 implementation and automatic hash upgrades.
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddScoped<AccountOperations>();
        return services;
    }
}
