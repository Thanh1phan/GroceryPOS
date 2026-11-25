using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Presentation.Dashboard;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dashboard;

/// <summary>
/// Tổng quan: greeting and quick actions, KPI cards (doanh thu hôm nay vs. hôm qua, hóa đơn, lợi nhuận gộp or
/// tiền hoàn trả, ca của bạn), 7-day revenue chart, today's sales by hour, top 5 products of the day and the
/// low-stock list. Refreshes every minute while shown and on F5.
/// Passive MVP view – the logic lives in <see cref="DashboardPresenter"/>.
/// </summary>
internal sealed class DashboardScreen : BaseUserControl, IDashboardView
{
    private readonly DashboardPresenter _presenter;
    private readonly Action<string>? _navigate;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _greeting;
    private readonly ToneLabel _subtitle;
    private readonly FlowLayoutPanel _actions;
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

    /// <param name="navigate">Opens another screen by its menu key ("pos", "inventory", …); null hides the shortcuts.</param>
    public DashboardScreen(IServiceProvider services, Action<string>? navigate = null)
    {
        _presenter = services.GetRequiredService<DashboardPresenter>();
        _navigate = navigate;

        // ---- Greeting + quick actions --------------------------------------------------------------
        _greeting = new Label { Dock = DockStyle.Top, Height = 34, Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold), Text = "Tổng quan", AutoEllipsis = true };
        _subtitle = new ToneLabel { Dock = DockStyle.Top, Height = 22, Font = ThemeFonts.Get(FontRole.Body), Tone = LabelTone.Muted, AutoEllipsis = true };
        var titles = new Panel { Dock = DockStyle.Fill };
        titles.Controls.Add(_subtitle);
        titles.Controls.Add(_greeting);

        _actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };

        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(24, 20, 24, 8) };
        header.Controls.Add(titles);
        header.Controls.Add(_actions);

        // ---- KPI row --------------------------------------------------------------------------------
        _revenue = Stat(DashboardBoard.RevenueCaption, Glyphs.Money, BadgeKind.Success);
        _invoices = Stat(DashboardBoard.InvoicesCaption, Glyphs.Document, BadgeKind.Primary);
        _profit = Stat(DashboardBoard.ProfitCaption, Glyphs.Chart, BadgeKind.Info);
        _shift = Stat(DashboardShiftCard.Caption, Glyphs.Calculator, BadgeKind.Warning);
        var stats = new StatCardRow { Padding = new Padding(24, 4, 24, 12) };
        stats.AddCards(_revenue, _invoices, _profit, _shift);

        // ---- Charts ---------------------------------------------------------------------------------
        _weekChart = new BarChart { Dock = DockStyle.Fill, ShowValues = true, EmptyText = "Chưa có doanh thu trong 7 ngày qua", TooltipFormatter = Vnd.Format };
        _weekCard = Card("Doanh thu 7 ngày", Glyphs.Chart, _weekChart);
        _hourChart = new BarChart { Dock = DockStyle.Fill, EmptyText = "Hôm nay chưa có hóa đơn nào", TooltipFormatter = Vnd.Format };
        _hourCard = Card("Doanh thu theo giờ hôm nay", Glyphs.History, _hourChart);

        // ---- Lists ----------------------------------------------------------------------------------
        _topGrid = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 50, EmptyText = "Hôm nay chưa bán sản phẩm nào." };
        _topGrid.AddTextColumn(nameof(DashboardTopRow.Rank), "#", width: 40);
        _topGrid.AddTextColumn(nameof(DashboardTopRow.ProductName), "Sản phẩm");
        _topGrid.AddTextColumn(nameof(DashboardTopRow.QuantityText), "SL", width: 90);
        _topGrid.AddMoneyColumn(nameof(DashboardTopRow.Revenue), "Doanh thu", width: 130);
        _topCard = Card("Bán chạy hôm nay", Glyphs.Shop, _topGrid);

        _lowGrid = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 50, EmptyText = "Không có sản phẩm nào dưới định mức. 👍" };
        _lowGrid.AddTextColumn(nameof(DashboardLowRow.ProductCode), "Mã SP", width: 90);
        _lowGrid.AddTextColumn(nameof(DashboardLowRow.ProductName), "Sản phẩm");
        _lowGrid.AddNumberColumn(nameof(DashboardLowRow.StockQuantity), "Tồn", width: 80, decimals: 3);
        _lowGrid.AddNumberColumn(nameof(DashboardLowRow.MinStockLevel), "Định mức", width: 90, decimals: 3);
        _lowGrid.InnerGrid.CellFormatting += ToneLowStockCell;
        _lowGrid.RowActivated += (_, _) => _presenter.Open(DashboardShortcut.Inventory);
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

        _timer = new System.Windows.Forms.Timer { Interval = (int)DashboardPresenter.RefreshInterval.TotalMilliseconds };
        _timer.Tick += async (_, _) =>
        {
            if (Visible && IsAlive)
            {
                await _presenter.LoadAsync(automatic: true);
            }
        };

        _presenter.Attach(this);
    }

    public override Task RefreshDataAsync() => _presenter.LoadAsync();

    protected override async Task OnFirstLoadAsync()
    {
        await RefreshDataAsync();
        if (IsAlive)
        {
            _timer.Start();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F2 && _presenter.Open(DashboardShortcut.Pos))
        {
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

    // ---- IDashboardView ---------------------------------------------------------------------------------

    bool IView.IsAlive => IsAlive;

    bool IDashboardView.CanNavigate => _navigate is not null;

    void IDashboardView.ShowLayout(IReadOnlySet<DashboardShortcut> shortcuts, bool showShiftCard)
    {
        _actions.Controls.Clear();
        AddShortcut(shortcuts, DashboardShortcut.Pos, "Bán hàng (F2)", Glyphs.Shop, ButtonVariant.Primary);
        AddShortcut(shortcuts, DashboardShortcut.Purchases, "Nhập hàng", Glyphs.Document, ButtonVariant.Secondary);
        AddShortcut(shortcuts, DashboardShortcut.Inventory, "Tồn kho", Glyphs.Package, ButtonVariant.Secondary);
        var refresh = new ModernButton { Text = "Làm mới (F5)", IconGlyph = Glyphs.Refresh, Variant = ButtonVariant.Ghost, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        refresh.Click += (_, _) => RequestRefresh();
        _actions.Controls.Add(refresh);
        _shift.Visible = showShiftCard;
    }

    void IDashboardView.ShowHeader(DashboardHeader header)
    {
        _greeting.Text = header.Greeting;
        _subtitle.Text = header.Subtitle;
        _subtitle.Tone = header.SubtitleTone.ToLabelTone();
    }

    void IDashboardView.ShowBoard(DashboardBoard board)
    {
        ((IDashboardView)this).ShowHeader(board.Header);
        Apply(_revenue, board.Revenue);
        Apply(_invoices, board.Invoices, BadgeKind.Primary);
        Apply(_profit, board.Profit);
        _profit.Glyph = board.ProfitKind == DashboardProfitKind.Profit ? Glyphs.Chart : Glyphs.Return;

        Apply(_weekChart, _weekCard, board.Week);
        Apply(_hourChart, _hourCard, board.Hours);

        _topGrid.SetItems(board.TopProducts);
        _topCard.Subtitle = board.TopSubtitle;
        _lowGrid.SetItems(board.LowStock);
        _lowCard.Subtitle = board.LowSubtitle;
    }

    void IDashboardView.ShowShiftCard(DashboardCard card) => Apply(_shift, card);

    void IDashboardView.Navigate(DashboardShortcut shortcut) => _navigate?.Invoke(shortcut switch
    {
        DashboardShortcut.Pos => "pos",
        DashboardShortcut.Purchases => "purchases",
        _ => "inventory",
    });

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) => Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    // ---- Building blocks ----------------------------------------------------------------------------------

    private void AddShortcut(IReadOnlySet<DashboardShortcut> shortcuts, DashboardShortcut shortcut, string text, string glyph, ButtonVariant variant)
    {
        if (!shortcuts.Contains(shortcut))
        {
            return;
        }

        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += (_, _) => _presenter.Open(shortcut);
        _actions.Controls.Add(button);
    }

    private static void Apply(StatCard target, DashboardCard card, BadgeKind? accent = null)
    {
        target.Caption = card.Caption;
        target.Value = card.Value;
        target.Note = card.Note;
        target.Accent = accent ?? card.Accent.ToBadgeKind();
        if (card.ShowTrend)
        {
            target.SetTrend(card.TrendPercent, card.TrendComparedTo ?? "kỳ trước");
        }
        else
        {
            target.ClearTrend();
        }
    }

    private static void Apply(BarChart chart, ModernCard card, DashboardChart data)
    {
        chart.Points = data.Bars.Select(b => new ChartPoint(b.Label, b.Value, b.Tooltip)).ToList();
        chart.HighlightIndex = data.HighlightIndex;
        card.Subtitle = data.Subtitle;
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
        if (e.RowIndex < 0 || _lowGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(DashboardLowRow.StockQuantity) || e.Value is not decimal stock)
        {
            return;
        }

        var palette = ThemeManager.Current.Colors;
        e.CellStyle!.ForeColor = stock <= 0 ? palette.Danger : palette.Warning;
    }
}
