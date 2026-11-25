using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Dashboard;

/// <summary>Screens the dashboard can open (quick actions, F2, a low-stock row).</summary>
public enum DashboardShortcut
{
    Pos,
    Purchases,
    Inventory,
}

/// <summary>The Tổng quan screen as seen by its presenter.</summary>
public interface IDashboardView : IView
{
    /// <summary>False when the host gave the screen no way to open other screens (shortcuts stay hidden).</summary>
    bool CanNavigate { get; }

    /// <summary>Which quick-action buttons to show, and whether the shift card is visible.</summary>
    void ShowLayout(IReadOnlySet<DashboardShortcut> shortcuts, bool showShiftCard);

    void ShowHeader(DashboardHeader header);

    void ShowBoard(DashboardBoard board);

    void ShowShiftCard(DashboardCard card);

    /// <summary>Opens the screen of <paramref name="shortcut"/> (selects its menu item).</summary>
    void Navigate(DashboardShortcut shortcut);
}

/// <summary>
/// Tổng quan logic (<c>dashboard.view</c>): loads today's figures and the user's drawer with "latest wins", refreshes
/// every minute (skipped while a load is running), keeps the last good figures when a refresh fails – an automatic
/// refresh only turns the subtitle amber, F5 also shows an error toast – and opens other screens only with permission.
/// </summary>
public sealed class DashboardPresenter : Presenter<IDashboardView>
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private long _loadVersion;
    private int _running;
    private DashboardBoard? _board;
    private DateTime? _lastSuccess;

    public DashboardPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, TimeProvider clock)
        : base(scopeFactory)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        CanSeeShift = currentUser.HasPermission(Permissions.ShiftsManage) || currentUser.HasPermission(Permissions.ShiftsSupervise);
    }

    public bool CanSeeShift { get; }

    /// <summary>The figures on screen (null before the first successful load).</summary>
    public DashboardBoard? Board => _board;

    /// <summary>True while a load runs; the auto-refresh timer skips its tick then.</summary>
    public bool IsLoading => _running > 0;

    private DateTime Now => _clock.GetLocalNow().DateTime;

    /// <summary>Permission each shortcut needs.</summary>
    public static string PermissionFor(DashboardShortcut shortcut) => shortcut switch
    {
        DashboardShortcut.Pos => Permissions.SalesCreate,
        DashboardShortcut.Purchases => Permissions.PurchasesManage,
        _ => Permissions.InventoryView,
    };

    public bool CanOpen(DashboardShortcut shortcut) => View.CanNavigate && _currentUser.HasPermission(PermissionFor(shortcut));

    protected override void OnAttached()
    {
        var shortcuts = Enum.GetValues<DashboardShortcut>().Where(CanOpen).ToHashSet();
        View.ShowLayout(shortcuts, CanSeeShift);
        View.ShowHeader(DashboardHeader.Loading);
        if (CanSeeShift)
        {
            View.ShowShiftCard(DashboardShiftCard.Loading);
        }
    }

    /// <summary>
    /// Reloads the figures. <paramref name="automatic"/> is the minute timer: skipped while another load runs, and a
    /// failure only marks the subtitle (no toast every minute while the database is unreachable).
    /// </summary>
    public async Task LoadAsync(bool automatic = false)
    {
        if (automatic && IsLoading)
        {
            return;
        }

        var version = ++_loadVersion;
        _running++;
        try
        {
            var (dashboard, shift, offset) = await UseScopeAsync(async sp =>
            {
                var report = await sp.GetRequiredService<IReportService>().GetDashboardAsync().ConfigureAwait(true);
                Result<CurrentShiftDto>? current = CanSeeShift
                    ? await sp.GetRequiredService<IShiftService>().GetCurrentAsync().ConfigureAwait(true)
                    : null;
                var storeOffset = (sp.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
                return (report, current, storeOffset);
            });

            if (!ViewAlive || version != _loadVersion)
            {
                return;
            }

            var now = Now;
            if (dashboard.IsSuccess)
            {
                _board = DashboardBoard.From(dashboard.Value, _currentUser.Session?.FullName, now);
                _lastSuccess = now;
                View.ShowBoard(_board);
            }
            else
            {
                ReportFailure(dashboard.Error.Message, automatic, now);
            }

            if (shift is not null)
            {
                View.ShowShiftCard(shift.IsSuccess
                    ? DashboardShiftCard.From(shift.Value, offset, _clock.GetUtcNow())
                    : DashboardShiftCard.Failed);
            }
        }
        finally
        {
            _running--;
        }
    }

    /// <summary>Opens <paramref name="shortcut"/> when the user may; otherwise does nothing (the button is hidden anyway).</summary>
    public bool Open(DashboardShortcut shortcut)
    {
        if (!CanOpen(shortcut))
        {
            return false;
        }

        View.Navigate(shortcut);
        return true;
    }

    private void ReportFailure(string message, bool automatic, DateTime now)
    {
        if (_board is null || _lastSuccess is not { } last)
        {
            // Nothing to keep: the subtitle carries the error, cards stay "–".
            View.ShowHeader(new DashboardHeader(DashboardHeader.GreetingFor(_currentUser.Session?.FullName, now), message, Tone.Danger));
            if (!automatic)
            {
                View.ToastError(message, "Không tải được tổng quan");
            }

            return;
        }

        // Keep the last good figures and say how old they are.
        View.ShowHeader(_board.Header with
        {
            Subtitle = $"Không cập nhật được lúc {DashboardBoard.Time(now)} · đang hiển thị số liệu lúc {DashboardBoard.Time(last)}",
            SubtitleTone = Tone.Warning,
        });
        if (!automatic)
        {
            View.ToastError(message, "Không cập nhật được tổng quan");
        }
    }
}
