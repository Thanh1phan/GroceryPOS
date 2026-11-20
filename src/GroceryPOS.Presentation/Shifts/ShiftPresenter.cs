using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Shifts;

/// <summary>The Ca làm việc screen as seen by its presenter.</summary>
public interface IShiftView : IView
{
    void ShowCurrent(CurrentShiftState state);

    void ShowHistory(IReadOnlyList<ShiftRow> rows);

    /// <summary>Marks the "Từ" date as invalid (or clears it with null).</summary>
    void ShowPeriodError(string? message);

    /// <summary>Mở ca dialog; the opened shift, or null when the user backed out.</summary>
    ShiftSummaryDto? OpenShift(decimal suggestedFloat, string? cashierName);

    /// <summary>Nộp / chi tiền dialog; the updated shift, or null when the user backed out.</summary>
    ShiftSummaryDto? RecordCashMovement(ShiftSummaryDto shift, CashMovementType type);

    /// <summary>Kết ca count sheet; the frozen report of the closed shift, or null when the user backed out.</summary>
    ShiftSummaryDto? CloseShift(ShiftSummaryDto shift, bool closingForSomeoneElse);

    /// <summary>Print preview of a mid-shift or closing report.</summary>
    void ShowReport(ShiftSummaryDto shift);
}

/// <summary>
/// Ca làm việc logic: the signed-in user's drawer (open, pay-in/out, mid-shift report, close) and the shift history
/// with "latest wins" loading. Supervisors (<c>shifts.supervise</c>) see everyone's shifts and can close another
/// cashier's open shift (Kết ca hộ). Every drawer action re-reads the shift first, so a shift closed on another
/// machine is reported instead of acting on stale numbers.
/// </summary>
public sealed class ShiftPresenter : Presenter<IShiftView>
{
    public const string Title = "Ca làm việc";

    private readonly int? _userId;
    private readonly string? _cashierName;
    private ShiftHistoryFilter _filter = ShiftHistoryFilter.LastSevenDays(DateTime.Today);
    private long _historyVersion;
    private long _currentVersion;

    public ShiftPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanSupervise = currentUser.HasPermission(Permissions.ShiftsSupervise);
        _userId = currentUser.Session?.UserId;
        _cashierName = currentUser.Session?.FullName;
    }

    /// <summary>Sees every cashier's shifts and may close them (Kết ca hộ).</summary>
    public bool CanSupervise { get; }

    public string HistoryTitle => CanSupervise ? "Lịch sử ca (tất cả nhân viên)" : "Lịch sử ca của bạn";

    public ShiftHistoryFilter Filter => _filter;

    /// <summary>The user's drawer as last loaded (null before the first load or after a failure).</summary>
    public CurrentShiftDto? Current { get; private set; }

    public IReadOnlyList<ShiftRow> History { get; private set; } = [];

    public bool CanViewReport(ShiftRow? row) => row is not null;

    public bool CanCloseForOther(ShiftRow? row) => CanSupervise && row is { IsOpen: true } && row.UserId != _userId;

    /// <summary>Reloads the current shift and the history with <paramref name="filter"/>.</summary>
    public async Task LoadAsync(ShiftHistoryFilter? filter = null)
    {
        await ReloadCurrentAsync();
        await FilterAsync(filter ?? _filter);
    }

    /// <summary>Reloads the history only (a filter changed).</summary>
    public async Task FilterAsync(ShiftHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filter = filter;
        var from = (filter.From ?? DateTime.Today.AddDays(-6)).Date;
        var to = (filter.To ?? DateTime.Today).Date.AddDays(1);
        if (from >= to)
        {
            ++_historyVersion; // an older load in flight must not overwrite the error state
            View.ShowPeriodError("Từ ngày phải trước đến ngày.");
            return;
        }

        View.ShowPeriodError(null);
        var version = ++_historyVersion;
        var loaded = await UseScopeAsync(async sp =>
        {
            var offset = StoreOffset(sp);
            var result = await sp.GetRequiredService<IShiftService>().ListAsync(new ShiftFilter(
                From: new DateTimeOffset(from, offset),
                To: new DateTimeOffset(to, offset),
                Status: filter.OnlyOpen ? ShiftStatus.Open : null));
            return (offset, result);
        });
        if (version != _historyVersion || !ViewAlive)
        {
            return;
        }

        if (loaded.result.IsFailure)
        {
            View.ToastError($"Không tải được lịch sử ca: {loaded.result.Error.Message}", Title);
            return;
        }

        History = loaded.result.Value.Select(s => ShiftRow.From(s, loaded.offset)).ToList();
        View.ShowHistory(History);
    }

    /// <summary>Mở ca: refuses (and shows it) when a shift was opened meanwhile, e.g. from the POS screen.</summary>
    public async Task OpenShiftAsync()
    {
        if (!await ReloadCurrentAsync())
        {
            return;
        }

        if (Current?.Current is { } open)
        {
            View.ShowInfo($"Bạn đã có ca {open.Code} đang mở.", Title);
            return;
        }

        var opened = View.OpenShift(Current?.SuggestedOpeningFloat ?? 0, _cashierName);
        if (opened is null || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess($"Đã mở ca {opened.Code} với tiền đầu ca {Vnd.Format(opened.OpeningFloat)}.", Title);
        await LoadAsync();
    }

    /// <summary>Nộp tiền vào két / Chi tiền: re-reads the drawer first so the pay-out check uses fresh cash.</summary>
    public async Task MoveCashAsync(CashMovementType type)
    {
        if (await OwnOpenShiftAsync() is not { } shift)
        {
            return;
        }

        var updated = View.RecordCashMovement(shift, type);
        if (updated is null || !ViewAlive)
        {
            return;
        }

        var movement = updated.CashMovements.Where(m => m.Type == type).MaxBy(m => m.Timestamp);
        var verb = type == CashMovementType.PayIn ? "nộp" : "chi";
        View.ToastSuccess(
            movement is null
                ? $"Đã ghi phiếu {verb} tiền. Tiền mặt trong két: {Vnd.Format(updated.ExpectedCash)}."
                : $"Đã {verb} {Vnd.Format(movement.Amount)} ({movement.Reason}). Tiền mặt trong két: {Vnd.Format(updated.ExpectedCash)}.",
            Title);
        await ReloadCurrentAsync();
    }

    /// <summary>Báo cáo giữa ca with live numbers.</summary>
    public async Task ShowCurrentReportAsync()
    {
        if (await OwnOpenShiftAsync() is { } shift)
        {
            View.ShowReport(shift);
        }
    }

    /// <summary>Kết ca for the signed-in user's own shift.</summary>
    public async Task CloseOwnShiftAsync()
    {
        if (await OwnOpenShiftAsync() is { } shift)
        {
            await CloseAsync(shift, closingForSomeoneElse: false);
        }
    }

    /// <summary>Kết ca hộ: a supervisor closes another cashier's open shift picked in the history.</summary>
    public async Task CloseForOtherAsync(ShiftRow? row)
    {
        if (row is null || !row.IsOpen || row.UserId == _userId)
        {
            return;
        }

        if (!CanSupervise)
        {
            View.ShowWarning("Bạn không có quyền kết ca của nhân viên khác.", "Kết ca hộ");
            return;
        }

        var result = await UseAsync<IShiftService, Result<ShiftSummaryDto>>(s => s.GetSummaryAsync(row.ShiftId));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, "Kết ca hộ");
            return;
        }

        if (result.Value.Status != ShiftStatus.Open)
        {
            View.ShowInfo($"Ca {row.Code} của {row.UserName} đã được kết.", "Kết ca hộ");
            await FilterAsync(_filter);
            return;
        }

        await CloseAsync(result.Value, closingForSomeoneElse: true);
    }

    /// <summary>Xem báo cáo (Enter on a history row): the frozen report of a closed shift or the live one of an open shift.</summary>
    public async Task ShowReportAsync(ShiftRow? row)
    {
        if (row is null)
        {
            return;
        }

        var result = await UseAsync<IShiftService, Result<ShiftSummaryDto>>(s => s.GetSummaryAsync(row.ShiftId));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, "Báo cáo ca");
            return;
        }

        View.ShowReport(result.Value);
    }

    /// <summary>Text of the "print the closing report?" question, with the cash variance.</summary>
    public static string ClosedMessage(ShiftSummaryDto closed)
    {
        ArgumentNullException.ThrowIfNull(closed);
        var variance = closed.Variance switch
        {
            null => string.Empty,
            0 => " Tiền mặt khớp.",
            < 0 => $" Thiếu {Vnd.Format(-closed.Variance.Value)}.",
            _ => $" Thừa {Vnd.Format(closed.Variance.Value)}.",
        };
        return $"Đã kết ca {closed.Code}.{variance} In báo cáo kết ca?";
    }

    private async Task CloseAsync(ShiftSummaryDto shift, bool closingForSomeoneElse)
    {
        var closed = View.CloseShift(shift, closingForSomeoneElse);
        if (closed is null || !ViewAlive)
        {
            return;
        }

        await LoadAsync();
        if (ViewAlive && View.Confirm(ClosedMessage(closed), "Kết ca", "In báo cáo"))
        {
            View.ShowReport(closed);
        }
    }

    /// <summary>
    /// Fresh copy of the user's open shift; null after telling the user (load failed, or the shift was closed
    /// elsewhere – e.g. a supervisor's Kết ca hộ – in which case the screen is refreshed).
    /// </summary>
    private async Task<ShiftSummaryDto?> OwnOpenShiftAsync()
    {
        if (!await ReloadCurrentAsync())
        {
            return null;
        }

        if (Current?.Current is { } shift)
        {
            return shift;
        }

        View.ShowWarning("Ca của bạn không còn mở (có thể đã được kết ca hộ). Hãy mở ca mới để tiếp tục.", Title);
        await FilterAsync(_filter);
        return null;
    }

    /// <summary>Reloads the drawer and shows it; false when it failed or a newer reload took over.</summary>
    private async Task<bool> ReloadCurrentAsync()
    {
        var version = ++_currentVersion;
        var loaded = await UseScopeAsync(async sp =>
        {
            var offset = StoreOffset(sp);
            var now = (sp.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            var result = await sp.GetRequiredService<IShiftService>().GetCurrentAsync();
            return (offset, now, result);
        });
        if (version != _currentVersion || !ViewAlive)
        {
            return false;
        }

        if (loaded.result.IsFailure)
        {
            Current = null;
            View.ShowCurrent(CurrentShiftState.Failed(loaded.result.Error.Message));
            return false;
        }

        Current = loaded.result.Value;
        View.ShowCurrent(CurrentShiftState.From(Current, loaded.offset, loaded.now));
        return true;
    }

    private static TimeSpan StoreOffset(IServiceProvider services) =>
        (services.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
}
