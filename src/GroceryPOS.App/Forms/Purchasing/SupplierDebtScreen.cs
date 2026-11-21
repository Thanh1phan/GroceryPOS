using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Công nợ NCC: KPI cards (total payable, paid and returned in the period, largest debt), a "Công nợ phải trả" tab
/// listing suppliers the store owes, a "Phiếu chi" tab with the payments of the period (cancel with a reason) and a
/// "Trả hàng NCC" tab with the supplier returns. Thanh toán (F6), Sổ công nợ (F7), Trả hàng NCC (F8), F5 reloads.
/// Passive MVP view – the logic lives in <see cref="SupplierDebtPresenter"/>.
/// </summary>
internal sealed class SupplierDebtScreen : BaseUserControl, ISupplierDebtView
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupplierDebtPresenter _presenter;
    private readonly StatCard _total;
    private readonly StatCard _paid;
    private readonly StatCard _returned;
    private readonly StatCard _largest;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly ModernTabControl _tabs;
    private readonly ModernTabPage _payablesPage;
    private readonly ModernTabPage _paymentsPage;
    private readonly ModernTabPage? _returnsPage;
    private readonly ModernDataGrid _payablesGrid;
    private readonly ModernDataGrid _paymentsGrid;
    private readonly ModernDataGrid? _returnsGrid;
    private readonly ModernButton _pay;
    private readonly ModernButton _ledger;
    private readonly ModernButton _cancelPayment;

    public SupplierDebtScreen(IServiceProvider services)
    {
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _presenter = services.GetRequiredService<SupplierDebtPresenter>();
        _presenter.Attach(this);

        // ---- KPI row + actions --------------------------------------------------------------------------
        _total = Stat("Tổng nợ phải trả", Glyphs.Money, BadgeKind.Warning);
        _paid = Stat("Đã trả NCC trong kỳ", Glyphs.Accept, BadgeKind.Success);
        _returned = Stat("Trả hàng NCC trong kỳ", Glyphs.Return, BadgeKind.Info);
        _largest = Stat("Nợ lớn nhất", Glyphs.People, BadgeKind.Primary);
        var stats = new StatCardRow { Padding = new Padding(24, 0, 24, 0) };
        stats.AddCards(_total, _paid, _returned, _largest);

        _pay = Action("Thanh toán (F6)", Glyphs.Money, ButtonVariant.Primary, () => _presenter.PayAsync(SelectedSupplierId));
        _pay.Visible = _presenter.CanPay;
        _ledger = Action("Sổ công nợ (F7)", Glyphs.History, ButtonVariant.Secondary, () =>
        {
            _presenter.ShowLedger(SelectedSupplierId);
            return Task.CompletedTask;
        });
        var returns = Action("Trả hàng NCC (F8)", Glyphs.Return, ButtonVariant.Secondary, () => _presenter.NewReturnAsync(PayableSupplierId));
        returns.Visible = _presenter.CanReturn;
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        var actions = ScreenLayout.Toolbar(_pay, _ledger, returns, refresh);

        // ---- Period filter (payments, returns, KPIs) ---------------------------------------------------
        var period = DocumentPeriod.ThisMonth(DateTime.Today);
        _from = new ModernDatePicker { Width = 140, Value = period.From, ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 140, Value = period.To, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await _presenter.FilterAsync(CurrentPeriod());
        _to.ValueChanged += async (_, _) => await _presenter.FilterAsync(CurrentPeriod());
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false, Padding = new Padding(24, 4, 24, 0) };
        filter.Controls.AddRange([
            new Label { Text = "Kỳ báo cáo: từ", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _from,
            new Label { Text = "đến", AutoSize = true, Margin = new Padding(8, 12, 6, 0) }, _to,
        ]);

        // ---- Payables tab -------------------------------------------------------------------------------
        _payablesGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Cửa hàng không còn nợ nhà cung cấp nào. 👍", SearchPlaceholder = "Tìm mã, tên, SĐT nhà cung cấp…" };
        _payablesGrid.AddTextColumn(nameof(SupplierPayableDto.Code), "Mã NCC", width: 110);
        _payablesGrid.AddTextColumn(nameof(SupplierPayableDto.Name), "Nhà cung cấp");
        _payablesGrid.AddTextColumn(nameof(SupplierPayableDto.Phone), "Điện thoại", width: 140);
        _payablesGrid.AddMoneyColumn(nameof(SupplierPayableDto.DebtBalance), "Còn nợ", width: 160);
        _payablesGrid.RowActivated += async (_, e) =>
        {
            var id = ((SupplierPayableDto)e.Item).SupplierId;
            if (_presenter.CanPay)
            {
                await _presenter.PayAsync(id);
            }
            else
            {
                _presenter.ShowLedger(id);
            }
        };
        _payablesGrid.SelectionChanged += (_, _) => UpdateButtons();
        _payablesPage = new ModernTabPage("Công nợ phải trả") { IconGlyph = Glyphs.Money, BadgeKind = BadgeKind.Warning, Padding = new Padding(0, 8, 0, 0) };
        _payablesPage.Controls.Add(_payablesGrid);

        // ---- Payments tab -------------------------------------------------------------------------------
        _paymentsGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có phiếu chi trả nợ NCC trong kỳ.", SearchPlaceholder = "Tìm số phiếu, NCC, chứng từ…" };
        _paymentsGrid.AddTextColumn(nameof(SupplierPaymentRow.Code), "Số phiếu", width: 140);
        _paymentsGrid.AddDateColumn(nameof(SupplierPaymentRow.PaidAt), "Ngày chi", width: 140, withTime: true);
        _paymentsGrid.AddTextColumn(nameof(SupplierPaymentRow.SupplierName), "Nhà cung cấp");
        _paymentsGrid.AddTextColumn(nameof(SupplierPaymentRow.MethodName), "Hình thức", width: 120);
        _paymentsGrid.AddMoneyColumn(nameof(SupplierPaymentRow.Amount), "Số tiền", width: 130);
        _paymentsGrid.AddMoneyColumn(nameof(SupplierPaymentRow.DebtAfter), "Nợ sau chi", width: 130);
        _paymentsGrid.AddTextColumn(nameof(SupplierPaymentRow.Reference), "Chứng từ", width: 130);
        _paymentsGrid.AddTextColumn(nameof(SupplierPaymentRow.StatusName), "Trạng thái", width: 170);
        _paymentsGrid.SelectionChanged += (_, _) => UpdateButtons();
        _paymentsGrid.RowActivated += (_, e) => _presenter.ShowLedger(((SupplierPaymentRow)e.Item).SupplierId);
        _paymentsGrid.InnerGrid.CellFormatting += OnPaymentCellFormatting;
        _cancelPayment = new ModernButton { Text = "Hủy phiếu chi", Variant = ButtonVariant.Danger, IconGlyph = Glyphs.Cancel, Enabled = false, Visible = _presenter.CanPay, Margin = new Padding(8, 0, 0, 0) };
        _cancelPayment.Click += async (_, _) => await _presenter.CancelPaymentAsync(_paymentsGrid.GetSelected<SupplierPaymentRow>());
        _paymentsGrid.ToolbarItems.Controls.Add(_cancelPayment);
        _paymentsPage = new ModernTabPage("Phiếu chi") { IconGlyph = Glyphs.Document, Padding = new Padding(0, 8, 0, 0) };
        _paymentsPage.Controls.Add(_paymentsGrid);

        _tabs = new ModernTabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(_payablesPage);
        _tabs.TabPages.Add(_paymentsPage);

        // ---- Returns tab (purchases.manage) ------------------------------------------------------------
        if (_presenter.CanReturn)
        {
            _returnsGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có phiếu trả hàng NCC trong kỳ.", SearchPlaceholder = "Tìm số phiếu, NCC, phiếu nhập…" };
            _returnsGrid.AddTextColumn(nameof(SupplierReturnRow.Code), "Số phiếu", width: 140);
            _returnsGrid.AddDateColumn(nameof(SupplierReturnRow.ReturnDate), "Ngày trả", width: 140, withTime: true);
            _returnsGrid.AddTextColumn(nameof(SupplierReturnRow.SupplierName), "Nhà cung cấp");
            _returnsGrid.AddTextColumn(nameof(SupplierReturnRow.GoodsReceiptCode), "Theo phiếu nhập", width: 140);
            _returnsGrid.AddNumberColumn(nameof(SupplierReturnRow.LineCount), "Số SP", width: 70);
            _returnsGrid.AddMoneyColumn(nameof(SupplierReturnRow.Total), "Giá trị", width: 130);
            _returnsGrid.AddMoneyColumn(nameof(SupplierReturnRow.RefundAmount), "NCC hoàn tiền", width: 130);
            _returnsGrid.AddMoneyColumn(nameof(SupplierReturnRow.DebtOffset), "Cấn trừ nợ", width: 130);
            _returnsGrid.SelectionChanged += (_, _) => UpdateButtons();
            _returnsGrid.RowActivated += async (_, e) => await _presenter.OpenReturnAsync((SupplierReturnRow)e.Item);
            _returnsPage = new ModernTabPage("Trả hàng NCC") { IconGlyph = Glyphs.Return, Padding = new Padding(0, 8, 0, 0) };
            _returnsPage.Controls.Add(_returnsGrid);
            _tabs.TabPages.Add(_returnsPage);
        }

        _tabs.SelectedIndexChanged += (_, _) => UpdateButtons();

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 24) };
        body.Controls.Add(_tabs);

        Controls.Add(body);
        Controls.Add(filter);
        Controls.Add(stats);
        Controls.Add(actions);
        UpdateButtons();
    }

    bool IView.IsAlive => IsAlive;

    /// <summary>Supplier of the selected row on the current tab.</summary>
    private int? SelectedSupplierId =>
        _tabs.SelectedTab == _payablesPage ? _payablesGrid.GetSelected<SupplierPayableDto>()?.SupplierId
        : _tabs.SelectedTab == _paymentsPage ? _paymentsGrid.GetSelected<SupplierPaymentRow>()?.SupplierId
        : _returnsGrid?.GetSelected<SupplierReturnRow>()?.SupplierId;

    /// <summary>A new return starts with the supplier picked on the payables tab.</summary>
    private int? PayableSupplierId =>
        _tabs.SelectedTab == _payablesPage ? _payablesGrid.GetSelected<SupplierPayableDto>()?.SupplierId : null;

    public override Task RefreshDataAsync() => _presenter.LoadAsync(CurrentPeriod());

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F6 when _presenter.CanPay:
                Run(() => _presenter.PayAsync(SelectedSupplierId));
                return true;
            case Keys.F7:
                _presenter.ShowLedger(SelectedSupplierId);
                return true;
            case Keys.F8 when _presenter.CanReturn:
                Run(() => _presenter.NewReturnAsync(PayableSupplierId));
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

    private DocumentPeriod CurrentPeriod() => new(_from.Value, _to.Value);

    private void UpdateButtons()
    {
        var supplierId = SelectedSupplierId;
        _ledger.Enabled = supplierId is not null;
        _pay.Enabled = _presenter.CanPayFor(supplierId);
        _cancelPayment.Enabled = _presenter.CanCancel(_paymentsGrid.GetSelected<SupplierPaymentRow>());
    }

    private void OnPaymentCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _paymentsGrid.InnerGrid.Rows[e.RowIndex].Tag is not SupplierPaymentRow row || e.CellStyle is not { } style)
        {
            return;
        }

        var tone = _paymentsGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(SupplierPaymentRow.StatusName) && row.IsCancelled
            ? row.StatusTone
            : row.RowTone;
        var colors = ThemeManager.Current.Colors;
        style.ForeColor = tone switch
        {
            Tone.Muted => colors.TextMuted,
            Tone.Danger => colors.Danger,
            _ => style.ForeColor,
        };
    }

    void ISupplierDebtView.ShowPayables(IReadOnlyList<SupplierPayableDto> payables)
    {
        _payablesGrid.SetItems(payables);
        _payablesPage.BadgeCount = payables.Count;
        UpdateButtons();
    }

    void ISupplierDebtView.ShowPayments(IReadOnlyList<SupplierPaymentRow> rows)
    {
        _paymentsGrid.SetItems(rows);
        UpdateButtons();
    }

    void ISupplierDebtView.ShowReturns(IReadOnlyList<SupplierReturnRow> rows)
    {
        _returnsGrid?.SetItems(rows);
        UpdateButtons();
    }

    void ISupplierDebtView.ShowSummary(SupplierDebtSummary summary)
    {
        _total.Value = summary.TotalText;
        _total.Note = summary.TotalNote;
        _paid.Value = summary.PaidText;
        _paid.Note = summary.PaidNote;
        _returned.Value = summary.ReturnedText;
        _returned.Note = summary.ReturnedNote;
        _largest.Value = summary.LargestText;
        _largest.Note = summary.LargestNote;
    }

    void ISupplierDebtView.ShowPeriodError(string? message)
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

    SupplierPaymentDto? ISupplierDebtView.OpenPayment(SupplierPayableDto payable)
    {
        using var dialog = new SupplierPaymentDialog(_scopeFactory, payable);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Payment : null;
    }

    void ISupplierDebtView.OpenLedger(int supplierId)
    {
        using var form = new SupplierDebtLedgerForm(_scopeFactory, supplierId);
        form.ShowDialog(this);
    }

    SupplierReturnReceipt? ISupplierDebtView.OpenReturnEditor(IReadOnlyList<StockItem> catalog, IReadOnlyList<SupplierDto> suppliers, int? supplierId)
    {
        using var editor = new SupplierReturnEditorForm(_scopeFactory, catalog, suppliers, supplierId: supplierId);
        return editor.ShowDialog(this) == DialogResult.OK ? editor.Posted : null;
    }

    void ISupplierDebtView.OpenReturnDetail(SupplierReturnDto supplierReturn, TimeSpan storeOffset)
    {
        using var form = new SupplierReturnDetailForm(supplierReturn, storeOffset);
        form.ShowDialog(this);
    }

    string? ISupplierDebtView.AskReason(string title, string message, IReadOnlyList<string> suggestions)
    {
        using var prompt = new ReasonPromptDialog(title, message, suggestions, required: true);
        return prompt.ShowDialog(this) == DialogResult.OK ? prompt.Reason : null;
    }

    void ISupplierDebtView.SelectReturnsTab()
    {
        if (_returnsPage is not null)
        {
            _tabs.SelectedTab = _returnsPage;
        }
    }

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
