using GroceryPOS.App.Forms;
using GroceryPOS.Application;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Infrastructure;
using GroceryPOS.Infrastructure.Logging;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.UI.Controls.Feedback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GroceryPOS.App;

internal static class Program
{
    // Synchronous on purpose: with "async Task Main" the code after the first await runs on a thread-pool (MTA)
    // thread, so the forms would be created off the [STAThread] UI thread (clipboard, drag & drop and dialogs break).
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var logging = configuration.GetSection(LoggingOptions.SectionName).Get<LoggingOptions>() ?? new LoggingOptions();
        var seed = configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
        var store = configuration.GetSection(StoreProfile.SectionName).Get<StoreProfile>() ?? StoreProfile.Default;

        var services = new ServiceCollection();
        services.AddGroceryLogging(logging);
        services.AddInfrastructure(database, seed);
        services.AddApplication(store: store);
        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            initializer.InitializeAsync().GetAwaiter().GetResult();
        }

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var currentUser = provider.GetRequiredService<ICurrentUser>();
        var logger = provider.GetRequiredService<Serilog.ILogger>();
        Log.Logger = logger; // static Log for UI code that has no injected logger (e.g. printing)

        void ReportUnhandled(Exception exception, bool onUiThread)
        {
            logger.Error(exception, "Lỗi không xử lý được");
            const string message = "Đã xảy ra lỗi ngoài dự kiến. Chi tiết đã được ghi vào log; nếu lỗi lặp lại, hãy gửi nội dung bên dưới (Ctrl+C để sao chép) cho bộ phận hỗ trợ.";
            if (onUiThread)
            {
                try
                {
                    ModernDialog.Error(System.Windows.Forms.Form.ActiveForm, $"{message}\n\n{exception.Message}", "GroceryPOS – Lỗi", exception.ToString());
                    return;
                }
                catch (Exception dialogFailure)
                {
                    // The themed dialog itself failed (e.g. a broken UI state) – fall back to the system box.
                    logger.Error(dialogFailure, "Không hiển thị được hộp thoại lỗi");
                }
            }

            MessageBox.Show(
                $"Đã xảy ra lỗi ngoài dự kiến:\n{exception.Message}\n\nChi tiết đã được ghi vào log.",
                "GroceryPOS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        System.Windows.Forms.Application.ThreadException += (_, e) => ReportUnhandled(e.Exception, onUiThread: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                ReportUnhandled(exception, onUiThread: false);
            }
        };

        while (true)
        {
            using var login = new LoginForm(scopeFactory);
            if (login.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (currentUser.Session is { MustChangePassword: true })
            {
                using var changePassword = new ChangePasswordForm(scopeFactory, mandatory: true);
                if (changePassword.ShowDialog() != DialogResult.OK)
                {
                    continue;
                }
            }

            using var main = new MainForm(scopeFactory, currentUser);
            System.Windows.Forms.Application.Run(main);
        }
    }
}
