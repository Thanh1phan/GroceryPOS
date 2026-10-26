using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
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
/// </summary>
internal sealed class ShiftScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;
    private readonly SalesOptions _options;
    private readonly bool _canSupervise;

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

    private CurrentShiftDto? _current;

    public ShiftScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _canSupervise = currentUser.HasPermission(Permissions.ShiftsSupervise);
        using (var scope = scopeFactory.CreateScope())
        {
            _options = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
        }

        Dock = DockStyle.Fill;

        // ---- Current shift card -------------------------------------------------------------------
        _status = new ModernBadge { Location = new Point(0, 4) };
        _title = new Label { AutoSize = true, Location = new Point(110, 0), Font = new Font(Font.FontFamily, 14, FontStyle.Bold) };
        _subtitle = new Label { AutoSize = true, Location = new Point(0, 34) };

        _open = Action("Mở ca", Glyphs.Add, ButtonVariant.Primary, async () => await OpenShiftAsync());
        _payIn = Action("Nộp tiền vào két", Glyphs.Money, ButtonVariant.Secondary, async () => await MoveCashAsync(CashMovementType.PayIn));
        _payOut = Action("Chi tiền", Glyphs.Money, ButtonVariant.Secondary, async () => await MoveCashAsync(CashMovementType.PayOut));
        _report = Action("Báo cáo giữa ca", Glyphs.Print, ButtonVariant.Secondary, async () => await ShowCurrentReportAsync());
        _close = Action("Kết ca", Glyphs.Lock, ButtonVariant.Danger, async () => await CloseOwnShiftAsync());
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, async () => await ReloadAsync());

        var actions = new FlowLayoutPanel { Location = new Point(0, 64), AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([_open, _payIn, _payOut, _report, _close, refresh]);

        _revenue = Stat("Doanh thu thuần", Glyphs.Chart, BadgeKind.Success);
        _invoices = Stat("Hóa đơn", Glyphs.Document, BadgeKind.Primary);
        _cash = Stat("Tiền mặt trong két", Glyphs.Money, BadgeKind.Info);
        _nonCash = Stat("Chuyển khoản + thẻ", Glyphs.Calculator, BadgeKind.Neutral);
        var stats = new FlowLayoutPanel { Location = new Point(0, 120), AutoSize = true, WrapContents = false };
        stats.Controls.AddRange([_revenue, _invoices, _cash, _nonCash]);

        var currentPanel = new Panel { Dock = DockStyle.Top, Height = 260, Padding = new Padding(24, 20, 24, 0) };
        var inner = new Panel { Dock = DockStyle.Fill };
        inner.Controls.AddRange([_status, _title, _subtitle, actions, stats]);
        currentPanel.Controls.Add(inner);

        // ---- History ------------------------------------------------------------------------------
        var today = DateTime.Today;
        _from = new ModernDatePicker { Width = 150, Value = today.AddDays(-6), ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 150, Value = today, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await ReloadHistoryAsync();
        _to.ValueChanged += async (_, _) => await ReloadHistoryAsync();
        _onlyOpen = new CheckBox { Text = "Chỉ ca đang mở", AutoSize = true, Margin = new Padding(8, 12, 8, 0) };
        _onlyOpen.CheckedChanged += async (_, _) => await ReloadHistoryAsync();

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có ca nào trong khoảng thời gian này." };
        _grid.AddTextColumn(nameof(ShiftRow.Code), "Mã ca", width: 130);
        _grid.AddTextColumn(nameof(ShiftRow.UserName), "Thu ngân");
        _grid.AddTextColumn(nameof(ShiftRow.StatusName), "Trạng thái", width: 110);
        _grid.AddDateColumn(nameof(ShiftRow.OpenedAt), "Mở ca", withTime: true);
        _grid.AddDateColumn(nameof(ShiftRow.ClosedAt), "Kết ca", withTime: true);
        _grid.AddMoneyColumn(nameof(ShiftRow.NetRevenue), "Doanh thu thuần", width: 150);
        _grid.AddMoneyColumn(nameof(ShiftRow.Variance), "Chênh lệch", width: 130);
        _grid.RowActivated += async (_, e) => await ShowReportAsync(((ShiftRow)e.Item).ShiftId);
        _grid.SelectionChanged += (_, _) => UpdateHistoryButtons();

        _viewReport = new ModernButton { Text = "Xem báo cáo", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Print, Enabled = false };
        _viewReport.Click += async (_, _) =>
        {
            if (_grid.GetSelected<ShiftRow>() is { } row)
            {
                await ShowReportAsync(row.ShiftId);
            }
        };
        _closeForOther = new ModernButton { Text = "Kết ca hộ", Variant = ButtonVariant.Secondary, Enabled = false, Visible = _canSupervise };
        _closeForOther.Click += async (_, _) => await CloseSelectedForOtherAsync();

        _grid.ToolbarItems.Controls.AddRange([
            new Label { Text = "Từ", AutoSize = true, Margin = new Padding(0, 12, 4, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(8, 12, 4, 0) }, _to,
            _onlyOpen, _viewReport, _closeForOther,
        ]);

        var historyCard = new ModernCard { Dock = DockStyle.Fill, Title = _canSupervise ? "Lịch sử ca (tất cả nhân viên)" : "Lịch sử ca của bạn" };
        historyCard.Controls.Add(_grid);
        var historyHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        historyHost.Controls.Add(historyCard);

        Controls.Add(historyHost);
        Controls.Add(currentPanel);

        Load += async (_, _) => await ReloadAsync();
    }

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, Size = new Size(text.Length > 12 ? 170 : 130, 40), Margin = new Padding(0, 0, 8, 0) };
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

    private async Task ReloadAsync()
    {
        await ReloadCurrentAsync();
        await ReloadHistoryAsync();
    }

    private async Task ReloadCurrentAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().GetCurrentAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            _title.Text = "Không tải được ca làm việc";
            _subtitle.Text = result.Error.Message;
            return;
        }

        _current = result.Value;
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        var shift = _current?.Current;
        var isOpen = shift is not null;
        _open.Visible = !isOpen;
        _payIn.Visible = _payOut.Visible = _report.Visible = _close.Visible = isOpen;

        if (shift is null)
        {
            _status.Set("Chưa mở ca", BadgeKind.Neutral);
            _title.Text = "Bạn chưa mở ca";
            _subtitle.Text = _current is { SuggestedOpeningFloat: > 0 } c
                ? $"Ca trước để lại két {Vnd.Format(c.SuggestedOpeningFloat)}. Mở ca để bắt đầu bán hàng."
                : "Mở ca để bắt đầu bán hàng.";
            foreach (var card in new[] { _revenue, _invoices, _cash, _nonCash })
            {
                card.Value = "–";
                card.Note = null;
            }

            return;
        }

        var totals = shift.Totals;
        _status.Set("Đang mở", BadgeKind.Info);
        _title.Text = $"Ca {shift.Code}";
        _subtitle.Text = $"{shift.UserName} · mở lúc {StoreTime(shift.OpenedAt).ToString("HH:mm dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)} · tiền đầu ca {Vnd.Format(shift.OpeningFloat)}";
        _revenue.Value = Vnd.Format(totals.NetRevenue);
        _revenue.Note = totals.DiscountTotal > 0 ? $"Đã giảm giá {Vnd.Format(totals.DiscountTotal)}" : null;
        _invoices.Value = totals.SalesCount.ToString("#,0", Vnd.Culture);
        _invoices.Note = totals.VoidCount + totals.ReturnCount > 0 ? $"{totals.VoidCount} hủy · {totals.ReturnCount} trả hàng" : null;
        _cash.Value = Vnd.Format(shift.ExpectedCash);
        _cash.Note = shift.PayInTotal + shift.PayOutTotal > 0
            ? $"Nộp {Vnd.FormatNumber(shift.PayInTotal)} · chi {Vnd.FormatNumber(shift.PayOutTotal)}"
            : null;
        _nonCash.Value = Vnd.Format(totals.TransferSales + totals.CardSales);
        _nonCash.Note = $"CK {Vnd.FormatNumber(totals.TransferSales)} · thẻ {Vnd.FormatNumber(totals.CardSales)}";
    }

    private async Task ReloadHistoryAsync()
    {
        var from = _from.Value?.Date ?? DateTime.Today.AddDays(-6);
        var to = (_to.Value?.Date ?? DateTime.Today).AddDays(1);
        var filter = new ShiftFilter(
            From: new DateTimeOffset(from, _options.StoreUtcOffset),
            To: new DateTimeOffset(to, _options.StoreUtcOffset),
            Status: _onlyOpen.Checked ? ShiftStatus.Open : null);

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().ListAsync(filter);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value.Select(s => new ShiftRow(
                s.ShiftId,
                s.Code,
                s.UserId,
                s.UserName ?? $"#{s.UserId}",
                s.Status,
                s.StatusName,
                StoreTime(s.OpenedAt),
                s.ClosedAt is { } closed ? StoreTime(closed) : null,
                s.NetRevenue,
                s.Variance)).ToList());
        }

        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        var row = _grid.GetSelected<ShiftRow>();
        _viewReport.Enabled = row is not null;
        _closeForOther.Enabled = _canSupervise && row is { Status: ShiftStatus.Open } && row.UserId != _currentUser.Session?.UserId;
    }

    private DateTime StoreTime(DateTimeOffset value) => value.ToOffset(_options.StoreUtcOffset).DateTime;

    private async Task OpenShiftAsync()
    {
        using var dialog = new OpenShiftDialog(_scopeFactory, _current?.SuggestedOpeningFloat ?? 0, _currentUser.Session?.FullName);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync();
        }
    }

    private async Task MoveCashAsync(CashMovementType type)
    {
        await ReloadCurrentAsync(); // fresh drawer amount for the pay-out check
        if (_current?.Current is not { } shift)
        {
            return;
        }

        using var dialog = new CashMovementDialog(_scopeFactory, shift, type);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadCurrentAsync();
        }
    }

    private async Task ShowCurrentReportAsync()
    {
        await ReloadCurrentAsync();
        if (_current?.Current is { } shift)
        {
            ShiftReportViewer.Show(this, _scopeFactory, shift);
        }
    }

    private async Task CloseOwnShiftAsync()
    {
        await ReloadCurrentAsync();
        if (_current?.Current is { } shift)
        {
            await CloseAsync(shift, closingForSomeoneElse: false);
        }
    }

    private async Task CloseSelectedForOtherAsync()
    {
        if (_grid.GetSelected<ShiftRow>() is not { Status: ShiftStatus.Open } row)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().GetSummaryAsync(row.ShiftId);
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Kết ca hộ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await CloseAsync(result.Value, closingForSomeoneElse: true);
    }

    private async Task CloseAsync(ShiftSummaryDto shift, bool closingForSomeoneElse)
    {
        using var dialog = new CloseShiftDialog(_scopeFactory, shift, _options, closingForSomeoneElse);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ClosedShift is not { } closed)
        {
            return;
        }

        await ReloadAsync();
        if (MessageBox.Show(this, $"Đã kết ca {closed.Code}. In báo cáo kết ca?", "Kết ca", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
            == DialogResult.Yes)
        {
            ShiftReportViewer.Show(this, _scopeFactory, closed);
        }
    }

    private async Task ShowReportAsync(int shiftId)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().GetSummaryAsync(shiftId);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Báo cáo ca", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ShiftReportViewer.Show(this, _scopeFactory, result.Value);
    }

    /// <summary>Grid row with times already in store time.</summary>
    private sealed record ShiftRow(
        int ShiftId,
        string Code,
        int UserId,
        string UserName,
        ShiftStatus Status,
        string StatusName,
        DateTime OpenedAt,
        DateTime? ClosedAt,
        decimal? NetRevenue,
        decimal? Variance);
}
