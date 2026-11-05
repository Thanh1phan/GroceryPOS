using GroceryPOS.Application;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Settings;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Settings;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grocerypos-tests", Guid.NewGuid().ToString("N"));

    public SettingsPersistenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private ServiceProvider BuildApp() => new ServiceCollection()
        .AddApplication(store: new StoreProfile { Name = "Từ appsettings" })
        .AddInfrastructure(new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(_dir, "app.db")}" })
        .AddSingleton<IPasswordHasher>(new BCryptPasswordHasher(workFactor: 4))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public void AppSettings_rows_round_trip_with_a_unique_key()
    {
        using var db = new SqliteTestDatabase();
        using (var context = db.CreateContext())
        {
            context.AppSettings.Add(new AppSetting("store.name", "Tạp hóa Cô Ba"));
            context.SaveChanges();
        }

        using (var context = db.CreateContext())
        {
            var row = context.AppSettings.Single();
            Assert.Equal("store.name", row.Key);
            Assert.Equal("Tạp hóa Cô Ba", row.Value);
            Assert.Equal(db.Clock.Now, row.CreatedAt);

            context.AppSettings.Add(new AppSetting("store.name", "trùng"));
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public async Task Saved_settings_are_used_at_once_and_after_a_restart()
    {
        using (var app = BuildApp())
        {
            using (var scope = app.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync("admin", "admin123")).IsSuccess);
            }

            using (var scope = app.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var current = (await service.GetAsync()).Value;
                Assert.Equal("Từ appsettings", current.StoreName);

                var saved = await service.SaveAsync(current with { StoreName = "Tạp hóa Cô Ba", VndPerPoint = 20_000, ReturnWindowDays = 3 });
                Assert.True(saved.IsSuccess);
            }

            using (var scope = app.CreateScope())
            {
                // A new scope (next screen / use case) sees the new values without restarting.
                Assert.Equal("Tạp hóa Cô Ba", scope.ServiceProvider.GetRequiredService<StoreProfile>().Name);
                Assert.Equal(20_000, scope.ServiceProvider.GetRequiredService<SalesOptions>().Loyalty.VndPerPoint);

                var audit = await scope.ServiceProvider.GetRequiredService<GroceryDbContext>().AuditLogs
                    .SingleAsync(a => a.Action == AuditActions.SettingsChanged);
                Assert.Equal("admin", audit.Username);
                Assert.Contains("Hạn đổi trả: 7 ngày → 3 ngày", audit.Details);
            }
        }

        using (var restarted = BuildApp())
        {
            Assert.Equal("Từ appsettings", restarted.GetRequiredService<StoreSettingsState>().Store.Name);
            using (var scope = restarted.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
                await scope.ServiceProvider.GetRequiredService<ISettingsService>().ReloadAsync();
            }

            using (var scope = restarted.CreateScope())
            {
                Assert.Equal("Tạp hóa Cô Ba", scope.ServiceProvider.GetRequiredService<StoreProfile>().Name);
                Assert.Equal(3, scope.ServiceProvider.GetRequiredService<SalesOptions>().ReturnWindowDays);
            }
        }
    }
}
