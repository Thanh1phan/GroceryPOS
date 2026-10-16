using GroceryPOS.Application.Abstractions;
using GroceryPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Tests;

/// <summary>Fixed clock for deterministic audit stamps.</summary>
internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public UserSession? Session { get; set; }
}

/// <summary>
/// In-memory SQLite database built from the real migrations. The connection stays open for the
/// lifetime of the test so several contexts (write, then read back) see the same data.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.Migrate();
    }

    public FakeClock Clock { get; } = new();

    public FakeCurrentUser CurrentUser { get; } = new();

    public SqliteGroceryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SqliteGroceryDbContext>()
            .UseSqlite(_connection, DbContextFactories.Sqlite)
            .AddInterceptors(new AuditableEntityInterceptor(Clock, CurrentUser))
            .Options);

    public void Dispose() => _connection.Dispose();
}
