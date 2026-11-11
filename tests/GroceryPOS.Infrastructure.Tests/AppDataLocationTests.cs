using GroceryPOS.Application.Backup;
using GroceryPOS.Infrastructure.Hosting;
using GroceryPOS.Infrastructure.Logging;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Sales;
using Microsoft.Data.Sqlite;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class AppDataLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "grocerypos-paths-" + Guid.NewGuid().ToString("N"));

    public AppDataLocationTests()
    {
        Directory.CreateDirectory(AppDirectory);
        Directory.CreateDirectory(LocalAppData);
    }

    private string AppDirectory => Path.Combine(_root, "Program Files", "GroceryPOS");

    private string LocalAppData => Path.Combine(_root, "LocalAppData");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Default_is_the_per_user_folder_outside_the_program_folder()
    {
        var location = AppDataLocation.Resolve(AppDirectory, configuredDirectory: null, environmentDirectory: null, LocalAppData);

        Assert.Equal(AppDataMode.PerUser, location.Mode);
        Assert.Equal(Path.Combine(LocalAppData, "GroceryPOS"), location.DataDirectory);
        Assert.Equal(Path.Combine(LocalAppData, "GroceryPOS", "appsettings.local.json"), location.LocalSettingsPath);
        Assert.Equal(Path.Combine(LocalAppData, "GroceryPOS", "Backups"), location.BackupsDirectory);
    }

    [Fact]
    public void Portable_marker_wins_over_everything_else()
    {
        File.WriteAllText(Path.Combine(AppDirectory, AppDataLocation.PortableMarkerFileName), string.Empty);

        var location = AppDataLocation.Resolve(AppDirectory, "D:\\Shop", "E:\\Other", LocalAppData);

        Assert.Equal(AppDataMode.Portable, location.Mode);
        Assert.Equal(Path.Combine(AppDirectory, "data"), location.DataDirectory);
    }

    [Fact]
    public void Environment_variable_beats_the_configured_folder_and_relative_paths_use_the_program_folder()
    {
        var fromEnvironment = AppDataLocation.Resolve(AppDirectory, "configured", "from-env", LocalAppData);
        var fromConfig = AppDataLocation.Resolve(AppDirectory, "  configured  ", "   ", LocalAppData);

        Assert.Equal(AppDataMode.Configured, fromEnvironment.Mode);
        Assert.Equal(Path.Combine(AppDirectory, "from-env"), fromEnvironment.DataDirectory);
        Assert.Equal(Path.Combine(AppDirectory, "configured"), fromConfig.DataDirectory);
    }

    [Fact]
    public void Without_a_user_profile_the_data_folder_sits_beside_the_program()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, localAppData: "");

        Assert.Equal(Path.Combine(AppDirectory, "data"), location.DataDirectory);
    }

    [Fact]
    public void Relative_sqlite_file_moves_into_the_data_folder_others_stay()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);
        var absolute = Path.Combine(_root, "elsewhere", "shop.db");

        var resolved = location.ResolveSqliteConnectionString("Data Source=grocerypos.db;Cache=Shared");

        Assert.Equal(Path.Combine(location.DataDirectory, "grocerypos.db"), new SqliteConnectionStringBuilder(resolved).DataSource);
        Assert.Equal(SqliteCacheMode.Shared, new SqliteConnectionStringBuilder(resolved).Cache);
        Assert.Equal($"Data Source={absolute}", location.ResolveSqliteConnectionString($"Data Source={absolute}"));
        Assert.Equal("Data Source=:memory:", location.ResolveSqliteConnectionString("Data Source=:memory:"));
        Assert.Equal("Data Source=pos;Mode=Memory;Cache=Shared", location.ResolveSqliteConnectionString("Data Source=pos;Mode=Memory;Cache=Shared"));
        Assert.Null(AppDataLocation.SqliteFilePath("Data Source=:memory:"));
    }

    [Fact]
    public void ApplyTo_points_unset_paths_at_the_data_folder_and_keeps_explicit_ones()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);
        var database = new DatabaseOptions();
        var logging = new LoggingOptions();
        var parked = new ParkedOrderOptions();
        var backup = new BackupOptions { Folder = Path.Combine(_root, "USB") };

        location.ApplyTo(database, logging, parked, backup);

        Assert.Equal(Path.Combine(location.DataDirectory, "grocerypos.db"), AppDataLocation.SqliteFilePath(database.ConnectionString));
        Assert.Equal(location.LogsDirectory, logging.ResolveDirectory());
        Assert.Equal(location.ParkedOrdersPath, parked.ResolveFilePath());
        Assert.Equal(Path.Combine(_root, "USB"), backup.ResolveFolder());

        var relative = new LoggingOptions { Directory = "nhat-ky" };
        location.ApplyTo(new DatabaseOptions(), relative, new ParkedOrderOptions(), new BackupOptions());
        Assert.Equal(Path.Combine(location.DataDirectory, "nhat-ky"), relative.ResolveDirectory());
    }

    [Fact]
    public void ApplyTo_leaves_sql_server_connection_strings_alone()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);
        const string connection = "Server=.\\SQLEXPRESS;Database=GroceryPOS;Trusted_Connection=True;TrustServerCertificate=True";
        var database = new DatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = connection };

        location.ApplyTo(database, new LoggingOptions(), new ParkedOrderOptions(), new BackupOptions());

        Assert.Equal(connection, database.ConnectionString);
    }

    [Fact]
    public void Legacy_database_beside_the_exe_is_copied_once_with_its_journal()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);
        var legacy = AppDataLocation.LegacySqlitePath(DatabaseOptions.DefaultSqliteConnection, AppDirectory);
        Assert.Equal(Path.Combine(AppDirectory, "grocerypos.db"), legacy);
        File.WriteAllText(legacy!, "old data");
        File.WriteAllText(legacy + "-wal", "journal");
        var target = Path.Combine(location.DataDirectory, "grocerypos.db");

        Assert.True(AppDataLocation.CopyLegacyDatabase(legacy, target));
        Assert.Equal("old data", File.ReadAllText(target));
        Assert.Equal("journal", File.ReadAllText(target + "-wal"));
        Assert.True(File.Exists(legacy)); // kept as a fallback

        File.WriteAllText(legacy!, "changed later");
        Assert.False(AppDataLocation.CopyLegacyDatabase(legacy, target)); // never overwrites the live database
        Assert.Equal("old data", File.ReadAllText(target));
        Assert.False(AppDataLocation.CopyLegacyDatabase(Path.Combine(_root, "missing.db"), Path.Combine(_root, "new.db")));
        Assert.Null(AppDataLocation.LegacySqlitePath($"Data Source={target}", AppDirectory));
    }

    [Fact]
    public void EnsureCreated_creates_a_writable_folder()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);

        location.EnsureCreated();

        Assert.True(Directory.Exists(location.DataDirectory));
        Assert.Empty(Directory.EnumerateFiles(location.DataDirectory));
    }

    [Fact]
    public void Support_info_lists_paths_and_masks_sql_server_passwords()
    {
        var location = AppDataLocation.Resolve(AppDirectory, null, null, LocalAppData);
        var database = new DatabaseOptions();
        var logging = new LoggingOptions();
        var backup = new BackupOptions();
        location.ApplyTo(database, logging, new ParkedOrderOptions(), backup);

        var info = new SupportInfo("0.9.0", ".NET 8", AppDirectory + Path.DirectorySeparatorChar, location, database, logging.ResolveDirectory(), backup.ResolveFolder());

        Assert.Equal(Path.Combine(location.DataDirectory, "grocerypos.db"), info.DatabaseFile);
        Assert.Equal("Phiên bản", info.Lines[0].Label);
        Assert.Contains(info.Lines, l => l.Label == "Thư mục chương trình" && l.Value == AppDirectory);
        Assert.Contains("Chưa có", info.Lines.Single(l => l.Label == "Cấu hình riêng").Value);
        Assert.StartsWith("GroceryPOS – Thông tin phần mềm", info.ToClipboardText());
        Assert.Contains($"Sao lưu: {location.BackupsDirectory}", info.ToClipboardText());

        var sqlServer = SupportInfo.DescribeDatabase(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=pos-server;Database=Shop;User Id=sa;Password=Secret#123;TrustServerCertificate=True",
        });
        Assert.Equal("SQL Server · pos-server · Shop · đăng nhập: sa / ****", sqlServer);
        Assert.DoesNotContain("Secret", sqlServer);
        Assert.Contains("Windows", SupportInfo.DescribeDatabase(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=.;Database=Shop;Integrated Security=True",
        }));
    }
}
