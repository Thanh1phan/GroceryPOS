using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
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
/// Passive MVP view – the logic lives in <see cref="PurchasePresenter"/>.
/// </summary>
internal sealed class PurchaseScreen : BaseUserControl, IPurchaseView
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PurchasePresenter _presenter;
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
    private bool _fillingFilters;

    public PurchaseScreen(IServiceProvider services)
    {
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _presenter = services.GetRequiredService<PurchasePresenter>();
        _presenter.Attach(this);

        // ---- KPI row + actions --------------------------------------------------------------------------
        _amount = Stat("Tiền hàng đã nhập", Glyphs.Money, BadgeKind.Primary);
        _debt = Stat("Ghi nợ NCC trong kỳ", Glyphs.Warning, BadgeKind.Warning);
        _count = Stat("Phiếu đã nhập", Glyphs.Document, BadgeKind.Success);
        _drafts = Stat("Phiếu tạm chưa nhập", Glyphs.Edit, BadgeKind.Info);
        var stats = new StatCardRow { Padding = new Padding(24, 0, 24, 0) };
        stats.AddCards(_amount, _debt, _count, _drafts);

        var create = Action("Tạo phiếu (F2)", Glyphs.Add, ButtonVariant.Primary, () => _presenter.NewReceiptAsync(withSuggestions: false));
        var suggested = Action("Nhập hàng sắp hết (F4)", Glyphs.Warning, ButtonVariant.Secondary, () => _presenter.NewReceiptAsync(withSuggestions: true));
        create.Visible = suggested.Visible = _presenter.CanManage;
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        var actions = ScreenLayout.Toolbar(create, suggested, refresh);

        // ---- Filters + list -----------------------------------------------------------------------------
        var period = PurchaseFilter.ThisMonth(DateTime.Today);
        _from = new ModernDatePicker { Width = 140, Value = period.From, ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 140, Value = period.To, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
        _status = new ModernComboBox
        {
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(StatusFilterOption.Name),
            ValueMember = nameof(StatusFilterOption.Status),
            DataSource = StatusFilterOption.All.ToList(),
            Margin = new Padding(8, 0, 0, 0),
        };
        _status.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _supplier = new ModernComboBox
        {
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(SupplierFilterOption.Name),
            ValueMember = nameof(SupplierFilterOption.Id),
            Margin = new Padding(8, 0, 0, 0),
        };
        _supplier.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Không có phiếu nhập nào trong khoảng thời gian này.", SearchPlaceholder = "Tìm số phiếu, NCC…" };
        _grid.AddTextColumn(nameof(PurchaseReceiptRow.Code), "Số phiếu", width: 140);
        _grid.AddDateColumn(nameof(PurchaseReceiptRow.ReceiptDate), "Ngày lập", width: 140, withTime: true);
        _grid.AddTextColumn(nameof(PurchaseReceiptRow.SupplierName), "Nhà cung cấp");
        _grid.AddNumberColumn(nameof(PurchaseReceiptRow.LineCount), "Số SP", width: 70);
        _grid.AddMoneyColumn(nameof(PurchaseReceiptRow.Total), "Tổng tiền", width: 140);
        _grid.AddMoneyColumn(nameof(PurchaseReceiptRow.PaidAmount), "Đã trả", width: 130);
        _grid.AddMoneyColumn(nameof(PurchaseReceiptRow.DebtAmount), "Ghi nợ", width: 130);
        _grid.AddTextColumn(nameof(PurchaseReceiptRow.StatusName), "Trạng thái", width: 120);
        _grid.RowActivated += async (_, e) => await _presenter.OpenAsync((PurchaseReceiptRow)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;

        _open = new ModernButton { Text = PurchasePresenter.OpenText(null), Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Edit, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
        _open.Click += async (_, _) => await _presenter.OpenAsync(Selected);
        _cancel = new ModernButton { Text = "Hủy phiếu tạm", Variant = ButtonVariant.Danger, IconGlyph = Glyphs.Cancel, Enabled = false, Visible = _presenter.CanManage, Margin = new Padding(8, 0, 0, 0) };
        _cancel.Click += async (_, _) => await _presenter.CancelAsync(Selected);

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
        Controls.Add(stats);
        Controls.Add(actions);
    }

    bool IView.IsAlive => IsAlive;

    private PurchaseReceiptRow? Selected => _grid.GetSelected<PurchaseReceiptRow>();

    public override Task RefreshDataAsync() => _presenter.LoadAsync(CurrentFilter());

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2 when _presenter.CanManage:
                Run(() => _presenter.NewReceiptAsync(withSuggestions: false));
                return true;
            case Keys.F4 when _presenter.CanManage:
                Run(() => _presenter.NewReceiptAsync(withSuggestions: true));
                return true;
            case Keys.Delete when _grid.InnerGrid.ContainsFocus:
                Run(() => _presenter.CancelAsync(Selected));
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Fire-and-forget from a key press; exceptions still reach the global handler.</summary>
    private static async void Run(Func<Task> work) => await work();

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

    private PurchaseFilter CurrentFilter() => new(
        _from.Value,
        _to.Value,
        _status.SelectedValue as GoodsReceiptStatus?,
        _supplier.SelectedValue is int id and > 0 ? id : null);

    private Task FilterChangedAsync() => _fillingFilters ? Task.CompletedTask : _presenter.FilterAsync(CurrentFilter());

    private void UpdateButtons()
    {
        var row = Selected;
        _open.Enabled = row is not null;
        _open.Text = PurchasePresenter.OpenText(row);
        _cancel.Enabled = _presenter.CanCancel(row);
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not PurchaseReceiptRow row || e.CellStyle is not { } style)
        {
            return;
        }

        var tone = _grid.Columns[e.ColumnIndex].DataPropertyName switch
        {
            nameof(PurchaseReceiptRow.StatusName) => row.StatusTone,
            nameof(PurchaseReceiptRow.DebtAmount) => row.DebtTone,
            _ => row.RowTone,
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

    void IPurchaseView.ShowSupplierFilter(IReadOnlyList<SupplierFilterOption> options)
    {
        var selected = _supplier.SelectedValue as int?;
        _fillingFilters = true;
        try
        {
            _supplier.DataSource = options.ToList();
            _supplier.SelectedValue = selected is { } id && options.Any(o => o.Id == id) ? id : SupplierFilterOption.All.Id;
        }
        finally
        {
            _fillingFilters = false;
        }
    }

    void IPurchaseView.ShowReceipts(IReadOnlyList<PurchaseReceiptRow> rows)
    {
        _grid.SetItems(rows);
        UpdateButtons();
    }

    void IPurchaseView.ShowSummary(PurchaseSummary summary)
    {
        _amount.Value = summary.AmountText;
        _amount.Note = summary.AmountNote;
        _debt.Value = summary.DebtText;
        _debt.Note = summary.DebtNote;
        _count.Value = summary.CountText;
        _count.Note = summary.CountNote;
        _drafts.Value = summary.DraftsText;
        _drafts.Note = summary.DraftsNote;
    }

    void IPurchaseView.ShowPeriodError(string? message)
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

    bool IPurchaseView.OpenEditor(GoodsReceiptDraft draft, IReadOnlyList<SupplierDto> suppliers)
    {
        using var editor = new GoodsReceiptEditorForm(_scopeFactory, draft, suppliers);
        return editor.ShowDialog(this) == DialogResult.OK || editor.Saved;
    }

    void IPurchaseView.OpenDetail(GoodsReceiptDto receipt, TimeSpan storeOffset)
    {
        using var detail = new GoodsReceiptDetailForm(receipt, storeOffset);
        detail.ShowDialog(this);
    }

    string? IPurchaseView.AskReason(string title, string message, IReadOnlyList<string> suggestions)
    {
        using var prompt = new ReasonPromptDialog(title, message, suggestions);
        return prompt.ShowDialog(this) == DialogResult.OK ? prompt.Reason ?? string.Empty : null;
    }

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
