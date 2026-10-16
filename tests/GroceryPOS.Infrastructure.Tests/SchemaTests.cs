using GroceryPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Infrastructure.Tests;

public class SchemaTests
{
    [Fact]
    public void Sqlite_migrations_create_every_table()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.CreateContext();

        Assert.Empty(context.Database.GetPendingMigrations());
        var tables = context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToList();
        foreach (var expected in new[] { "Users", "Roles", "RolePermissions", "AuditLogs", "Products", "Sales", "SaleLines",
                     "SalePayments", "SaleReturns", "StockMovements", "GoodsReceipts", "SupplierPayments", "SupplierReturns", "Shifts", "CashMovements" })
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public void Sqlite_model_matches_its_latest_migration() =>
        AssertNoPendingModelChanges(new SqliteDesignTimeFactory().CreateDbContext([]));

    [Fact]
    public void SqlServer_model_matches_its_latest_migration() =>
        AssertNoPendingModelChanges(new SqlServerDesignTimeFactory().CreateDbContext([]));

    [Fact]
    public void SqlServer_create_script_uses_native_types()
    {
        using var context = new SqlServerDesignTimeFactory().CreateDbContext([]);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[SaleDate] datetimeoffset NOT NULL", script);
        Assert.Contains("[SellPrice] decimal(18,3) NOT NULL", script);
        Assert.Contains("[VatRate] decimal(5,2) NOT NULL", script);
        Assert.Contains("[Barcode] IS NOT NULL", script); // unique barcode allows many products without one
    }

    /// <summary>EF Core 8 equivalent of EF 9's HasPendingModelChanges: diff the snapshot against the current model.</summary>
    private static void AssertNoPendingModelChanges(DbContext context)
    {
        using (context)
        {
            var services = ((IInfrastructure<IServiceProvider>)context).Instance;
            var snapshot = services.GetRequiredService<IMigrationsAssembly>().ModelSnapshot;
            Assert.NotNull(snapshot);

            var snapshotModel = services.GetRequiredService<IModelRuntimeInitializer>().Initialize(snapshot.Model);
            var differences = services.GetRequiredService<IMigrationsModelDiffer>().GetDifferences(
                snapshotModel.GetRelationalModel(),
                services.GetRequiredService<IDesignTimeModel>().Model.GetRelationalModel());

            Assert.True(differences.Count == 0,
                "Model has changes not covered by a migration: " + string.Join(", ", differences.Select(d => d.GetType().Name)));
        }
    }
}
