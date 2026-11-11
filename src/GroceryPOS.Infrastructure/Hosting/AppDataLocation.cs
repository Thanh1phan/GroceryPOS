using GroceryPOS.Application.Backup;
using GroceryPOS.Infrastructure.Logging;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Sales;
using Microsoft.Data.Sqlite;

namespace GroceryPOS.Infrastructure.Hosting;

/// <summary>How the data folder was chosen (shown on the Thông tin phần mềm dialog).</summary>
public enum AppDataMode
{
    /// <summary><c>%LocalAppData%\GroceryPOS</c> – the default for an installed app (Program Files is read-only).</summary>
    PerUser = 0,

    /// <summary><c>Paths:DataDirectory</c> in appsettings.json or the <c>GROCERYPOS_DATA</c> environment variable.</summary>
    Configured = 1,

    /// <summary>A <c>portable.txt</c> file next to the exe: everything lives in the <c>data</c> folder beside it (USB stick).</summary>
    Portable = 2,
}

/// <summary>Bound from the <c>Paths</c> section of appsettings.json.</summary>
public sealed class PathOptions
{
    public const string SectionName = "Paths";

    /// <summary>Data folder (database, logs, backups, per-machine files); empty = <c>%LocalAppData%\GroceryPOS</c>.</summary>
    public string? DataDirectory { get; set; }
}

/// <summary>
/// The one writable folder of an installation: SQLite database, logs, backups, parked orders, UI preferences and the
/// user-editable <c>appsettings.local.json</c> overlay. The program folder is treated as read-only so the app works when
/// installed under Program Files and an upgrade (which replaces the program folder) never touches the shop's data.
/// Precedence: portable marker → <c>GROCERYPOS_DATA</c> → <c>Paths:DataDirectory</c> → <c>%LocalAppData%\GroceryPOS</c>.
/// </summary>
public sealed class AppDataLocation
{
    public const string FolderName = "GroceryPOS";
    public const string PortableMarkerFileName = "portable.txt";
    public const string PortableDataFolderName = "data";
    public const string EnvironmentVariable = "GROCERYPOS_DATA";
    public const string LocalSettingsFileName = "appsettings.local.json";

    private static readonly string[] SqliteSidecarSuffixes = ["-wal", "-shm"];

    private AppDataLocation(string dataDirectory, AppDataMode mode)
    {
        DataDirectory = dataDirectory;
        Mode = mode;
    }

    public string DataDirectory { get; }

    public AppDataMode Mode { get; }

    public string ModeText => Mode switch
    {
        AppDataMode.Portable => "Bản di động (dữ liệu cạnh file chạy)",
        AppDataMode.Configured => "Thư mục được cấu hình",
        _ => "Theo người dùng Windows",
    };

    /// <summary>Optional settings overlay that survives upgrades (applied after the appsettings.json shipped with the app).</summary>
    public string LocalSettingsPath => Path.Combine(DataDirectory, LocalSettingsFileName);

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public string BackupsDirectory => Path.Combine(DataDirectory, "Backups");

    public string ParkedOrdersPath => Path.Combine(DataDirectory, "parked-orders.json");

    public string UiPreferencesPath => Path.Combine(DataDirectory, "ui-preferences.json");

    /// <summary>Resolves the data folder for the running process (reads the environment and the portable marker).</summary>
    public static AppDataLocation ForCurrentProcess(string? configuredDirectory) =>
        Resolve(
            AppContext.BaseDirectory,
            configuredDirectory,
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Pure resolution, see the class remarks for the precedence. Relative folders are taken from <paramref name="appDirectory"/>.</summary>
    public static AppDataLocation Resolve(string appDirectory, string? configuredDirectory, string? environmentDirectory, string? localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);

        if (File.Exists(Path.Combine(appDirectory, PortableMarkerFileName)))
        {
            return new AppDataLocation(Full(appDirectory, PortableDataFolderName), AppDataMode.Portable);
        }

        foreach (var candidate in new[] { environmentDirectory, configuredDirectory })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return new AppDataLocation(Full(appDirectory, Environment.ExpandEnvironmentVariables(candidate.Trim())), AppDataMode.Configured);
            }
        }

        return string.IsNullOrWhiteSpace(localAppData)
            ? new AppDataLocation(Full(appDirectory, PortableDataFolderName), AppDataMode.PerUser) // service accounts without a profile
            : new AppDataLocation(Full(localAppData, FolderName), AppDataMode.PerUser);
    }

    /// <summary>
    /// Makes a relative SQLite <c>Data Source</c> absolute under <see cref="DataDirectory"/> (the shipped default
    /// <c>Data Source=grocerypos.db</c> used to land in whatever the working directory was). Absolute paths (environment
    /// variables expanded), in-memory and <c>file:</c> URI databases are returned unchanged.
    /// </summary>
    public string ResolveSqliteConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (RelativeDataSource(builder) is not { } relative)
        {
            var expanded = Environment.ExpandEnvironmentVariables(builder.DataSource);
            if (expanded == builder.DataSource)
            {
                return connectionString;
            }

            builder.DataSource = expanded;
            return builder.ToString();
        }

        builder.DataSource = Full(DataDirectory, relative);
        return builder.ToString();
    }

    /// <summary>
    /// Points every per-installation file at the data folder: a relative SQLite database, and – when they are not set
    /// in appsettings – logs, backups and parked orders. Relative folders configured for them are taken from the data
    /// folder as well. SQL Server connection strings are left alone.
    /// </summary>
    public void ApplyTo(DatabaseOptions database, LoggingOptions logging, ParkedOrderOptions parkedOrders, BackupOptions backup)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(parkedOrders);
        ArgumentNullException.ThrowIfNull(backup);

        if (database.Provider == DatabaseProvider.Sqlite)
        {
            database.ConnectionString = ResolveSqliteConnectionString(
                string.IsNullOrWhiteSpace(database.ConnectionString) ? DatabaseOptions.DefaultSqliteConnection : database.ConnectionString);
        }

        logging.Directory = InData(logging.Directory, LogsDirectory);
        backup.Folder = InData(backup.Folder, BackupsDirectory);
        parkedOrders.FilePath = InData(parkedOrders.FilePath, ParkedOrdersPath);
    }

    /// <summary>
    /// Where earlier versions put the database for this connection string (relative to the program folder), or null
    /// when the data source is absolute / in memory.
    /// </summary>
    public static string? LegacySqlitePath(string connectionString, string legacyBaseDirectory)
    {
        var relative = RelativeDataSource(new SqliteConnectionStringBuilder(connectionString));
        return relative is null ? null : Full(legacyBaseDirectory, relative);
    }

    /// <summary>
    /// One-time move to the data folder: copies <paramref name="legacyPath"/> (plus its <c>-wal</c>/<c>-shm</c> files)
    /// to <paramref name="targetPath"/> when the old file exists and the new one does not. The old file is left in place
    /// as a fallback. Returns true when a copy was made.
    /// </summary>
    public static bool CopyLegacyDatabase(string? legacyPath, string targetPath)
    {
        if (string.IsNullOrEmpty(legacyPath)
            || !File.Exists(legacyPath)
            || File.Exists(targetPath)
            || string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        foreach (var suffix in SqliteSidecarSuffixes)
        {
            if (File.Exists(legacyPath + suffix))
            {
                File.Copy(legacyPath + suffix, targetPath + suffix, overwrite: true);
            }
        }

        // The main file goes last and through a temp name: its presence is what marks the move as done.
        var temp = targetPath + ".moving";
        File.Copy(legacyPath, temp, overwrite: true);
        File.Move(temp, targetPath);
        return true;
    }

    /// <summary>Absolute SQLite file path of a connection string, or null for in-memory / URI databases.</summary>
    public static string? SqliteFilePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return IsFileDatabase(builder) ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(builder.DataSource)) : null;
    }

    /// <summary>Creates the data folder; throws <see cref="UnauthorizedAccessException"/>/<see cref="IOException"/> when it is not writable.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        var probe = Path.Combine(DataDirectory, $".write-test-{Environment.ProcessId}");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }

    private string InData(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : Full(DataDirectory, Environment.ExpandEnvironmentVariables(configured.Trim()));

    private static string? RelativeDataSource(SqliteConnectionStringBuilder builder)
    {
        if (!IsFileDatabase(builder))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(builder.DataSource);
        return Path.IsPathRooted(expanded) ? null : expanded;
    }

    private static bool IsFileDatabase(SqliteConnectionStringBuilder builder) =>
        !string.IsNullOrWhiteSpace(builder.DataSource)
        && builder.Mode != SqliteOpenMode.Memory
        && !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
        && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static string Full(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
}
