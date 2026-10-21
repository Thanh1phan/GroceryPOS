using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GroceryPOS.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work for the whole store database. Provider-neutral: the provider-specific subclasses
/// (<see cref="SqliteGroceryDbContext"/>, <see cref="SqlServerGroceryDbContext"/>) only exist so each
/// provider keeps its own migration history.
/// </summary>
public class GroceryDbContext : DbContext, IUnitOfWork
{
    public GroceryDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GroceryDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money is whole đồng but quantities/rates carry decimals; 18,3 covers both without losing data.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 3);
        configurationBuilder.Properties<decimal?>().HavePrecision(18, 3);
        configurationBuilder.Properties<string>().HaveMaxLength(500);

        if (Database.IsSqlite())
        {
            // SQLite has no DateTimeOffset type: EF would store text that cannot be compared or ordered in SQL.
            // The binary form is a sortable 64-bit integer (UTC ticks + offset), so period filters run in the database.
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }
}

/// <summary>SQLite flavour (default, single-machine store). Owns the migrations in <c>Migrations/Sqlite</c>.</summary>
public sealed class SqliteGroceryDbContext : GroceryDbContext
{
    public SqliteGroceryDbContext(DbContextOptions<SqliteGroceryDbContext> options) : base(options)
    {
    }
}

/// <summary>SQL Server flavour (several counters sharing one database). Owns the migrations in <c>Migrations/SqlServer</c>.</summary>
public sealed class SqlServerGroceryDbContext : GroceryDbContext
{
    public SqlServerGroceryDbContext(DbContextOptions<SqlServerGroceryDbContext> options) : base(options)
    {
    }
}
