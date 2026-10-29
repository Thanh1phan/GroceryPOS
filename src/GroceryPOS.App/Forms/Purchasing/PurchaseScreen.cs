using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Nhập hàng: KPI cards for the period (amount received, new debt, receipts, open drafts) above the receipt list
/// filtered by date range, status and supplier. Tạo phiếu (F2), Nhập hàng sắp hết (F4) pre-fills a receipt with
/// the reorder suggestions, Enter opens a draft in the editor or shows a completed/cancelled receipt, Hủy phiếu tạm (Del).
/// </summary>
internal sealed class PurchaseScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canChangePrices;
    private readonly SalesOptions _options;
    private readonly StatCard _amount;
    private readonly StatCard _debt;
    private readonly StatCard _count;
    private readonly StatCard _drafts;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly ModernComboBox _status;
    private readonly ModernComboBox _supplier;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _open;
    private readonly ModernButton _cancel;
    private IReadOnlyList<SupplierDto> _suppliers = [];
    private bool _loadingFilters;

    public PurchaseScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canChangePrices = currentUser.HasPermission(Permissions.PricesChange);
        using (var scope = scopeFactory.CreateScope())
        {
            _options = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
        }

        Dock = DockStyle.Fill;

        // ---- KPI row + actions --------------------------------------------------------------------------
        _amount = Stat("Tiền hàng đã nhập", Glyphs.Money, BadgeKind.Primary);
        _debt = Stat("Ghi nợ NCC trong kỳ", Glyphs.Warning, BadgeKind.Warning);
        _count = Stat("Phiếu đã nhập", Glyphs.Document, BadgeKind.Success);
        _drafts = Stat("Phiếu tạm chưa nhập", Glyphs.Edit, BadgeKind.Info);
        var stats = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        stats.Controls.AddRange([_amount, _debt, _count, _drafts]);

        var create = Action("Tạo phiếu (F2)", Glyphs.Add, ButtonVariant.Primary, () => NewReceiptAsync(withSuggestions: false));
        var suggested = Action("Nhập hàng sắp hết (F4)", Glyphs.Warning, ButtonVariant.Secondary, () => NewReceiptAsync(withSuggestions: true));
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, ReloadAsync);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 24, 0, 0) };
        actions.Controls.AddRange([create, suggested, refresh]);

        var header = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(24, 20, 24, 0) };
        header.Controls.Add(stats);
        header.Controls.Add(actions);

        // ---- Filters + list -----------------------------------------------------------------------------
        var today = DateTime.Today;
        _from = new ModernDatePicker { Width = 140, Value = new DateTime(today.Year, today.Month, 1), ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 140, Value = today, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await ReloadListAsync();
        _to.ValueChanged += async (_, _) => await ReloadListAsync();
        _status = new ModernComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(StatusOption.Name),
            ValueMember = nameof(StatusOption.Status),
            DataSource = new List<StatusOption>
            {
                new(null, "Tất cả trạng thái"),
                new(GoodsReceiptStatus.Draft, GoodsReceiptStatus.Draft.DisplayName()),
                new(GoodsReceiptStatus.Completed, GoodsReceiptStatus.Completed.DisplayName()),
                new(GoodsReceiptStatus.Cancelled, GoodsReceiptStatus.Cancelled.DisplayName()),
            },
            Margin = new Padding(8, 0, 0, 0),
        };
        _status.SelectedIndexChanged += async (_, _) => await ReloadListAsync();
        _supplier = new ModernComboBox
        {
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(SupplierFilterOption.Name),
            ValueMember = nameof(SupplierFilterOption.Id),
            Margin = new Padding(8, 0, 0, 0),
        };
        _supplier.SelectedIndexChanged += async (_, _) => await ReloadListAsync();

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có phiếu nhập nào trong khoảng thời gian này.", SearchPlaceholder = "Tìm số phiếu, NCC…" };
        _grid.AddTextColumn(nameof(ReceiptRow.Code), "Số phiếu", width: 140);
        _grid.AddDateColumn(nameof(ReceiptRow.ReceiptDate), "Ngày lập", width: 140, withTime: true);
        _grid.AddTextColumn(nameof(ReceiptRow.SupplierName), "Nhà cung cấp");
        _grid.AddNumberColumn(nameof(ReceiptRow.LineCount), "Số SP", width: 70);
        _grid.AddMoneyColumn(nameof(ReceiptRow.Total), "Tổng tiền", width: 140);
        _grid.AddMoneyColumn(nameof(ReceiptRow.PaidAmount), "Đã trả", width: 130);
        _grid.AddMoneyColumn(nameof(ReceiptRow.DebtAmount), "Ghi nợ", width: 130);
        _grid.AddTextColumn(nameof(ReceiptRow.StatusName), "Trạng thái", width: 120);
        _grid.RowActivated += async (_, e) => await OpenAsync((ReceiptRow)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;

        _open = new ModernButton { Text = "Mở phiếu", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Edit, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
        _open.Click += async (_, _) =>
        {
            if (_grid.GetSelected<ReceiptRow>() is { } row)
            {
                await OpenAsync(row);
            }
        };
        _cancel = new ModernButton { Text = "Hủy phiếu tạm", Variant = ButtonVariant.Danger, IconGlyph = Glyphs.Cancel, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
        _cancel.Click += async (_, _) => await CancelSelectedAsync();

        _grid.ToolbarItems.Controls.AddRange([
            new Label { Text = "Từ", AutoSize = true, Margin = new Padding(0, 12, 4, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(8, 12, 4, 0) }, _to,
            _status, _supplier, _open, _cancel,
        ]);

        var card = new ModernCard { Dock = DockStyle.Fill, Title = "Phiếu nhập hàng" };
        card.Controls.Add(_grid);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        body.Controls.Add(card);

        Controls.Add(body);
        Controls.Add(header);

        Load += async (_, _) => await ReloadAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2:
                _ = NewReceiptAsync(withSuggestions: false);
                return true;
            case Keys.F4:
                _ = NewReceiptAsync(withSuggestions: true);
                return true;
            case Keys.F5:
                _ = ReloadAsync();
                return true;
            case Keys.Delete when _grid.InnerGrid.ContainsFocus:
                _ = CancelSelectedAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
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
        Margin = new Padding(0, 0, 16, 0),
    };

    private DateTime StoreTime(DateTimeOffset value) => value.ToOffset(_options.StoreUtcOffset).DateTime;

    private void UpdateButtons()
    {
        var row = _grid.GetSelected<ReceiptRow>();
        _open.Enabled = row is not null;
        _open.Text = row?.Status == GoodsReceiptStatus.Draft ? "Sửa phiếu tạm" : "Xem phiếu";
        _cancel.Enabled = row?.Status == GoodsReceiptStatus.Draft;
    }

    private async Task ReloadAsync()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var suppliers = await scope.ServiceProvider.GetRequiredService<ISupplierService>().ListAsync();
            if (IsDisposed)
            {
                return;
            }

            _suppliers = suppliers.IsSuccess ? suppliers.Value : [];
        }

        var selected = _supplier.SelectedValue as int?;
        _loadingFilters = true;
        try
        {
            _supplier.DataSource = new[] { new SupplierFilterOption(0, "Tất cả NCC") }
                .Concat(_suppliers.Select(s => new SupplierFilterOption(s.Id, s.Name)))
                .ToList();
            _supplier.SelectedValue = selected ?? 0;
        }
        finally
        {
            _loadingFilters = false;
        }

        await ReloadListAsync();
    }

    private async Task ReloadListAsync()
    {
        if (_loadingFilters)
        {
            return;
        }

        var from = _from.Value?.Date ?? DateTime.Today.AddDays(-30);
        var to = (_to.Value?.Date ?? DateTime.Today).AddDays(1);
        if (from >= to)
        {
            _from.SetError("Từ ngày phải trước đến ngày.");
            return;
        }

        _from.ClearValidation();
        var supplierId = _supplier.SelectedValue is int id and > 0 ? id : (int?)null;
        var filter = new GoodsReceiptFilter(
            From: new DateTimeOffset(from, _options.StoreUtcOffset),
            To: new DateTimeOffset(to, _options.StoreUtcOffset),
            SupplierId: supplierId,
            Status: _status.SelectedValue as GoodsReceiptStatus?);

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>();
        var result = await service.SearchAsync(filter);
        // Open drafts are counted whatever the period: a forgotten draft from last month still needs attention.
        var drafts = await service.SearchAsync(new GoodsReceiptFilter(Status: GoodsReceiptStatus.Draft));
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var rows = result.Value.Select(r => new ReceiptRow(
            r.Id, r.Code, StoreTime(r.ReceiptDate), r.Status, r.StatusName, r.SupplierName, r.LineCount, r.Total, r.PaidAmount, r.DebtAmount)).ToList();
        _grid.SetItems(rows);

        var completed = result.Value.Where(r => r.Status == GoodsReceiptStatus.Completed).ToList();
        _amount.Value = Vnd.Format(completed.Sum(r => r.Total));
        _amount.Note = $"Đã trả {Vnd.Format(completed.Sum(r => r.PaidAmount))}";
        _debt.Value = Vnd.Format(completed.Sum(r => r.DebtAmount));
        _debt.Note = $"Tổng công nợ NCC {Vnd.Format(_suppliers.Sum(s => s.DebtBalance))}";
        _count.Value = completed.Count.ToString("#,0", Vnd.Culture);
        _count.Note = $"{completed.Select(r => r.SupplierId).Distinct().Count()} nhà cung cấp";
        var draftList = drafts.IsSuccess ? drafts.Value : [];
        _drafts.Value = draftList.Count.ToString("#,0", Vnd.Culture);
        _drafts.Note = draftList.Count > 0 ? $"Cũ nhất: {StoreTime(draftList.Min(d => d.ReceiptDate)):dd/MM/yyyy}" : "Không có phiếu tồn đọng";
        UpdateButtons();
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not ReceiptRow row || e.CellStyle is not { } style)
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        switch (row.Status)
        {
            case GoodsReceiptStatus.Cancelled:
                style.ForeColor = colors.TextMuted;
                break;
            case GoodsReceiptStatus.Draft when _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(ReceiptRow.StatusName):
                style.ForeColor = colors.Info;
                break;
            case GoodsReceiptStatus.Completed when _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(ReceiptRow.DebtAmount) && row.DebtAmount > 0:
                style.ForeColor = colors.Warning;
                break;
        }
    }

    /// <summary>Products and suppliers the editor needs; null (after telling the user) when they cannot be loaded.</summary>
    private async Task<(IReadOnlyList<StockItem> Catalog, IReadOnlyList<SupplierDto> Suppliers)?> LoadEditorDataAsync(IServiceProvider services)
    {
        var catalog = await services.GetRequiredService<IInventoryService>().GetStockListAsync();
        var suppliers = await services.GetRequiredService<ISupplierService>().ListAsync();
        if (IsDisposed)
        {
            return null;
        }

        if (catalog.IsFailure || suppliers.IsFailure)
        {
            MessageBox.Show(this, (catalog.IsFailure ? catalog.Error : suppliers.Error).Message, "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        if (!suppliers.Value.Any(s => s.IsActive))
        {
            MessageBox.Show(this, "Chưa có nhà cung cấp nào đang giao dịch. Hãy thêm nhà cung cấp trước khi nhập hàng.", "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return (catalog.Value, suppliers.Value);
    }

    private async Task NewReceiptAsync(bool withSuggestions)
    {
        GoodsReceiptDraft draft;
        IReadOnlyList<SupplierDto> suppliers;
        using (var scope = _scopeFactory.CreateScope())
        {
            if (await LoadEditorDataAsync(scope.ServiceProvider) is not { } data)
            {
                return;
            }

            suppliers = data.Suppliers;
            draft = new GoodsReceiptDraft(data.Catalog, _canChangePrices);
            if (_supplier.SelectedValue is int id and > 0 && suppliers.Any(s => s.Id == id && s.IsActive))
            {
                draft.SetSupplier(id);
            }

            if (withSuggestions)
            {
                var suggestions = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().SuggestReorderAsync();
                if (IsDisposed)
                {
                    return;
                }

                if (suggestions.IsSuccess && draft.AddSuggestions(suggestions.Value) == 0)
                {
                    MessageBox.Show(this, "Không có sản phẩm nào dưới định mức tồn kho. Mở phiếu trống.", "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        await ShowEditorAsync(draft, suppliers);
    }

    private async Task OpenAsync(ReceiptRow row)
    {
        using var scope = _scopeFactory.CreateScope();
        var receipt = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().GetAsync(row.Id);
        if (IsDisposed)
        {
            return;
        }

        if (receipt.IsFailure)
        {
            MessageBox.Show(this, receipt.Error.Message, "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (receipt.Value.Status != GoodsReceiptStatus.Draft)
        {
            using var detail = new GoodsReceiptDetailForm(receipt.Value, _options.StoreUtcOffset);
            detail.ShowDialog(this);
            return;
        }

        if (await LoadEditorDataAsync(scope.ServiceProvider) is not { } data)
        {
            return;
        }

        var draft = GoodsReceiptDraft.FromDto(data.Catalog, _canChangePrices, receipt.Value);
        await ShowEditorAsync(draft, data.Suppliers);
    }

    private async Task ShowEditorAsync(GoodsReceiptDraft draft, IReadOnlyList<SupplierDto> suppliers)
    {
        using var editor = new GoodsReceiptEditorForm(_scopeFactory, draft, suppliers);
        var result = editor.ShowDialog(this);
        if (result == DialogResult.OK || editor.Saved)
        {
            await ReloadAsync();
        }
    }

    private async Task CancelSelectedAsync()
    {
        if (_grid.GetSelected<ReceiptRow>() is not { Status: GoodsReceiptStatus.Draft } row)
        {
            return;
        }

        using var prompt = new ReasonPromptDialog(
            "Hủy phiếu tạm",
            $"Hủy phiếu tạm {row.Code} ({row.SupplierName}, {Vnd.Format(row.Total)})? Tồn kho và công nợ không thay đổi.",
            ["Nhập trùng", "NCC không giao hàng", "Lập sai NCC"]);
        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().CancelAsync(row.Id, prompt.Reason);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Nhập hàng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadListAsync();
    }

    private sealed record StatusOption(GoodsReceiptStatus? Status, string Name);

    private sealed record SupplierFilterOption(int Id, string Name);

    private sealed record ReceiptRow(
        int Id,
        string Code,
        DateTime ReceiptDate,
        GoodsReceiptStatus Status,
        string StatusName,
        string SupplierName,
        int LineCount,
        decimal Total,
        decimal PaidAmount,
        decimal DebtAmount);
}
