using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Ca làm việc: the signed-in user's drawer (open shift, live KPIs, pay-in/out, mid-shift report, close with count sheet)
/// above the shift history. Supervisors (<c>shifts.supervise</c>) see everyone's shifts and can close another cashier's
/// open shift ("Kết ca hộ"); cashiers only see their own.
/// Passive MVP view – the logic lives in <see cref="ShiftPresenter"/>.
/// </summary>
internal sealed class ShiftScreen : BaseUserControl, IShiftView
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ShiftPresenter _presenter;
    private readonly SalesOptions _options;

    private readonly ModernBadge _status;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly StatCard _revenue;
    private readonly StatCard _invoices;
    private readonly StatCard _cash;
    private readonly StatCard _nonCash;
    private readonly ModernButton _open;
    private readonly ModernButton _payIn;
    private readonly ModernButton _payOut;
    private readonly ModernButton _report;
    private readonly ModernButton _close;

    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly CheckBox _onlyOpen;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _viewReport;
    private readonly ModernButton _closeForOther;

    public ShiftScreen(IServiceProvider services)
    {
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _options = services.GetService<SalesOptions>() ?? SalesOptions.Default;
        _presenter = services.GetRequiredService<ShiftPresenter>();
        _presenter.Attach(this);

        // ---- Current shift card -------------------------------------------------------------------
        _status = new ModernBadge { Margin = new Padding(0, 6, 12, 0) };
        _title = new Label { AutoSize = true, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), Text = "Ca làm việc" };
        _subtitle = new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0), Text = "Đang tải…" };

        _open = Action("Mở ca", Glyphs.Add, ButtonVariant.Primary, _presenter.OpenShiftAsync);
        _payIn = Action("Nộp tiền vào két", Glyphs.Money, ButtonVariant.Secondary, () => _presenter.MoveCashAsync(CashMovementType.PayIn));
        _payOut = Action("Chi tiền", Glyphs.Money, ButtonVariant.Secondary, () => _presenter.MoveCashAsync(CashMovementType.PayOut));
        _report = Action("Báo cáo giữa ca", Glyphs.Print, ButtonVariant.Secondary, _presenter.ShowCurrentReportAsync);
        _close = Action("Kết ca", Glyphs.Lock, ButtonVariant.Danger, _presenter.CloseOwnShiftAsync);
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        _open.Visible = _payIn.Visible = _payOut.Visible = _report.Visible = _close.Visible = false;

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(24, 12, 24, 4) };
        actions.Controls.AddRange([_open, _payIn, _payOut, _report, _close, refresh]);

        _revenue = Stat("Doanh thu thuần", Glyphs.Chart, BadgeKind.Success);
        _invoices = Stat("Hóa đơn", Glyphs.Document, BadgeKind.Primary);
        _cash = Stat("Tiền mặt trong két", Glyphs.Money, BadgeKind.Info);
        _nonCash = Stat("Chuyển khoản + thẻ", Glyphs.Calculator, BadgeKind.Neutral);
        var stats = new StatCardRow { Padding = new Padding(24, 4, 24, 8) };
        stats.AddCards(_revenue, _invoices, _cash, _nonCash);

        // Badge + title, then the subtitle, flow with their real text widths.
        var heading = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(24, 20, 24, 0) };
        heading.Controls.AddRange([_status, _title]);
        heading.SetFlowBreak(_title, true);
        heading.Controls.Add(_subtitle);

        // ---- History ------------------------------------------------------------------------------
        var period = ShiftHistoryFilter.LastSevenDays(DateTime.Today);
        _from = new ModernDatePicker { Width = 150, Value = period.From, ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 150, Value = period.To, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
        _onlyOpen = new CheckBox { Text = "Chỉ ca đang mở", AutoSize = true, Margin = new Padding(8, 12, 8, 0) };
        _onlyOpen.CheckedChanged += async (_, _) => await FilterChangedAsync();

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có ca nào trong khoảng thời gian này.", SearchPlaceholder = "Tìm mã ca, thu ngân…" };
        _grid.AddTextColumn(nameof(ShiftRow.Code), "Mã ca", width: 130);
        _grid.AddTextColumn(nameof(ShiftRow.UserName), "Thu ngân");
        _grid.AddTextColumn(nameof(ShiftRow.StatusName), "Trạng thái", width: 110);
        _grid.AddDateColumn(nameof(ShiftRow.OpenedAt), "Mở ca", withTime: true);
        _grid.AddDateColumn(nameof(ShiftRow.ClosedAt), "Kết ca", withTime: true);
        _grid.AddMoneyColumn(nameof(ShiftRow.NetRevenue), "Doanh thu thuần", width: 150);
        _grid.AddMoneyColumn(nameof(ShiftRow.Variance), "Chênh lệch", width: 130);
        _grid.RowActivated += async (_, e) => await _presenter.ShowReportAsync((ShiftRow)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateHistoryButtons();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;

        _viewReport = new ModernButton { Text = "Xem báo cáo", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Print, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
        _viewReport.Click += async (_, _) => await _presenter.ShowReportAsync(Selected);
        _closeForOther = new ModernButton { Text = "Kết ca hộ", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Lock, Enabled = false, Visible = _presenter.CanSupervise, Margin = new Padding(8, 0, 0, 0) };
        _closeForOther.Click += async (_, _) => await _presenter.CloseForOtherAsync(Selected);

        _grid.ToolbarItems.Controls.AddRange([
            new Label { Text = "Từ", AutoSize = true, Margin = new Padding(0, 12, 4, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(8, 12, 4, 0) }, _to,
            _onlyOpen, _viewReport, _closeForOther,
        ]);

        var historyCard = new ModernCard { Dock = DockStyle.Fill, Title = _presenter.HistoryTitle };
        historyCard.Controls.Add(_grid);
        var historyHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        historyHost.Controls.Add(historyCard);

        Controls.Add(historyHost);
        Controls.Add(stats);
        Controls.Add(actions);
        Controls.Add(heading);
    }

    bool IView.IsAlive => IsAlive;

    private ShiftRow? Selected => _grid.GetSelected<ShiftRow>();

    public override Task RefreshDataAsync() => _presenter.LoadAsync(CurrentFilter());

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(0, 0, 8, 8) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "–",
        Margin = new Padding(0, 0, 16, 0),
    };

    private ShiftHistoryFilter CurrentFilter() => new(_from.Value, _to.Value, _onlyOpen.Checked);

    private Task FilterChangedAsync() => _presenter.FilterAsync(CurrentFilter());

    private void UpdateHistoryButtons()
    {
        var row = Selected;
        _viewReport.Enabled = _presenter.CanViewReport(row);
        _closeForOther.Enabled = _presenter.CanCloseForOther(row);
    }

    private static BadgeKind BadgeFor(Tone tone) => tone switch
    {
        Tone.Info => BadgeKind.Info,
        Tone.Success => BadgeKind.Success,
        Tone.Warning => BadgeKind.Warning,
        Tone.Danger => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not ShiftRow row || e.CellStyle is not { } style)
        {
            return;
        }

        var tone = _grid.Columns[e.ColumnIndex].DataPropertyName switch
        {
            nameof(ShiftRow.StatusName) => row.StatusTone,
            nameof(ShiftRow.Variance) => row.VarianceTone,
            _ => Tone.Normal,
        };
        var colors = ThemeManager.Current.Colors;
        style.ForeColor = tone switch
        {
            Tone.Muted => colors.TextMuted,
            Tone.Info => colors.Info,
            Tone.Warning => colors.Warning,
            Tone.Danger => colors.Danger,
            Tone.Success => colors.Success,
            _ => style.ForeColor,
        };
    }

    void IShiftView.ShowCurrent(CurrentShiftState state)
    {
        _status.Set(state.BadgeText, BadgeFor(state.BadgeTone));
        _title.Text = state.Title;
        _subtitle.Text = state.Subtitle;
        _open.Visible = state.CanOpen;
        _payIn.Visible = _payOut.Visible = _report.Visible = _close.Visible = state.IsOpen;
        Fill(_revenue, state.Revenue);
        Fill(_invoices, state.Invoices);
        Fill(_cash, state.Cash);
        Fill(_nonCash, state.NonCash);

        static void Fill(StatCard card, ShiftCard value)
        {
            card.Value = value.Value;
            card.Note = value.Note;
        }
    }

    void IShiftView.ShowHistory(IReadOnlyList<ShiftRow> rows)
    {
        _grid.SetItems(rows);
        UpdateHistoryButtons();
    }

    void IShiftView.ShowPeriodError(string? message)
    {
        if (message is null)
        {
            _from.ClearValidation();
        }
        else
        {
            _from.SetError(message);
        }
    }

    ShiftSummaryDto? IShiftView.OpenShift(decimal suggestedFloat, string? cashierName)
    {
        using var dialog = new OpenShiftDialog(_scopeFactory, suggestedFloat, cashierName);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Opened : null;
    }

    ShiftSummaryDto? IShiftView.RecordCashMovement(ShiftSummaryDto shift, CashMovementType type)
    {
        using var dialog = new CashMovementDialog(_scopeFactory, shift, type);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Updated : null;
    }

    ShiftSummaryDto? IShiftView.CloseShift(ShiftSummaryDto shift, bool closingForSomeoneElse)
    {
        using var dialog = new CloseShiftDialog(_scopeFactory, shift, _options, closingForSomeoneElse);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.ClosedShift : null;
    }

    void IShiftView.ShowReport(ShiftSummaryDto shift) => ShiftReportViewer.Show(this, _scopeFactory, shift);

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, cancelText: "Không", danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
