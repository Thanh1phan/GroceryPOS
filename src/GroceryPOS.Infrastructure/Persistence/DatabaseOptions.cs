namespace GroceryPOS.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite = 0,
    SqlServer = 1,
}

/// <summary>Bound from the <c>Database</c> section of appsettings.json.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public const string DefaultSqliteConnection = "Data Source=grocerypos.db";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    public string ConnectionString { get; set; } = DefaultSqliteConnection;

    /// <summary>Logs SQL parameter values; development only (may expose customer data in logs).</summary>
    public bool EnableSensitiveDataLogging { get; set; }
}
