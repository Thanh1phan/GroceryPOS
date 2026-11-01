using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Inputs.Core;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Reports;

/// <summary>
/// Báo cáo: one period picker for four tabs – Doanh thu (KPI cards vs. the previous period, daily/monthly chart,
/// day table), Hàng bán chạy (rank by quantity/revenue/profit, category, top N), Lợi nhuận theo danh mục (chart +
/// table) and Giá trị tồn kho (current stock, ignores the period). Only the visible tab is loaded; the others
/// reload when shown after a filter change. Xuất Excel (Ctrl+E) exports the current tab, "Xuất tất cả" every tab
/// as one workbook (sheet per report, audited). F5 reloads.
/// </summary>
internal sealed class ReportsScreen : UserControl
{
    private const int RevenueTab = 0;
    private const int BestSellerTab = 1;
    private const int ProfitTab = 2;
    private const int InventoryTab = 3;
    private const string Caption = "Báo cáo";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernDateRangePicker _period;
    private readonly ToneLabel _status;
    private readonly ModernTabControl _tabs;
    private readonly bool[] _stale = [true, true, true, true];

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

    private bool _loading;
    private bool _loadingFilters;
    private int _filterVersion;

    public ReportsScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _ = currentUser; // menu entry is already gated by reports.view; the services re-check every call.
        Dock = DockStyle.Fill;

        // ---- Header: period + actions ------------------------------------------------------------------
        _period = new ModernDateRangePicker { Margin = new Padding(0, 0, 0, 0) };
        _period.RangeChanged += async (_, _) => await OnFiltersChangedAsync(includingInventory: false);
        _status = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(12, 12, 0, 0), Font = ThemeFonts.Get(FontRole.Caption) };

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        left.Controls.AddRange([_period, _status]);

        var refresh = Action("Làm mới (F5)", Glyphs.Refresh, ButtonVariant.Ghost, ReloadAllAsync);
        var exportOne = Action("Xuất Excel (Ctrl+E)", Glyphs.Document, ButtonVariant.Primary, () => ExportAsync(all: false));
        var exportAll = Action("Xuất tất cả", Glyphs.List, ButtonVariant.Secondary, () => ExportAsync(all: true));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([refresh, exportAll, exportOne]);

        var header = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(24, 16, 24, 0) };
        header.Controls.Add(left);
        header.Controls.Add(actions);

        // ---- Tab 1: revenue -------------------------------------------------------------------------------
        _netRevenue = Stat("Doanh thu thuần", Glyphs.Money, BadgeKind.Success);
        _invoiceCount = Stat("Số hóa đơn", Glyphs.Document, BadgeKind.Primary);
        _grossProfit = Stat("Lợi nhuận gộp", Glyphs.Chart, BadgeKind.Info);
        _refunds = Stat("Hoàn trả & hủy", Glyphs.Return, BadgeKind.Warning);
        var revenueStats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, WrapContents = false };
        revenueStats.Controls.AddRange([_netRevenue, _invoiceCount, _grossProfit, _refunds]);

        _revenueChart = new BarChart { Dock = DockStyle.Fill, EmptyText = "Không có doanh thu trong kỳ này", TooltipFormatter = Vnd.Format };
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
        _rankBy = Filter(150, [new Option<ProductRanking>(ProductRanking.Quantity, "Theo số lượng"), new(ProductRanking.Revenue, "Theo doanh thu"), new(ProductRanking.Profit, "Theo lợi nhuận")]);
        _category = Filter(200, new List<Option<int?>> { new(null, "Tất cả danh mục") });
        _top = Filter(110, [new Option<int>(10, "Top 10"), new(20, "Top 20"), new(50, "Top 50"), new(100, "Top 100")]);
        _top.SelectedIndex = 1;
        foreach (var filter in new[] { _rankBy, _category, _top })
        {
            filter.SelectedIndexChanged += async (_, _) =>
            {
                if (!_loadingFilters)
                {
                    _filterVersion++;
                    _stale[BestSellerTab] = true;
                    await ReloadTabAsync(BestSellerTab);
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
        _profitChart = new BarChart { Dock = DockStyle.Fill, ShowValues = true, ValueFormatter = ShortMoney, TooltipFormatter = Vnd.Format, EmptyText = "Chưa có lợi nhuận trong kỳ này" };
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
        _costValue = Stat("Giá trị tồn (giá vốn)", Glyphs.Package, BadgeKind.Primary);
        _retailValue = Stat("Giá trị tồn (giá bán)", Glyphs.Money, BadgeKind.Success);
        _potentialProfit = Stat("Lãi dự kiến", Glyphs.Chart, BadgeKind.Info);
        _stockAlerts = Stat("Hết / sắp hết hàng", Glyphs.Warning, BadgeKind.Warning);
        var inventoryStats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 140, WrapContents = false };
        inventoryStats.Controls.AddRange([_costValue, _retailValue, _potentialProfit, _stockAlerts]);

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
            if (!IsHandleCreated)
            {
                return; // the first page being added; Load does the initial query
            }

            UpdatePeriodHint();
            if (_stale[_tabs.SelectedIndex])
            {
                await ReloadTabAsync(_tabs.SelectedIndex);
            }
        };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        body.Controls.Add(_tabs);

        Controls.Add(body);
        Controls.Add(header);

        Load += async (_, _) =>
        {
            await LoadCategoriesAsync();
            await ReloadTabAsync(_tabs.SelectedIndex);
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                _ = ReloadAllAsync();
                return true;
            case Keys.Control | Keys.E:
                _ = ExportAsync(all: false);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Building blocks -------------------------------------------------------------------------------

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

    private static ModernComboBox Filter<T>(int width, IList<Option<T>> options) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        ReserveMessageSpace = false,
        DisplayMember = nameof(Option<T>.Name),
        ValueMember = nameof(Option<T>.Value),
        DataSource = options,
        Margin = new Padding(8, 0, 0, 0),
    };

    /// <summary>"1,2 tr" / "850 N" – compact bar labels.</summary>
    private static string ShortMoney(decimal amount) => Math.Abs(amount) switch
    {
        >= 1_000_000_000 => $"{(amount / 1_000_000_000).ToString("#,0.#", Vnd.Culture)} tỷ",
        >= 1_000_000 => $"{(amount / 1_000_000).ToString("#,0.#", Vnd.Culture)} tr",
        >= 1_000 => $"{(amount / 1_000).ToString("#,0", Vnd.Culture)} N",
        _ => amount.ToString("#,0", Vnd.Culture),
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

    private ReportPeriod CurrentPeriod => new(_period.Range.From, _period.Range.To);

    private void UpdatePeriodHint()
    {
        _period.Enabled = _tabs.SelectedIndex != InventoryTab;
        if (_tabs.SelectedIndex == InventoryTab)
        {
            _status.Text = "Giá trị tồn kho tính tại thời điểm hiện tại, không theo kỳ.";
        }
    }

    private void SetStatus(string text, LabelTone tone = LabelTone.Muted)
    {
        _status.Tone = tone;
        _status.Text = text;
    }

    // ---- Loading ---------------------------------------------------------------------------------------

    private async Task LoadCategoriesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var categories = await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync();
        if (IsDisposed)
        {
            return;
        }

        if (categories.IsFailure)
        {
            // No products.view: rank across all categories only.
            _category.Visible = false;
            return;
        }

        _loadingFilters = true;
        try
        {
            var options = new List<Option<int?>> { new(null, "Tất cả danh mục") };
            options.AddRange(categories.Value.OrderBy(c => c.Name, StringComparer.CurrentCulture).Select(c => new Option<int?>(c.Id, c.Name)));
            _category.DataSource = options;
            _category.SelectedIndex = 0;
        }
        finally
        {
            _loadingFilters = false;
        }
    }

    private async Task OnFiltersChangedAsync(bool includingInventory)
    {
        _filterVersion++;
        for (var i = 0; i < _stale.Length; i++)
        {
            if (i != InventoryTab || includingInventory)
            {
                _stale[i] = true;
            }
        }

        await ReloadTabAsync(_tabs.SelectedIndex);
    }

    private async Task ReloadAllAsync()
    {
        _period.RefreshToday();
        await OnFiltersChangedAsync(includingInventory: true);
    }

    private async Task ReloadTabAsync(int tab)
    {
        if (_loading || tab < 0)
        {
            return;
        }

        var version = _filterVersion;
        _loading = true;
        UseWaitCursor = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var error = tab switch
            {
                RevenueTab => await LoadRevenueAsync(reports),
                BestSellerTab => await LoadBestSellersAsync(reports),
                ProfitTab => await LoadProfitAsync(reports),
                _ => await LoadInventoryAsync(reports),
            };

            if (IsDisposed)
            {
                return;
            }

            if (error is not null)
            {
                SetStatus(error, LabelTone.Danger);
                return;
            }

            // A filter changed while this query ran: keep the tab stale so it reloads below.
            _stale[tab] = version != _filterVersion;
            if (tab == InventoryTab)
            {
                UpdatePeriodHint();
            }
            else
            {
                SetStatus($"{_period.Describe()} · cập nhật lúc {DateTime.Now:HH:mm}");
            }
        }
        finally
        {
            _loading = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
            }
        }

        // The user may have switched tab or changed a filter while this one was loading.
        if (!IsDisposed && _stale[_tabs.SelectedIndex] && (_tabs.SelectedIndex != tab || version != _filterVersion))
        {
            await ReloadTabAsync(_tabs.SelectedIndex);
        }
    }

    private async Task<string?> LoadRevenueAsync(IReportService reports)
    {
        var period = CurrentPeriod;
        var report = await reports.GetRevenueAsync(period);
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        var previous = await reports.GetRevenueAsync(ReportFigures.PreviousPeriod(period));
        if (IsDisposed)
        {
            return null;
        }

        var data = report.Value;
        var totals = data.Totals;
        var before = previous.IsSuccess ? previous.Value.Totals : null;

        _netRevenue.Value = Vnd.Format(totals.NetRevenue);
        _netRevenue.SetTrend(before is null ? null : ReportFigures.ChangePercent(totals.NetRevenue, before.NetRevenue), "kỳ trước");
        _netRevenue.Note = before is null || before.NetRevenue <= 0 ? $"Doanh số {Vnd.Format(totals.SalesTotal)}" : null;

        _invoiceCount.Value = totals.InvoiceCount.ToString("#,0", Vnd.Culture);
        _invoiceCount.Note = totals.InvoiceCount == 0 ? "Chưa có hóa đơn" : $"TB {Vnd.Format(totals.AverageBasket)} / hóa đơn";

        _grossProfit.Value = Vnd.Format(totals.GrossProfit);
        _grossProfit.Accent = totals.GrossProfit < 0 ? BadgeKind.Danger : BadgeKind.Info;
        _grossProfit.Note = $"Tỷ suất {totals.MarginPercent.ToString("0.0", Vnd.Culture)}% · giá vốn {Vnd.Format(totals.CostOfGoods)}";

        _refunds.Value = Vnd.Format(totals.RefundTotal);
        _refunds.Note = $"{totals.ReturnCount:#,0} phiếu trả · {totals.VoidedCount:#,0} HĐ hủy";

        var buckets = ReportFigures.RevenueBuckets(data);
        _revenueChart.ShowValues = buckets.Count <= 14;
        _revenueChart.ValueFormatter = ShortMoney;
        _revenueChart.Points = buckets
            .Select(b => new ChartPoint(
                b.Label,
                b.NetRevenue,
                (b.From == b.To ? DashboardFigures.LongDayLabel(b.From) : $"{b.From:dd/MM/yyyy} – {b.To:dd/MM/yyyy}")
                + $"\nDoanh thu thuần: {Vnd.Format(b.NetRevenue)}\nLợi nhuận: {Vnd.Format(b.GrossProfit)}\nHóa đơn: {b.InvoiceCount:#,0}"))
            .ToList();
        var best = ReportFigures.BestDay(data);
        _revenueChartCard.Title = data.Period.Days > ReportFigures.MaxDailyBars ? "Doanh thu thuần theo tháng" : "Doanh thu thuần theo ngày";
        _revenueChartCard.Subtitle = best is null
            ? "Không có doanh thu"
            : $"Tổng {Vnd.Format(totals.NetRevenue)} · TB {Vnd.Format(Vnd.Round(totals.NetRevenue / data.Period.Days))}/ngày · cao nhất {best.Date:dd/MM} ({Vnd.Format(best.NetRevenue)})";

        _revenueGrid.SetItems(data.Days
            .OrderByDescending(d => d.Date)
            .Select(d => new RevenueRow(
                d.Date!.Value.ToDateTime(TimeOnly.MinValue),
                d.InvoiceCount,
                d.SalesTotal,
                d.DiscountTotal,
                d.RefundTotal,
                d.NetRevenue,
                d.VatAmount,
                d.CostOfGoods,
                d.GrossProfit,
                d.MarginPercent,
                d.CashCollected,
                d.TransferCollected + d.CardCollected))
            .ToList());
        return null;
    }

    private async Task<string?> LoadBestSellersAsync(IReportService reports)
    {
        var rankBy = _rankBy.SelectedValue is ProductRanking r ? r : ProductRanking.Quantity;
        var top = _top.SelectedValue is int t ? t : 20;
        var categoryId = _category.Visible ? _category.SelectedValue as int? : null;
        var report = await reports.GetBestSellersAsync(new BestSellerQuery(CurrentPeriod, rankBy, top, categoryId));
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (IsDisposed)
        {
            return null;
        }

        var data = report.Value;
        _bestGrid.SetItems(data.Rows.ToList());
        var shownRevenue = data.Rows.Sum(x => x.Revenue);
        _bestCard.Subtitle = data.Rows.Count == 0
            ? null
            : $"{data.Rows.Count}/{data.ProductCount} sản phẩm có bán · chiếm {ReportFiguresShare(shownRevenue, data.TotalRevenue)} doanh thu ({Vnd.Format(shownRevenue)})";
        return null;
    }

    private static string ReportFiguresShare(decimal part, decimal whole) =>
        whole == 0 ? "0%" : $"{Math.Round(part * 100 / whole, 1, MidpointRounding.AwayFromZero).ToString("0.#", Vnd.Culture)}%";

    private async Task<string?> LoadProfitAsync(IReportService reports)
    {
        var report = await reports.GetProfitByCategoryAsync(CurrentPeriod);
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (IsDisposed)
        {
            return null;
        }

        var data = report.Value;
        var rows = data.Rows.OrderByDescending(x => x.Profit).ToList();
        _profitGrid.SetItems(rows);
        _profitChart.Points = rows
            .Take(12)
            .Select(x => new ChartPoint(
                x.CategoryName,
                x.Profit,
                $"{x.CategoryName}\nDoanh thu: {Vnd.Format(x.Revenue)}\nLợi nhuận: {Vnd.Format(x.Profit)} ({x.MarginPercent.ToString("0.0", Vnd.Culture)}%)"))
            .ToList();
        _profitChartCard.Subtitle = rows.Count == 0
            ? null
            : $"Tổng {Vnd.Format(data.Totals.Profit)} · tỷ suất {data.Totals.MarginPercent.ToString("0.0", Vnd.Culture)}% trên {Vnd.Format(data.Totals.RevenueExclVat)} chưa VAT";
        return null;
    }

    private async Task<string?> LoadInventoryAsync(IReportService reports)
    {
        var report = await reports.GetInventoryValueAsync();
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (IsDisposed)
        {
            return null;
        }

        var data = report.Value;
        var totals = data.Totals;
        _costValue.Value = Vnd.Format(totals.CostValue);
        _costValue.Note = $"{totals.ProductCount:#,0} sản phẩm";
        _retailValue.Value = Vnd.Format(totals.RetailValue);
        _retailValue.Note = $"Chưa VAT {Vnd.Format(totals.RetailValueExclVat)}";
        _potentialProfit.Value = Vnd.Format(totals.PotentialProfit);
        _potentialProfit.Note = "Nếu bán hết theo giá hiện tại";
        _stockAlerts.Value = $"{totals.OutOfStockCount:#,0} / {totals.LowStockCount:#,0}";
        _stockAlerts.Accent = totals.OutOfStockCount > 0 ? BadgeKind.Danger : BadgeKind.Warning;
        _stockAlerts.Note = totals.NegativeStockCount > 0 ? $"{totals.NegativeStockCount:#,0} SP tồn âm – kiểm tra phiếu nhập" : "Không có tồn âm";
        _inventoryGrid.SetItems(data.Rows.OrderByDescending(x => x.CostValue).ToList());
        return null;
    }

    // ---- Export ----------------------------------------------------------------------------------------

    private async Task ExportAsync(bool all)
    {
        IReadOnlyList<ReportKind> kinds = all
            ? [ReportKind.Revenue, ReportKind.BestSellers, ReportKind.ProfitByCategory, ReportKind.InventoryValue]
            : [(ReportKind)_tabs.SelectedIndex];

        var categoryId = _category.Visible ? _category.SelectedValue as int? : null;
        var request = new ReportExportRequest(
            kinds,
            CurrentPeriod,
            _rankBy.SelectedValue is ProductRanking r ? r : ProductRanking.Quantity,
            _top.SelectedValue is int t ? t : 20,
            categoryId,
            categoryId is null ? null : _category.Text);

        using var scope = _scopeFactory.CreateScope();
        var exporter = scope.ServiceProvider.GetRequiredService<IReportExportService>();
        UseWaitCursor = true;
        var result = await exporter.ExportAsync(request);
        if (IsDisposed)
        {
            return;
        }

        UseWaitCursor = false;
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu báo cáo",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = result.Value.FileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, result.Value.Content);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Không lưu được file (có thể file đang mở trong Excel).\n{ex.Message}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetStatus($"Đã xuất {Path.GetFileName(dialog.FileName)}", LabelTone.Success);
        if (MessageBox.Show(this, "Đã lưu báo cáo. Mở file ngay?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // No spreadsheet app associated with .xlsx – the file is saved anyway.
            }
        }
    }

    private sealed record Option<T>(T Value, string Name);

    private sealed record RevenueRow(
        DateTime Date,
        int InvoiceCount,
        decimal SalesTotal,
        decimal DiscountTotal,
        decimal RefundTotal,
        decimal NetRevenue,
        decimal VatAmount,
        decimal CostOfGoods,
        decimal GrossProfit,
        decimal MarginPercent,
        decimal Cash,
        decimal NonCash);
}
