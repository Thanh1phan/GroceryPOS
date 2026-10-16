using GroceryPOS.Application.Abstractions;
using GroceryPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroceryPOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database for the configured provider. <see cref="GroceryDbContext"/> and <see cref="IUnitOfWork"/>
    /// resolve to the same scoped instance, so repositories and the unit of work share one change tracker.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddScoped(sp => new AuditableEntityInterceptor(
            sp.GetRequiredService<TimeProvider>(), sp.GetService<ICurrentUser>()));

        switch (options.Provider)
        {
            case DatabaseProvider.Sqlite:
                services.AddDbContext<GroceryDbContext, SqliteGroceryDbContext>((sp, builder) =>
                    Configure(builder.UseSqlite(options.ConnectionString, DbContextFactories.Sqlite), sp, options));
                break;
            case DatabaseProvider.SqlServer:
                services.AddDbContext<GroceryDbContext, SqlServerGroceryDbContext>((sp, builder) =>
                    Configure(builder.UseSqlServer(options.ConnectionString, DbContextFactories.SqlServer), sp, options));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Provider, "Unknown database provider.");
        }

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<GroceryDbContext>());
        return services;
    }

    private static void Configure(DbContextOptionsBuilder builder, IServiceProvider services, DatabaseOptions options)
    {
        builder.AddInterceptors(services.GetRequiredService<AuditableEntityInterceptor>());
        if (options.EnableSensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
        }
    }
}
