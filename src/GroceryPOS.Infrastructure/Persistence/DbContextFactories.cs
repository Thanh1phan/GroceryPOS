using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GroceryPOS.Infrastructure.Persistence;

/// <summary>Provider settings shared by the app and the design-time tooling (<c>dotnet ef</c>).</summary>
public static class DbContextFactories
{
    public static void Sqlite(SqliteDbContextOptionsBuilder sqlite) =>
        sqlite.MigrationsHistoryTable("__EFMigrationsHistory");

    public static void SqlServer(SqlServerDbContextOptionsBuilder sqlServer) =>
        sqlServer.CommandTimeout(60);
}

/// <summary>Used by <c>dotnet ef migrations add X --context SqliteGroceryDbContext</c>.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteGroceryDbContext>
{
    public SqliteGroceryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteGroceryDbContext>()
            .UseSqlite(DatabaseOptions.DefaultSqliteConnection, DbContextFactories.Sqlite)
            .Options);
}

/// <summary>Used by <c>dotnet ef migrations add X --context SqlServerGroceryDbContext</c>; no server is contacted.</summary>
public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerGroceryDbContext>
{
    public SqlServerGroceryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerGroceryDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=GroceryPOS;Trusted_Connection=True;TrustServerCertificate=True", DbContextFactories.SqlServer)
            .Options);
}
