using FluentValidation;
using GroceryPOS.Application;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Infrastructure.Logging;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class CompositionAndLoggingTests : IDisposable
{
    private readonly string _logDir = Path.Combine(Path.GetTempPath(), "grocerypos-tests", Guid.NewGuid().ToString("N"));
    private readonly string _dbFile;

    public CompositionAndLoggingTests()
    {
        Directory.CreateDirectory(_logDir);
        _dbFile = Path.Combine(_logDir, "app.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_logDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a file handle may still be closing on some platforms.
        }
    }

    private ServiceProvider BuildApp() => new ServiceCollection()
        .AddApplication()
        .AddInfrastructure(new DatabaseOptions { ConnectionString = $"Data Source={_dbFile}" })
        .AddSingleton<IPasswordHasher>(new BCryptPasswordHasher(workFactor: 4))
        .AddGroceryLogging(new LoggingOptions { Directory = _logDir })
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private string ReadLog(string prefix)
    {
        var file = Assert.Single(Directory.GetFiles(_logDir, prefix + "*.log"));
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream).ReadToEnd();
    }

    [Fact]
    public void Container_resolves_every_use_case_and_validator()
    {
        using var app = BuildApp();
        using var scope = app.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IAuthService>());
        Assert.NotNull(sp.GetRequiredService<ICheckoutService>());
        Assert.NotNull(sp.GetRequiredService<IPosLookupService>());
        Assert.NotNull(sp.GetRequiredService<IReturnService>());
        Assert.NotNull(sp.GetRequiredService<IShiftService>());
        Assert.NotNull(sp.GetRequiredService<IInventoryService>());
        Assert.NotNull(sp.GetRequiredService<IGoodsReceiptService>());
        Assert.NotNull(sp.GetRequiredService<ISupplierPaymentService>());
        Assert.NotNull(sp.GetRequiredService<ISupplierReturnService>());
        Assert.NotNull(sp.GetRequiredService<IReportService>());
        Assert.NotNull(sp.GetRequiredService<IValidator<CheckoutRequest>>());
        Assert.NotNull(sp.GetRequiredService<IValidator<CloseShiftRequest>>());
        Assert.NotNull(sp.GetRequiredService<IValidator<ChangePasswordInput>>());

        // One desktop session shared by everything that asks for the current user.
        Assert.Same(app.GetRequiredService<IUserSessionStore>(), sp.GetRequiredService<ICurrentUser>());
    }

    [Fact]
    public async Task Login_through_the_container_signs_in_and_logs_with_the_user_name()
    {
        using (var app = BuildApp())
        {
            using (var scope = app.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            }

            using (var scope = app.CreateScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                Assert.True((await auth.LoginAsync("admin", "sai-mat-khau")).IsFailure);
                Assert.True((await auth.LoginAsync("admin", "admin123")).IsSuccess);
            }

            Assert.Equal("admin", app.GetRequiredService<ICurrentUser>().Session?.Username);
            app.GetRequiredService<ILogger<CompositionAndLoggingTests>>().LogError("Kiểm tra lỗi {Code}", 42);
        } // disposing the container flushes the file sinks

        var log = ReadLog(SerilogConfiguration.MainFilePrefix);
        Assert.Contains("[WRN] (-) GroceryPOS.Application.Authentication.AuthService: Login failed for admin: wrong password", log);
        Assert.Contains("[INF] (admin) GroceryPOS.Application.Authentication.AuthService: User admin signed in as Admin", log);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", log); // framework noise filtered below Warning

        var errors = ReadLog(SerilogConfiguration.ErrorFilePrefix);
        Assert.Contains("[ERR] (admin)", errors);
        Assert.Contains("Kiểm tra lỗi 42", errors);
        Assert.DoesNotContain("signed in", errors);
    }

    [Fact]
    public void Logger_honours_minimum_level_and_can_skip_error_file()
    {
        using (var logger = SerilogConfiguration.CreateLogger(new LoggingOptions
        {
            Directory = _logDir, MinimumLevel = "Warning", WriteErrorLog = false,
        }))
        {
            logger.Information("bỏ qua");
            logger.Warning("ghi lại");
        }

        var log = ReadLog(SerilogConfiguration.MainFilePrefix);
        Assert.Contains("ghi lại", log);
        Assert.DoesNotContain("bỏ qua", log);
        Assert.Empty(Directory.GetFiles(_logDir, SerilogConfiguration.ErrorFilePrefix + "*"));
    }

    [Theory]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("nonsense", LogEventLevel.Information)]
    [InlineData(null, LogEventLevel.Information)]
    [InlineData("99", LogEventLevel.Information)]
    public void Level_parsing_falls_back_on_bad_values(string? value, LogEventLevel expected) =>
        Assert.Equal(expected, SerilogConfiguration.ParseLevel(value, LogEventLevel.Information));

    [Fact]
    public void Relative_log_directory_is_resolved_against_the_app_folder()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "logs"), new LoggingOptions { Directory = "logs" }.ResolveDirectory());
        Assert.EndsWith(Path.Combine("GroceryPOS", "logs"), new LoggingOptions().ResolveDirectory());
    }
}
