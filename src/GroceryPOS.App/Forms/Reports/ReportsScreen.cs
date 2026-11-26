using GroceryPOS.App.Forms.Pos;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Presentation.Auditing;
using GroceryPOS.Presentation.Dashboard;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Reports;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Reports;

/// <summary>
/// Báo cáo: one period picker for four tabs – Doanh thu (KPI cards vs. the previous period, daily/monthly chart,
/// day table), Hàng bán chạy (rank by quantity/revenue/profit, category, top N), Lợi nhuận theo danh mục (chart +
/// table) and Giá trị tồn kho (current stock, ignores the period). Xuất Excel (Ctrl+E) exports the current tab,
/// "Xuất tất cả" every tab; In / PDF (Ctrl+P, Ctrl+Shift+P for every tab) opens the A4 print preview. F5 reloads.
/// Passive MVP view – the logic lives in <see cref="ReportsPresenter"/>.
/// </summary>
internal sealed class ReportsScreen : BaseUserControl, IReportsView
{
    private readonly ReportsPresenter _presenter;
    private readonly ModernDateRangePicker _period;
    private readonly ToneLabel _status;
    private readonly ModernTabControl _tabs;

    // Revenue tab.
    private readonly StatCard _netRevenue;
    private readonly StatCard _invoiceCount;
    private readonly StatCard _grossProfit;
    private readonly StatCard _refunds;
    private readonly ModernCard _revenueChartCard;
    private readonly BarChart _revenueChart;
    private readonly ModernDataGrid _revenueGrid;

    // Best-seller tab.
    private readonly ModernComboBox _rankBy;
    private readonly ModernComboBox _category;
    private readonly ModernComboBox _top;
    private readonly ModernDataGrid _bestGrid;
    private readonly ModernCard _bestCard;

    // Profit tab.
    private readonly BarChart _profitChart;
    private readonly ModernCard _profitChartCard;
    private readonly ModernDataGrid _profitGrid;

    // Inventory tab.
    private readonly StatCard _costValue;
    private readonly StatCard _retailValue;
    private readonly StatCard _potentialProfit;
    private readonly StatCard _stockAlerts;
    private readonly ModernDataGrid _inventoryGrid;

    private IDisposable? _loadingOverlay;
    private bool _suppressEvents;

    public ReportsScreen(IServiceProvider services)
    {
        _presenter = services.GetRequiredService<ReportsPresenter>();
        RefreshOnF5 = false; // F5 also moves the period to today (handled below)

        // ---- Header: period + actions ------------------------------------------------------------------
        _period = new ModernDateRangePicker { Margin = new Padding(0, 0, 0, 0) };
        _period.RangeChanged += async (_, _) =>
        {
            if (!_suppressEvents)
            {
                await _presenter.SetPeriodAsync(CurrentPeriod, _period.Describe());
            }
        };
        _status = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(12, 12, 0, 0), Font = ThemeFonts.Get(FontRole.Caption) };

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 16, 8) };
        left.Controls.AddRange([_period, _status]);

        var refresh = Action("Làm mới (F5)", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        var exportOne = Action("Xuất Excel (Ctrl+E)", Glyphs.Document, ButtonVariant.Primary, () => _presenter.ExportAsync(all: false));
        var exportAll = Action("Xuất tất cả", Glyphs.List, ButtonVariant.Secondary, () => _presenter.ExportAsync(all: true));
        var print = Action("In / PDF (Ctrl+P)", Glyphs.Print, ButtonVariant.Secondary, () => _presenter.PrintAsync(all: false));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        actions.Controls.AddRange([refresh, print, exportAll, exportOne]);

        // Period and actions share one line on wide windows and wrap onto two on narrow ones.
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(24, 16, 24, 0) };
        header.Controls.AddRange([left, actions]);

        // ---- Tab 1: revenue -------------------------------------------------------------------------------
        _netRevenue = Stat(RevenueBoard.NetRevenueCaption, Glyphs.Money, BadgeKind.Success);
        _invoiceCount = Stat(RevenueBoard.InvoicesCaption, Glyphs.Document, BadgeKind.Primary);
        _grossProfit = Stat(RevenueBoard.ProfitCaption, Glyphs.Chart, BadgeKind.Info);
        _refunds = Stat(RevenueBoard.RefundsCaption, Glyphs.Return, BadgeKind.Warning);
        var revenueStats = new StatCardRow { Padding = new Padding(0, 0, 0, 12) };
        revenueStats.AddCards(_netRevenue, _invoiceCount, _grossProfit, _refunds);

        _revenueChart = new BarChart { Dock = DockStyle.Fill, EmptyText = "Không có doanh thu trong kỳ này", TooltipFormatter = Vnd.Format, ValueFormatter = ReportText.ShortMoney };
        _revenueChartCard = Card("Doanh thu thuần", Glyphs.Chart, _revenueChart);

        _revenueGrid = Grid("Không có dữ liệu.", search: false);
        _revenueGrid.AddDateColumn(nameof(RevenueRow.Date), "Ngày", width: 110, searchable: false);
        _revenueGrid.AddNumberColumn(nameof(RevenueRow.InvoiceCount), "Số HĐ", width: 70);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.SalesTotal), "Doanh số", width: 130);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.DiscountTotal), "Giảm giá", width: 110);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.RefundTotal), "Hoàn trả", width: 110);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.NetRevenue), "DT thuần", width: 130);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.VatAmount), "VAT", width: 100);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.CostOfGoods), "Giá vốn", width: 130);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.GrossProfit), "Lợi nhuận", width: 130);
        _revenueGrid.AddNumberColumn(nameof(RevenueRow.MarginPercent), "LN %", width: 70, decimals: 1);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.Cash), "Tiền mặt", width: 120);
        _revenueGrid.AddMoneyColumn(nameof(RevenueRow.NonCash), "CK + thẻ", width: 120);
        _revenueGrid.InnerGrid.CellFormatting += ToneMoneyCells;
        var revenueTable = Card("Chi tiết theo ngày", Glyphs.Calendar, _revenueGrid);

        var revenueBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        revenueBody.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        revenueBody.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        revenueBody.Controls.Add(_revenueChartCard, 0, 0);
        revenueBody.Controls.Add(revenueTable, 0, 1);

        var revenuePage = Page("Doanh thu", Glyphs.Money);
        revenuePage.Controls.Add(revenueBody);
        revenuePage.Controls.Add(revenueStats);

        // ---- Tab 2: best sellers --------------------------------------------------------------------------
        _rankBy = Filter(150, ReportsPresenter.RankOptions);
        _category = Filter(200, new List<FilterOption<int?>> { ReportsPresenter.AllCategories });
        _top = Filter(110, ReportsPresenter.TopOptions);
        _top.SelectedIndex = ReportsPresenter.TopOptions.ToList().FindIndex(o => o.Value == ReportsPresenter.DefaultTop);
        foreach (var filter in new[] { _rankBy, _category, _top })
        {
            filter.SelectedIndexChanged += async (_, _) =>
            {
                if (!_suppressEvents)
                {
                    await _presenter.SetBestSellerFiltersAsync(
                        _rankBy.SelectedValue is ProductRanking r ? r : ReportsPresenter.DefaultRankBy,
                        _top.SelectedValue is int t ? t : ReportsPresenter.DefaultTop,
                        _category.SelectedValue as int?);
                }
            };
        }

        _bestGrid = Grid("Không có sản phẩm nào được bán trong kỳ này.", search: true);
        _bestGrid.AddNumberColumn(nameof(ProductSalesRowDto.Rank), "#", width: 50);
        _bestGrid.AddTextColumn(nameof(ProductSalesRowDto.ProductCode), "Mã SP", width: 100);
        _bestGrid.AddTextColumn(nameof(ProductSalesRowDto.ProductName), "Sản phẩm");
        _bestGrid.AddTextColumn(nameof(ProductSalesRowDto.CategoryName), "Danh mục", width: 150);
        _bestGrid.AddNumberColumn(nameof(ProductSalesRowDto.Quantity), "SL bán", width: 90, decimals: 3);
        _bestGrid.AddTextColumn(nameof(ProductSalesRowDto.Unit), "ĐVT", width: 70, searchable: false);
        _bestGrid.AddNumberColumn(nameof(ProductSalesRowDto.InvoiceCount), "Số HĐ", width: 70);
        _bestGrid.AddMoneyColumn(nameof(ProductSalesRowDto.Revenue), "Doanh thu", width: 130);
        _bestGrid.AddMoneyColumn(nameof(ProductSalesRowDto.Profit), "Lợi nhuận", width: 130);
        _bestGrid.AddNumberColumn(nameof(ProductSalesRowDto.MarginPercent), "LN %", width: 70, decimals: 1);
        _bestGrid.AddNumberColumn(nameof(ProductSalesRowDto.RevenueSharePercent), "Tỷ trọng %", width: 90, decimals: 1);
        _bestGrid.InnerGrid.CellFormatting += ToneMoneyCells;
        _bestGrid.ToolbarItems.Controls.AddRange([_rankBy, _category, _top]);
        _bestCard = Card("Hàng bán chạy", Glyphs.Shop, _bestGrid);

        var bestPage = Page("Hàng bán chạy", Glyphs.Shop);
        bestPage.Controls.Add(_bestCard);

        // ---- Tab 3: profit by category --------------------------------------------------------------------
        _profitChart = new BarChart { Dock = DockStyle.Fill, ShowValues = true, ValueFormatter = ReportText.ShortMoney, TooltipFormatter = Vnd.Format, EmptyText = "Chưa có lợi nhuận trong kỳ này" };
        _profitChartCard = Card("Lợi nhuận gộp theo danh mục", Glyphs.Chart, _profitChart);
        _profitGrid = Grid("Không có dữ liệu.", search: false);
        _profitGrid.AddTextColumn(nameof(CategoryProfitRowDto.CategoryName), "Danh mục");
        _profitGrid.AddNumberColumn(nameof(CategoryProfitRowDto.ProductCount), "Số SP có bán", width: 110);
        _profitGrid.AddMoneyColumn(nameof(CategoryProfitRowDto.Revenue), "Doanh thu", width: 140);
        _profitGrid.AddMoneyColumn(nameof(CategoryProfitRowDto.RevenueExclVat), "DT chưa VAT", width: 140);
        _profitGrid.AddMoneyColumn(nameof(CategoryProfitRowDto.Cost), "Giá vốn", width: 140);
        _profitGrid.AddMoneyColumn(nameof(CategoryProfitRowDto.Profit), "Lợi nhuận", width: 140);
        _profitGrid.AddNumberColumn(nameof(CategoryProfitRowDto.MarginPercent), "LN %", width: 80, decimals: 1);
        _profitGrid.AddNumberColumn(nameof(CategoryProfitRowDto.RevenueSharePercent), "Tỷ trọng %", width: 90, decimals: 1);
        _profitGrid.InnerGrid.CellFormatting += ToneMoneyCells;
        var profitTable = Card("Chi tiết", Glyphs.List, _profitGrid);

        var profitBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        profitBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        profitBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        profitBody.Controls.Add(_profitChartCard, 0, 0);
        profitBody.Controls.Add(profitTable, 1, 0);
        var profitPage = Page("Lợi nhuận theo danh mục", Glyphs.Chart);
        profitPage.Controls.Add(profitBody);

        // ---- Tab 4: inventory value ----------------------------------------------------------------------
        _costValue = Stat(InventoryBoard.CostCaption, Glyphs.Package, BadgeKind.Primary);
        _retailValue = Stat(InventoryBoard.RetailCaption, Glyphs.Money, BadgeKind.Success);
        _potentialProfit = Stat(InventoryBoard.PotentialCaption, Glyphs.Chart, BadgeKind.Info);
        _stockAlerts = Stat(InventoryBoard.AlertsCaption, Glyphs.Warning, BadgeKind.Warning);
        var inventoryStats = new StatCardRow { Padding = new Padding(0, 0, 0, 12) };
        inventoryStats.AddCards(_costValue, _retailValue, _potentialProfit, _stockAlerts);

        _inventoryGrid = Grid("Chưa có sản phẩm nào.", search: true);
        _inventoryGrid.AddTextColumn(nameof(InventoryValueRowDto.CategoryName), "Danh mục");
        _inventoryGrid.AddNumberColumn(nameof(InventoryValueRowDto.ProductCount), "Số SP", width: 80);
        _inventoryGrid.AddMoneyColumn(nameof(InventoryValueRowDto.CostValue), "Giá trị vốn", width: 150);
        _inventoryGrid.AddMoneyColumn(nameof(InventoryValueRowDto.RetailValue), "Giá trị bán", width: 150);
        _inventoryGrid.AddMoneyColumn(nameof(InventoryValueRowDto.PotentialProfit), "Lãi dự kiến", width: 140);
        _inventoryGrid.AddNumberColumn(nameof(InventoryValueRowDto.OutOfStockCount), "Hết hàng", width: 90);
        _inventoryGrid.AddNumberColumn(nameof(InventoryValueRowDto.LowStockCount), "Sắp hết", width: 90);
        _inventoryGrid.AddNumberColumn(nameof(InventoryValueRowDto.NegativeStockCount), "Tồn âm", width: 90);
        _inventoryGrid.InnerGrid.CellFormatting += ToneMoneyCells;
        var inventoryTable = Card("Theo danh mục", Glyphs.Package, _inventoryGrid);

        var inventoryPage = Page("Giá trị tồn kho", Glyphs.Package);
        inventoryPage.Controls.Add(inventoryTable);
        inventoryPage.Controls.Add(inventoryStats);

        _tabs = new ModernTabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(revenuePage);
        _tabs.TabPages.Add(bestPage);
        _tabs.TabPages.Add(profitPage);
        _tabs.TabPages.Add(inventoryPage);
        _tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (IsHandleCreated && _tabs.SelectedIndex >= 0)
            {
                await _presenter.SelectTabAsync((ReportTab)_tabs.SelectedIndex);
            }
        };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        body.Controls.Add(_tabs);

        Controls.Add(body);
        Controls.Add(header);

        _presenter.Attach(this);
    }

    private ReportPeriod CurrentPeriod => new(_period.Range.From, _period.Range.To);

    /// <summary>F5 / Làm mới: moves a "today"-based period to today, then reloads every tab.</summary>
    public override Task RefreshDataAsync()
    {
        _suppressEvents = true;
        try
        {
            _period.RefreshToday();
        }
        finally
        {
            _suppressEvents = false;
        }

        return _presenter.RefreshAsync(CurrentPeriod, _period.Describe());
    }

    protected override Task OnFirstLoadAsync() => _presenter.LoadAsync(CurrentPeriod, _period.Describe());

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!IsBusy)
        {
            switch (keyData)
            {
                case Keys.F5:
                    RequestRefresh();
                    return true;
                case Keys.Control | Keys.E:
                    _ = _presenter.ExportAsync(all: false);
                    return true;
                case Keys.Control | Keys.P:
                    _ = _presenter.PrintAsync(all: false);
                    return true;
                case Keys.Control | Keys.Shift | Keys.P:
                    _ = _presenter.PrintAsync(all: true);
                    return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadingOverlay?.Dispose();
            _loadingOverlay = null;
        }

        base.Dispose(disposing);
    }

    // ---- IReportsView ---------------------------------------------------------------------------------------

    bool IView.IsAlive => IsAlive;

    void IReportsView.ShowCategories(IReadOnlyList<FilterOption<int?>> options, int? selected, bool visible)
    {
        _suppressEvents = true;
        try
        {
            _category.DataSource = options.ToList();
            _category.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => o.Value == selected));
            _category.Visible = visible;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    void IReportsView.ShowStatus(string text, Tone tone)
    {
        _status.Tone = tone.ToLabelTone();
        _status.Text = text;
    }

    void IReportsView.ShowPeriodEnabled(bool enabled) => _period.Enabled = enabled;

    void IReportsView.ShowLoading(bool loading)
    {
        if (loading)
        {
            _loadingOverlay ??= LoadingOverlay.Begin(_tabs, "Đang tải báo cáo…");
        }
        else
        {
            _loadingOverlay?.Dispose();
            _loadingOverlay = null;
        }
    }

    void IReportsView.ShowRevenue(RevenueBoard board)
    {
        Apply(_netRevenue, board.NetRevenue, BadgeKind.Success);
        Apply(_invoiceCount, board.Invoices, BadgeKind.Primary);
        Apply(_grossProfit, board.Profit);
        Apply(_refunds, board.Refunds);
        _revenueChart.ShowValues = board.ShowBarValues;
        _revenueChart.Points = Points(board.Bars);
        _revenueChartCard.Title = board.ChartTitle;
        _revenueChartCard.Subtitle = board.ChartSubtitle;
        _revenueGrid.SetItems(board.Rows);
    }

    void IReportsView.ShowBestSellers(BestSellerBoard board)
    {
        _bestGrid.SetItems(board.Rows);
        _bestCard.Subtitle = board.Subtitle;
    }

    void IReportsView.ShowProfit(ProfitBoard board)
    {
        _profitGrid.SetItems(board.Rows);
        _profitChart.Points = Points(board.Bars);
        _profitChartCard.Subtitle = board.Subtitle;
    }

    void IReportsView.ShowInventory(InventoryBoard board)
    {
        Apply(_costValue, board.CostValue, BadgeKind.Primary);
        Apply(_retailValue, board.RetailValue);
        Apply(_potentialProfit, board.PotentialProfit);
        Apply(_stockAlerts, board.StockAlerts);
        _inventoryGrid.SetItems(board.Rows);
    }

    Task<T> IReportsView.RunBusyAsync<T>(Func<Task<T>> work, string message) => RunBusyAsync(work, message);

    Task<string?> IReportsView.SaveExcelAsync(string suggestedFileName, byte[] content) =>
        ExcelFiles.SaveAsync(this, ReportsPresenter.Title, "Lưu báo cáo", suggestedFileName, content);

    void IReportsView.ShowPrintPreview(ReportDocument document, Func<ReportOutput, string?, Task> onOutput)
    {
        using var preview = new ReportPrintPreviewForm(document, onOutput);
        preview.ShowDialog(FindForm());
    }

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) => Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    // ---- Building blocks -------------------------------------------------------------------------------

    private static List<ChartPoint> Points(IReadOnlyList<DashboardBar> bars) =>
        bars.Select(b => new ChartPoint(b.Label, b.Value, b.Tooltip)).ToList();

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

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += async (_, _) => await onClick();
        return button;
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

    private static ModernTabPage Page(string text, string glyph) =>
        new(text) { IconGlyph = glyph, Padding = new Padding(0, 12, 0, 0) };

    private static ModernDataGrid Grid(string emptyText, bool search) => new()
    {
        Dock = DockStyle.Fill,
        ShowSearch = search,
        ShowPager = false,
        PageSize = 1000,
        EmptyText = emptyText,
        SearchPlaceholder = "Tìm…",
    };

    private static ModernComboBox Filter<T>(int width, IEnumerable<FilterOption<T>> options) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        ReserveMessageSpace = false,
        DisplayMember = nameof(FilterOption<T>.Name),
        ValueMember = nameof(FilterOption<T>.Value),
        DataSource = options.ToList(),
        Margin = new Padding(8, 0, 0, 0),
    };

    private static void ToneMoneyCells(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Value is not decimal value || value >= 0 || sender is not DataGridView grid)
        {
            return;
        }

        var property = grid.Columns[e.ColumnIndex].DataPropertyName;
        if (property is nameof(RevenueRow.GrossProfit) or nameof(RevenueRow.MarginPercent) or nameof(InventoryValueRowDto.PotentialProfit))
        {
            e.CellStyle!.ForeColor = ThemeManager.Current.Colors.Danger;
        }
    }
}
