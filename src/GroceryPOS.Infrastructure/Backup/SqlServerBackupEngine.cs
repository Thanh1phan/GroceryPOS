using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Backup;
using GroceryPOS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Backup;

/// <summary>
/// SQL Server backups with <c>BACKUP DATABASE … WITH COPY_ONLY</c> (does not disturb the DBA's backup chain). The path is
/// opened by the SQL Server service, so on a separate server the backup folder must be a path on that server. Restoring
/// replaces a database other counters are connected to and is left to SQL Server Management Studio.
/// </summary>
internal sealed class SqlServerBackupEngine : IDatabaseBackupEngine
{
    private readonly DatabaseOptions _options;
    private readonly GroceryDbContext _context;

    public SqlServerBackupEngine(DatabaseOptions options, GroceryDbContext context)
    {
        _options = options;
        _context = context;
    }

    public string ProviderName => "SQL Server";

    public string FileExtension => ".bak";

    public bool SupportsRestore => false;

    public async Task CreateAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var database = new SqlConnectionStringBuilder(_options.ConnectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Chuỗi kết nối SQL Server không có tên cơ sở dữ liệu (Initial Catalog/Database).");
        }

        // The database name cannot be a parameter: it is bracket-quoted (']' doubled) instead; the path is a parameter.
        var sql = "BACKUP DATABASE [" + database.Replace("]", "]]", StringComparison.Ordinal) + "] TO DISK = @p0 WITH COPY_ONLY, INIT, NAME = N'GroceryPOS';";
        await _context.Database.ExecuteSqlRawAsync(sql, [destinationPath], cancellationToken);
    }

    public Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BackupInspection(
            IsValid: true,
            Problem: "Bản sao lưu SQL Server (.bak): nội dung được SQL Server kiểm tra khi phục hồi.",
            BackupSchemaCompatibility.Unknown,
            LatestMigration: null,
            Products: null,
            Customers: null,
            Sales: null,
            LastSaleAt: null));

    public Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Phục hồi SQL Server cần dùng SQL Server Management Studio.");
}
