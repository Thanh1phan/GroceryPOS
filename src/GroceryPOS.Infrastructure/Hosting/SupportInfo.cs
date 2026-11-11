using GroceryPOS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GroceryPOS.Infrastructure.Hosting;

/// <summary>One line of the Thông tin phần mềm dialog.</summary>
public sealed record SupportInfoLine(string Label, string Value);

/// <summary>
/// Facts a shop owner reads out to support: version, where the program, data, database, logs and backups are. Built
/// once at start-up from the resolved options. Secrets never leave this class: SQL Server passwords are masked.
/// </summary>
public sealed class SupportInfo
{
    public SupportInfo(
        string version, string runtime, string appDirectory, AppDataLocation location,
        DatabaseOptions database, string logsDirectory, string backupFolder)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(database);

        Version = version;
        Location = location;
        LogsDirectory = logsDirectory;
        BackupFolder = backupFolder;
        DatabaseFile = database.Provider == DatabaseProvider.Sqlite ? AppDataLocation.SqliteFilePath(database.ConnectionString) : null;
        Lines =
        [
            new("Phiên bản", version),
            new("Môi trường", runtime),
            new("Thư mục chương trình", appDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            new("Thư mục dữ liệu", $"{location.DataDirectory} – {location.ModeText}"),
            new("Cơ sở dữ liệu", DescribeDatabase(database)),
            new("Nhật ký (log)", logsDirectory),
            new("Sao lưu", backupFolder),
            new("Cấu hình riêng", File.Exists(location.LocalSettingsPath)
                ? location.LocalSettingsPath
                : $"Chưa có ({AppDataLocation.LocalSettingsFileName} trong thư mục dữ liệu)"),
        ];
    }

    public string Version { get; }

    public AppDataLocation Location { get; }

    public string LogsDirectory { get; }

    public string BackupFolder { get; }

    /// <summary>Full path of the SQLite file; null for SQL Server or an in-memory database.</summary>
    public string? DatabaseFile { get; }

    public IReadOnlyList<SupportInfoLine> Lines { get; }

    /// <summary>Plain text for "Sao chép thông tin" (pasted into Zalo/e-mail for support).</summary>
    public string ToClipboardText() =>
        "GroceryPOS – Thông tin phần mềm" + Environment.NewLine +
        string.Join(Environment.NewLine, Lines.Select(l => $"{l.Label}: {l.Value}"));

    /// <summary>Provider plus file (SQLite) or server/database with the password masked (SQL Server).</summary>
    public static string DescribeDatabase(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Provider == DatabaseProvider.Sqlite)
        {
            return $"SQLite · {AppDataLocation.SqliteFilePath(database.ConnectionString) ?? "trong bộ nhớ"}";
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(database.ConnectionString);
            var auth = builder.IntegratedSecurity ? "Windows" : string.IsNullOrEmpty(builder.UserID) ? "không rõ" : $"{builder.UserID} / ****";
            return $"SQL Server · {builder.DataSource} · {builder.InitialCatalog} · đăng nhập: {auth}";
        }
        catch (ArgumentException)
        {
            return "SQL Server · chuỗi kết nối không hợp lệ";
        }
    }
}
