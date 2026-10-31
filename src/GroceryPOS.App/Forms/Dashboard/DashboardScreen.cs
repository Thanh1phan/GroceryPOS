using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dashboard;

/// <summary>
/// Tổng quan: greeting and quick actions, KPI cards (doanh thu hôm nay vs. hôm qua, hóa đơn, lợi nhuận gộp or
/// tiền hoàn trả, ca của bạn), 7-day revenue chart, today's sales by hour, top 5 products of the day and the
/// low-stock list. Refreshes every minute while shown and on F5.
/// </summary>
internal sealed class DashboardScreen : UserControl
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;
    private readonly Action<string>? _navigate;
    private readonly bool _canSeeShift;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _greeting;
    private readonly ToneLabel _subtitle;
    private readonly StatCard _revenue;
    private readonly StatCard _invoices;
    private readonly StatCard _profit;
    private readonly StatCard _shift;
    private readonly ModernCard _weekCard;
    private readonly ModernCard _hourCard;
    private readonly ModernCard _topCard;
    private readonly ModernCard _lowCard;
    private readonly BarChart _weekChart;
    private readonly BarChart _hourChart;
    private readonly ModernDataGrid _topGrid;
    private readonly ModernDataGrid _lowGrid;
    private bool _loading;

    /// <param name="navigate">Opens another screen by its menu key ("pos", "inventory", …); null hides the shortcuts.</param>
    public DashboardScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, Action<string>? navigate = null)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        _navigate = navigate;
        _canSeeShift = currentUser.HasPermission(Permissions.ShiftsManage) || currentUser.HasPermission(Permissions.ShiftsSupervise);
        Dock = DockStyle.Fill;

        // ---- Greeting + quick actions --------------------------------------------------------------
        _greeting = new Label { Dock = DockStyle.Top, Height = 34, Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold), Text = "Tổng quan" };
        _subtitle = new ToneLabel { Dock = DockStyle.Top, Height = 22, Font = ThemeFonts.Get(FontRole.Body), Tone = LabelTone.Muted };
        var titles = new Panel { Dock = DockStyle.Fill };
        titles.Controls.Add(_subtitle);
        titles.Controls.Add(_greeting);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        AddShortcut(actions, "Bán hàng (F2)", Glyphs.Shop, ButtonVariant.Primary, "pos", Permissions.SalesCreate);
        AddShortcut(actions, "Nhập hàng", Glyphs.Document, ButtonVariant.Secondary, "purchases", Permissions.PurchasesManage);
        AddShortcut(actions, "Tồn kho", Glyphs.Package, ButtonVariant.Secondary, "inventory", Permissions.InventoryView);
        var refresh = new ModernButton { Text = "Làm mới (F5)", IconGlyph = Glyphs.Refresh, Variant = ButtonVariant.Ghost, Size = new Size(140, 40), Margin = new Padding(8, 0, 0, 0) };
        refresh.Click += async (_, _) => await ReloadAsync();
        actions.Controls.Add(refresh);

        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(24, 20, 24, 8) };
        header.Controls.Add(titles);
        header.Controls.Add(actions);

        // ---- KPI row --------------------------------------------------------------------------------
        _revenue = Stat("Doanh thu hôm nay", Glyphs.Money, BadgeKind.Success);
        _invoices = Stat("Hóa đơn hôm nay", Glyphs.Document, BadgeKind.Primary);
        _profit = Stat("Lợi nhuận gộp hôm nay", Glyphs.Chart, BadgeKind.Info);
        _shift = Stat("Ca của bạn", Glyphs.Calculator, BadgeKind.Warning);
        _shift.Visible = _canSeeShift;
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, WrapContents = false, Padding = new Padding(24, 4, 24, 0) };
        stats.Controls.AddRange([_revenue, _invoices, _profit, _shift]);

        // ---- Charts ---------------------------------------------------------------------------------
        _weekChart = new BarChart { Dock = DockStyle.Fill, ShowValues = true, EmptyText = "Chưa có doanh thu trong 7 ngày qua", TooltipFormatter = Vnd.Format };
        _weekCard = Card("Doanh thu 7 ngày", Glyphs.Chart, _weekChart);
        _hourChart = new BarChart { Dock = DockStyle.Fill, EmptyText = "Hôm nay chưa có hóa đơn nào", TooltipFormatter = Vnd.Format };
        _hourCard = Card("Doanh thu theo giờ hôm nay", Glyphs.History, _hourChart);

        // ---- Lists ----------------------------------------------------------------------------------
        _topGrid = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 50, EmptyText = "Hôm nay chưa bán sản phẩm nào." };
        _topGrid.AddTextColumn(nameof(TopRow.Rank), "#", width: 40);
        _topGrid.AddTextColumn(nameof(TopRow.ProductName), "Sản phẩm");
        _topGrid.AddTextColumn(nameof(TopRow.QuantityText), "SL", width: 90);
        _topGrid.AddMoneyColumn(nameof(TopRow.Revenue), "Doanh thu", width: 130);
        _topCard = Card("Bán chạy hôm nay", Glyphs.Shop, _topGrid);

        _lowGrid = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 50, EmptyText = "Không có sản phẩm nào dưới định mức. 👍" };
        _lowGrid.AddTextColumn(nameof(LowRow.ProductCode), "Mã SP", width: 90);
        _lowGrid.AddTextColumn(nameof(LowRow.ProductName), "Sản phẩm");
        _lowGrid.AddNumberColumn(nameof(LowRow.StockQuantity), "Tồn", width: 80, decimals: 3);
        _lowGrid.AddNumberColumn(nameof(LowRow.MinStockLevel), "Định mức", width: 90, decimals: 3);
        _lowGrid.InnerGrid.CellFormatting += ToneLowStockCell;
        if (_navigate is not null && currentUser.HasPermission(Permissions.InventoryView))
        {
            _lowGrid.RowActivated += (_, _) => _navigate("inventory");
        }

        _lowCard = Card("Sắp hết hàng", Glyphs.Warning, _lowGrid);

        var board = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(24, 8, 24, 24) };
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        board.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        board.Controls.Add(_weekCard, 0, 0);
        board.Controls.Add(_topCard, 1, 0);
        board.Controls.Add(_hourCard, 0, 1);
        board.Controls.Add(_lowCard, 1, 1);

        Controls.Add(board);
        Controls.Add(stats);
        Controls.Add(header);

        _timer = new System.Windows.Forms.Timer { Interval = (int)RefreshInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) =>
        {
            if (Visible && !_loading)
            {
                await ReloadAsync();
            }
        };

        Load += async (_, _) =>
        {
            await ReloadAsync();
            _timer.Start();
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            _ = ReloadAsync();
            return true;
        }

        if (keyData == Keys.F2 && _navigate is not null && _currentUser.HasPermission(Permissions.SalesCreate))
        {
            _navigate("pos");
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AddShortcut(FlowLayoutPanel panel, string text, string glyph, ButtonVariant variant, string key, string permission)
    {
        if (_navigate is null || !_currentUser.HasPermission(permission))
        {
            return;
        }

        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, Size = new Size(text.Length > 10 ? 150 : 120, 40), Margin = new Padding(8, 0, 0, 0) };
        button.Click += (_, _) => _navigate(key);
        panel.Controls.Add(button);
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "–",
        Size = new Size(260, 128),
        Margin = new Padding(0, 0, 16, 0),
    };

    private static ModernCard Card(string title, string glyph, Control content)
    {
        var card = new ModernCard { Title = title, HeaderGlyph = glyph, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 16) };
        card.Controls.Add(content);
        return card;
    }

    private void ToneLowStockCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _lowGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(LowRow.StockQuantity) || e.Value is not decimal stock)
        {
            return;
        }

        var palette = ThemeManager.Current.Colors;
        e.CellStyle!.ForeColor = stock <= 0 ? palette.Danger : palette.Warning;
    }

    private async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var dashboard = await reports.GetDashboardAsync();
            ShiftSummaryDto? shift = null;
            var shiftLoaded = false;
            if (_canSeeShift)
            {
                var current = await scope.ServiceProvider.GetRequiredService<IShiftService>().GetCurrentAsync();
                shiftLoaded = current.IsSuccess;
                shift = current.IsSuccess ? current.Value.Current : null;
            }

            if (IsDisposed)
            {
                return;
            }

            if (dashboard.IsFailure)
            {
                _subtitle.Text = dashboard.Error.Message;
                return;
            }

            Render(dashboard.Value);
            RenderShift(shift, shiftLoaded);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Render(DashboardDto data)
    {
        var now = DateTime.Now;
        var name = _currentUser.Session?.FullName;
        _greeting.Text = string.IsNullOrWhiteSpace(name) ? DashboardFigures.Greeting(now.Hour) : $"{DashboardFigures.Greeting(now.Hour)}, {name}!";
        _subtitle.Text = $"{DashboardFigures.LongDayLabel(data.Today)} · cập nhật lúc {now:HH:mm}";

        // KPI cards.
        _revenue.Value = Vnd.Format(data.NetRevenueToday);
        _revenue.SetTrend(data.RevenueChangePercent, "hôm qua");
        _revenue.Note = data.RevenueChangePercent is null ? $"Hôm qua: {Vnd.Format(data.NetRevenueYesterday)}" : null;

        _invoices.Value = data.InvoiceCountToday.ToString("#,0", Vnd.Culture);
        _invoices.Note = data.InvoiceCountToday == 0 ? "Chưa có hóa đơn" : $"TB {Vnd.Format(data.AverageBasketToday)} / hóa đơn";

        if (data.GrossProfitToday is { } profit)
        {
            _profit.Caption = "Lợi nhuận gộp hôm nay";
            _profit.Glyph = Glyphs.Chart;
            _profit.Accent = profit < 0 ? BadgeKind.Danger : BadgeKind.Info;
            _profit.Value = Vnd.Format(profit);
            _profit.Note = data.RefundTotalToday > 0 ? $"Đã hoàn trả {Vnd.Format(data.RefundTotalToday)}" : "Sau giá vốn, trước chi phí";
        }
        else
        {
            // Cashiers don't see margins: show today's refunds instead.
            _profit.Caption = "Tiền hoàn trả hôm nay";
            _profit.Glyph = Glyphs.Return;
            _profit.Accent = data.RefundTotalToday > 0 ? BadgeKind.Warning : BadgeKind.Neutral;
            _profit.Value = Vnd.Format(data.RefundTotalToday);
            _profit.Note = "Khách trả hàng";
        }

        // 7-day chart.
        var week = DashboardFigures.Week(data.Last7Days);
        _weekChart.Points = data.Last7Days
            .Select(d => new ChartPoint(
                DashboardFigures.DayLabel(d.Date, data.Today),
                d.NetRevenue,
                $"{DashboardFigures.LongDayLabel(d.Date)}\nDoanh thu: {Vnd.Format(d.NetRevenue)}\nHóa đơn: {d.InvoiceCount:#,0}"))
            .ToList();
        _weekChart.HighlightIndex = data.Last7Days.Count - 1;
        _weekCard.Subtitle = week.BestDay is null
            ? "Chưa có doanh thu"
            : $"Tổng {Vnd.Format(week.NetRevenue)} · TB {Vnd.Format(week.AveragePerDay)}/ngày · cao nhất {DashboardFigures.DayLabel(week.BestDay.Date, data.Today)}";

        // Hourly chart.
        var hours = DashboardFigures.HourWindow(data.TodayByHour);
        _hourChart.Points = hours
            .Select(h => new ChartPoint(
                $"{h.Hour}h",
                h.SalesTotal,
                $"{DashboardFigures.HourRangeLabel(h.Hour)}\nDoanh số: {Vnd.Format(h.SalesTotal)}\nHóa đơn: {h.InvoiceCount:#,0}"))
            .ToList();
        _hourChart.HighlightIndex = hours.ToList().FindIndex(h => h.Hour == now.Hour);
        var peak = DashboardFigures.PeakHour(data.TodayByHour);
        _hourCard.Subtitle = peak is null
            ? "Chưa có hóa đơn hôm nay"
            : $"Đông khách nhất {DashboardFigures.HourRangeLabel(peak.Hour)} · {Vnd.Format(peak.SalesTotal)} ({peak.InvoiceCount} HĐ)";

        // Lists.
        _topGrid.SetItems(data.TopProductsToday.Select((p, i) => new TopRow(
            (i + 1).ToString(Vnd.Culture),
            p.ProductName,
            $"{p.Quantity.ToString("#,0.###", Vnd.Culture)} {p.Unit}",
            p.Revenue)).ToList());
        _topCard.Subtitle = data.TopProductsToday.Count == 0 ? null : "Top 5 theo doanh thu";

        _lowGrid.SetItems(data.LowStockItems.Select(i => new LowRow(i.ProductCode, i.ProductName, i.StockQuantity, i.MinStockLevel)).ToList());
        var lowTotal = data.OutOfStockCount + data.LowStockCount;
        _lowCard.Subtitle = lowTotal == 0
            ? "Tồn kho ổn định"
            : $"{data.OutOfStockCount:#,0} hết hàng · {data.LowStockCount:#,0} sắp hết" + (lowTotal > data.LowStockItems.Count ? $" (hiện {data.LowStockItems.Count})" : string.Empty);
    }

    private void RenderShift(ShiftSummaryDto? shift, bool loaded)
    {
        if (!_canSeeShift)
        {
            return;
        }

        if (!loaded)
        {
            _shift.Value = "–";
            _shift.Note = "Không tải được thông tin ca";
            return;
        }

        if (shift is null)
        {
            _shift.Caption = "Ca của bạn";
            _shift.Accent = BadgeKind.Warning;
            _shift.Value = "Chưa mở ca";
            _shift.Note = "Mở ca trước khi bán hàng";
            return;
        }

        _shift.Caption = $"Ca {shift.Code}";
        _shift.Accent = BadgeKind.Success;
        _shift.Value = Vnd.Format(shift.ExpectedCash);
        _shift.Note = $"Tiền mặt trong két · mở lúc {shift.OpenedAt.ToLocalTime():HH:mm}";
    }

    private sealed record TopRow(string Rank, string ProductName, string QuantityText, decimal Revenue);

    private sealed record LowRow(string ProductCode, string ProductName, decimal StockQuantity, decimal MinStockLevel);
}
