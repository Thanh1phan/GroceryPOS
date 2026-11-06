using GroceryPOS.Application;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Backup;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Infrastructure.Tests;

/// <summary>Backups and restores of a real SQLite file through the composed application.</summary>
public sealed class BackupPersistenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grocerypos-tests", Guid.NewGuid().ToString("N"));

    public BackupPersistenceTests() => Directory.CreateDirectory(_dir);

    private string BackupFolder => Path.Combine(_dir, "backups");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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
        .AddApplication(backup: new BackupOptions { Folder = BackupFolder })
        .AddInfrastructure(new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(_dir, "app.db")}" })
        .AddSingleton<IPasswordHasher>(new BCryptPasswordHasher(workFactor: 4))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static async Task SignInAsync(ServiceProvider app)
    {
        using var scope = app.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthService>().LoginAsync("admin", "admin123")).IsSuccess);
    }

    private static async Task AddCategoryAsync(ServiceProvider app, string name)
    {
        using var scope = app.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GroceryDbContext>();
        context.Categories.Add(new Category(name));
        await context.SaveChangesAsync();
    }

    private static async Task<List<string>> CategoryNamesAsync(ServiceProvider app)
    {
        using var scope = app.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GroceryDbContext>().Categories.Select(c => c.Name).OrderBy(n => n).ToListAsync();
    }

    [Fact]
    public async Task Backup_is_a_readable_copy_and_restore_brings_the_old_data_back_with_an_audit_entry()
    {
        using var app = BuildApp();
        await SignInAsync(app);
        await AddCategoryAsync(app, "Bánh kẹo");

        BackupFile backup;
        using (var scope = app.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            backup = (await service.CreateAsync()).Value;

            var inspection = (await service.InspectAsync(backup.FullPath)).Value;
            Assert.True(inspection.CanRestore);
            Assert.Equal(BackupSchemaCompatibility.Current, inspection.Schema);
            Assert.Equal(0, inspection.Products);
            Assert.Equal(0, inspection.Sales);
            Assert.EndsWith("_AddAppSettings", inspection.LatestMigration);
        }

        Assert.StartsWith(BackupFolder, backup.FullPath);
        Assert.Empty(Directory.GetFiles(BackupFolder, "*.tmp"));

        // Work done after the backup …
        await AddCategoryAsync(app, "Nước giải khát");
        Assert.Contains("Nước giải khát", await CategoryNamesAsync(app));

        RestoreOutcome outcome;
        using (var scope = app.CreateScope())
        {
            var restored = await scope.ServiceProvider.GetRequiredService<IBackupService>().RestoreAsync(backup.FullPath);
            Assert.True(restored.IsSuccess, restored.Error.Message);
            outcome = restored.Value;
        }

        // … is gone, but kept in the safety copy.
        var names = await CategoryNamesAsync(app);
        Assert.Contains("Bánh kẹo", names);
        Assert.DoesNotContain("Nước giải khát", names);
        Assert.Equal(BackupKind.BeforeRestore, outcome.SafetyCopy.Kind);

        using (var scope = app.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GroceryDbContext>();
            var audit = await context.AuditLogs.Where(a => a.Action == AuditActions.BackupRestored).SingleAsync();
            Assert.Equal("admin", audit.Username);
            Assert.Contains(outcome.SafetyCopy.FileName, audit.Details);
            // The backup was taken before its own audit row was saved.
            Assert.False(await context.AuditLogs.AnyAsync(a => a.Action == AuditActions.BackupCreated));

            var safety = (await scope.ServiceProvider.GetRequiredService<IBackupService>().InspectAsync(outcome.SafetyCopy.FullPath)).Value;
            Assert.True(safety.CanRestore);
        }

        using (var scope = app.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IBackupService>().RestoreAsync(outcome.SafetyCopy.FullPath)).IsSuccess);
        }

        Assert.Contains("Nước giải khát", await CategoryNamesAsync(app));
    }

    [Fact]
    public async Task Start_up_backup_works_before_anyone_signs_in()
    {
        using var app = BuildApp();
        using (var scope = app.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }

        using (var scope = app.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IBackupService>().RunAutomaticIfDueAsync();
            Assert.Equal(BackupKind.Automatic, result.Value!.Kind);
            Assert.True(result.Value.SizeBytes > 0);
        }
    }

    [Fact]
    public async Task Inspection_rejects_foreign_files_and_flags_backups_from_a_newer_version()
    {
        using var app = BuildApp();
        await SignInAsync(app);

        var text = Path.Combine(_dir, "not-a-db.db");
        await File.WriteAllTextAsync(text, new string('x', 4096));

        var otherDb = Path.Combine(_dir, "other.db");
        await using (var connection = new SqliteConnection($"Data Source={otherDb};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Notes (Id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        BackupFile newer;
        using (var scope = app.CreateScope())
        {
            newer = (await scope.ServiceProvider.GetRequiredService<IBackupService>().CreateAsync()).Value;
        }

        await using (var connection = new SqliteConnection($"Data Source={newer.FullPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29991231000000_FromTheFuture', '9.0.0');";
            await command.ExecuteNonQueryAsync();
        }

        using (var scope = app.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var foreign = (await service.InspectAsync(text)).Value;
            Assert.False(foreign.IsValid);
            Assert.Contains("Không phải file sao lưu", foreign.Problem);

            Assert.Contains("không phải cơ sở dữ liệu GroceryPOS", (await service.InspectAsync(otherDb)).Value.Problem);

            var future = (await service.InspectAsync(newer.FullPath)).Value;
            Assert.True(future.IsValid);
            Assert.Equal(BackupSchemaCompatibility.Newer, future.Schema);

            var refused = await service.RestoreAsync(newer.FullPath);
            Assert.Contains("mới hơn", refused.Error.Message);
        }
    }
}
