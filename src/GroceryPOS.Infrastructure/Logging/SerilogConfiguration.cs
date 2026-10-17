using System.Reflection;
using GroceryPOS.Application.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GroceryPOS.Infrastructure.Logging;

/// <summary>Builds the application's Serilog logger: rolling daily files (size-capped) plus an error-only file.</summary>
public static class SerilogConfiguration
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({UserName}) {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public const string MainFilePrefix = "grocerypos-";
    public const string ErrorFilePrefix = "errors-";

    /// <param name="currentUser">Resolved for each event, so the logger can be created before the session exists.</param>
    public static Logger CreateLogger(LoggingOptions options, Func<ICurrentUser?>? currentUser = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = options.ResolveDirectory();
        System.IO.Directory.CreateDirectory(directory);

        var level = ParseLevel(options.MinimumLevel, LogEventLevel.Information);
        var frameworkLevel = ParseLevel(options.FrameworkMinimumLevel, LogEventLevel.Warning);
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", frameworkLevel)
            .MinimumLevel.Override("System", frameworkLevel)
            .Enrich.FromLogContext()
            .Enrich.With(new CurrentUserEnricher(currentUser ?? (() => null)))
            .Enrich.WithProperty("Application", "GroceryPOS")
            .Enrich.WithProperty("Version", version)
            .WriteTo.File(
                Path.Combine(directory, MainFilePrefix + ".log"),
                outputTemplate: OutputTemplate,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: Math.Max(1, options.FileSizeLimitMegabytes) * 1024L * 1024L,
                retainedFileCountLimit: Math.Max(1, options.RetainedFileCountLimit),
                shared: true);

        if (options.WriteErrorLog)
        {
            configuration = configuration.WriteTo.File(
                Path.Combine(directory, ErrorFilePrefix + ".log"),
                restrictedToMinimumLevel: LogEventLevel.Error,
                outputTemplate: OutputTemplate,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, options.ErrorRetainedFileCountLimit),
                shared: true);
        }

        return configuration.CreateLogger();
    }

    internal static LogEventLevel ParseLevel(string? value, LogEventLevel fallback) =>
        Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) && Enum.IsDefined(level) ? level : fallback;
}
