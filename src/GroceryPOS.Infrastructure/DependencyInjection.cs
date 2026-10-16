using GroceryPOS.Application.Abstractions;
using GroceryPOS.Infrastructure.Auditing;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Repositories;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GroceryPOS.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the application layer needs from Infrastructure: the database (<see cref="AddPersistence"/>),
    /// repositories, audit log, BCrypt password hashing and the start-up <see cref="IDatabaseInitializer"/>.
    /// All database-bound services are scoped, so one scope = one unit of work.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, DatabaseOptions database, SeedOptions? seed = null)
    {
        services.AddPersistence(database);
        services.TryAddSingleton(seed ?? new SeedOptions());
        services.TryAddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        services.AddScoped<IAuditService>(sp => new AuditService(
            sp.GetRequiredService<GroceryDbContext>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<ICurrentUser>()));
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<ISaleReturnRepository, SaleReturnRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IGoodsReceiptRepository, GoodsReceiptRepository>();
        services.AddScoped<ISupplierPaymentRepository, SupplierPaymentRepository>();
        services.AddScoped<ISupplierReturnRepository, SupplierReturnRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        return services;
    }

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
