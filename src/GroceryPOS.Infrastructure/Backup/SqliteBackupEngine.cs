using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Backup;
using GroceryPOS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroceryPOS.Infrastructure.Backup;

/// <summary>
/// SQLite backups with the online backup API (<see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>): a page-by-page
/// consistent copy taken while the app keeps running. Restore copies the backup back over the live file in the same way,
/// after clearing the connection pool, then applies pending migrations so a backup from an older version is upgraded.
/// </summary>
internal sealed class SqliteBackupEngine : IDatabaseBackupEngine
{
    private readonly DatabaseOptions _options;
    private readonly GroceryDbContext _context;
    private readonly ILogger<SqliteBackupEngine> _logger;

    public SqliteBackupEngine(DatabaseOptions options, GroceryDbContext context, ILogger<SqliteBackupEngine>? logger = null)
    {
        _options = options;
        _context = context;
        _logger = logger ?? NullLogger<SqliteBackupEngine>.Instance;
    }

    public string ProviderName => "SQLite";

    public string FileExtension => ".db";

    public bool SupportsRestore => true;

    public async Task CreateAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
        {
            throw new IOException($"File {Path.GetFileName(destinationPath)} đã tồn tại.");
        }

        var temp = destinationPath + ".tmp";
        try
        {
            await using (var live = new SqliteConnection(LiveConnectionString()))
            await using (var target = new SqliteConnection(FileConnectionString(temp, SqliteOpenMode.ReadWriteCreate)))
            {
                await live.OpenAsync(cancellationToken);
                await target.OpenAsync(cancellationToken);
                live.BackupDatabase(target);
            }

            // Only a complete copy gets the real name, so a crash never leaves a half-written backup in the list.
            File.Move(temp, destinationPath);
            _logger.LogInformation("Database backed up to {Path}", destinationPath);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public async Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(FileConnectionString(path, SqliteOpenMode.ReadOnly));
            await connection.OpenAsync(cancellationToken);

            var check = Convert.ToString(await ScalarAsync(connection, "PRAGMA quick_check;", cancellationToken));
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return BackupInspection.Invalid($"Cơ sở dữ liệu bị hỏng ({check}).");
            }

            if (!await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken) ||
                !await TableExistsAsync(connection, "Products", cancellationToken))
            {
                return BackupInspection.Invalid("Đây không phải cơ sở dữ liệu GroceryPOS.");
            }

            var applied = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            if (applied.Count == 0)
            {
                return BackupInspection.Invalid("Cơ sở dữ liệu chưa được khởi tạo.");
            }

            var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var schema = applied.Any(m => !known.Contains(m)) ? BackupSchemaCompatibility.Newer
                : applied.Count < known.Count ? BackupSchemaCompatibility.Older
                : BackupSchemaCompatibility.Current;

            var products = await CountAsync(connection, "Products", cancellationToken);
            var customers = await CountAsync(connection, "Customers", cancellationToken);
            var sales = await CountAsync(connection, "Sales", cancellationToken);
            DateTimeOffset? lastSale = null;
            if (sales > 0 && await ScalarAsync(connection, "SELECT MAX(SaleDate) FROM Sales;", cancellationToken) is long binary)
            {
                lastSale = (DateTimeOffset?)new DateTimeOffsetToBinaryConverter().ConvertFromProvider(binary);
            }

            return new BackupInspection(true, null, schema, applied[^1], products, customers, sales, lastSale);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Backup file {Path} could not be read", path);
            return BackupInspection.Invalid(ex.SqliteErrorCode == 26 // SQLITE_NOTADB
                ? "Không phải file sao lưu GroceryPOS (không đọc được cơ sở dữ liệu SQLite)."
                : ex.Message);
        }
    }

    public async Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        // Pooled connections would keep reading their cached pages of the old database.
        SqliteConnection.ClearAllPools();
        await using (var source = new SqliteConnection(FileConnectionString(sourcePath, SqliteOpenMode.ReadOnly)))
        await using (var live = new SqliteConnection(LiveConnectionString()))
        {
            await source.OpenAsync(cancellationToken);
            await live.OpenAsync(cancellationToken);
            source.BackupDatabase(live);
        }

        SqliteConnection.ClearAllPools();
        _context.ChangeTracker.Clear();
        await _context.Database.MigrateAsync(cancellationToken);
        _logger.LogWarning("Database restored from {Path}", sourcePath);
    }

    private string LiveConnectionString() =>
        new SqliteConnectionStringBuilder(_options.ConnectionString) { Pooling = false }.ToString();

    private static string FileConnectionString(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<int?> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken) =>
        await TableExistsAsync(connection, table, cancellationToken)
            ? Convert.ToInt32(await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"{table}\";", cancellationToken))
            : null;
}
