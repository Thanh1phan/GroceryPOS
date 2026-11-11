using System.Reflection;

namespace GroceryPOS.App.Hosting;

/// <summary>Version and build facts of the running app (single source: <c>Version</c> in Directory.Build.props).</summary>
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    /// <summary>"0.9.0" – the informational version without the "+commit" suffix the SDK appends.</summary>
    public static string Version { get; } = ReadVersion();

    public static string Runtime => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";

    public static DateTime BuildTime => File.GetLastWriteTime(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "GroceryPOS.exe"));

    private static readonly Lazy<Icon?> AppIcon = new(() =>
    {
        try
        {
            return Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    /// <summary>The basket icon embedded in the exe (app.ico), for the windows that show in the taskbar.</summary>
    public static Icon? Icon => AppIcon.Value;

    private static string ReadVersion()
    {
        var informational = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
