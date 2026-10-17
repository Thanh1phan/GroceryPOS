namespace GroceryPOS.Infrastructure.Logging;

/// <summary>Bound from the <c>AppLogging</c> section of appsettings.json.</summary>
public sealed class LoggingOptions
{
    public const string SectionName = "AppLogging";

    /// <summary>Log folder; relative paths are resolved against the app folder. Default: %LocalAppData%\GroceryPOS\logs.</summary>
    public string? Directory { get; set; }

    /// <summary>Verbose, Debug, Information, Warning, Error or Fatal.</summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>Level for framework sources (Microsoft.*, System.*) – EF Core is very chatty below Warning.</summary>
    public string FrameworkMinimumLevel { get; set; } = "Warning";

    /// <summary>Daily files kept for the main log (grocerypos-yyyyMMdd.log).</summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>A day's file rolls over to <c>_001</c>, <c>_002</c>… when it reaches this size.</summary>
    public int FileSizeLimitMegabytes { get; set; } = 10;

    /// <summary>Also writes Error/Fatal events to errors-yyyyMMdd.log (kept 90 days) for quick troubleshooting.</summary>
    public bool WriteErrorLog { get; set; } = true;

    public int ErrorRetainedFileCountLimit { get; set; } = 90;

    public string ResolveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Directory))
        {
            return Path.IsPathRooted(Directory) ? Directory : Path.Combine(AppContext.BaseDirectory, Directory);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(localAppData)
            ? Path.Combine(AppContext.BaseDirectory, "logs")
            : Path.Combine(localAppData, "GroceryPOS", "logs");
    }
}
