using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
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
/// "Trả hàng NCC" tab with the supplier returns. Thanh toán (F6), Sổ công nợ (F7), Trả hàng NCC (F8).
/// Paying needs <c>suppliers.pay</c>; returns need <c>purchases.manage</c>.
/// </summary>
internal sealed class SupplierDebtScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canPay;
    private readonly bool _canReturn;
    private readonly TimeSpan _storeOffset;
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
    private IReadOnlyList<SupplierPayableDto> _payables = [];

    public SupplierDebtScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canPay = currentUser.HasPermission(Permissions.SupplierPaymentsManage);
        _canReturn = currentUser.HasPermission(Permissions.PurchasesManage);
        using (var scope = scopeFactory.CreateScope())
        {
            _storeOffset = (scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
        }

        Dock = DockStyle.Fill;

        // ---- KPI row + actions --------------------------------------------------------------------------
        _total = Stat("Tổng nợ phải trả", Glyphs.Money, BadgeKind.Warning);
        _paid = Stat("Đã trả NCC trong kỳ", Glyphs.Accept, BadgeKind.Success);
        _returned = Stat("Trả hàng NCC trong kỳ", Glyphs.Return, BadgeKind.Info);
        _largest = Stat("Nợ lớn nhất", Glyphs.People, BadgeKind.Primary);
        var stats = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        stats.Controls.AddRange([_total, _paid, _returned, _largest]);

        _pay = Action("Thanh toán (F6)", Glyphs.Money, ButtonVariant.Primary, PaySelectedAsync);
        _pay.Visible = _canPay;
        _ledger = Action("Sổ công nợ (F7)", Glyphs.History, ButtonVariant.Secondary, () => { ShowLedgerOfSelected(); return Task.CompletedTask; });
        var returns = Action("Trả hàng NCC (F8)", Glyphs.Return, ButtonVariant.Secondary, NewReturnAsync);
        returns.Visible = _canReturn;
        var refresh = Action("Làm mới", Glyphs.Refresh, ButtonVariant.Ghost, ReloadAsync);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 24, 0, 0) };
        actions.Controls.AddRange([_pay, _ledger, returns, refresh]);

        var header = new Panel { Dock = DockStyle.Top, Height = 140, Padding = new Padding(24, 20, 24, 0) };
        header.Controls.Add(stats);
        header.Controls.Add(actions);

        // ---- Period filter (payments, returns, KPIs) ---------------------------------------------------
        var today = DateTime.Today;
        _from = new ModernDatePicker { Width = 140, Value = new DateTime(today.Year, today.Month, 1), ReserveMessageSpace = false };
        _to = new ModernDatePicker { Width = 140, Value = today, ReserveMessageSpace = false };
        _from.ValueChanged += async (_, _) => await ReloadDocumentsAsync();
        _to.ValueChanged += async (_, _) => await ReloadDocumentsAsync();
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
        _payablesGrid.RowActivated += async (_, _) =>
        {
            if (_canPay)
            {
                await PaySelectedAsync();
            }
            else
            {
                ShowLedgerOfSelected();
            }
        };
        _payablesGrid.SelectionChanged += (_, _) => UpdateButtons();
        _payablesPage = new ModernTabPage("Công nợ phải trả") { IconGlyph = Glyphs.Money, BadgeKind = BadgeKind.Warning, Padding = new Padding(0, 8, 0, 0) };
        _payablesPage.Controls.Add(_payablesGrid);

        // ---- Payments tab -------------------------------------------------------------------------------
        _paymentsGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có phiếu chi trả nợ NCC trong kỳ.", SearchPlaceholder = "Tìm số phiếu, NCC, chứng từ…" };
        _paymentsGrid.AddTextColumn(nameof(PaymentRow.Code), "Số phiếu", width: 140);
        _paymentsGrid.AddDateColumn(nameof(PaymentRow.PaidAt), "Ngày chi", width: 140, withTime: true);
        _paymentsGrid.AddTextColumn(nameof(PaymentRow.SupplierName), "Nhà cung cấp");
        _paymentsGrid.AddTextColumn(nameof(PaymentRow.MethodName), "Hình thức", width: 120);
        _paymentsGrid.AddMoneyColumn(nameof(PaymentRow.Amount), "Số tiền", width: 130);
        _paymentsGrid.AddMoneyColumn(nameof(PaymentRow.DebtAfter), "Nợ sau chi", width: 130);
        _paymentsGrid.AddTextColumn(nameof(PaymentRow.Reference), "Chứng từ", width: 130);
        _paymentsGrid.AddTextColumn(nameof(PaymentRow.StatusName), "Trạng thái", width: 170);
        _paymentsGrid.SelectionChanged += (_, _) => UpdateButtons();
        _paymentsGrid.RowActivated += (_, _) => ShowLedgerOfSelected();
        _paymentsGrid.InnerGrid.CellFormatting += OnPaymentCellFormatting;
        _cancelPayment = new ModernButton { Text = "Hủy phiếu chi", Variant = ButtonVariant.Danger, IconGlyph = Glyphs.Cancel, Enabled = false, Visible = _canPay, Margin = new Padding(8, 0, 0, 0) };
        _cancelPayment.Click += async (_, _) => await CancelPaymentAsync();
        _paymentsGrid.ToolbarItems.Controls.Add(_cancelPayment);
        _paymentsPage = new ModernTabPage("Phiếu chi") { IconGlyph = Glyphs.Document, Padding = new Padding(0, 8, 0, 0) };
        _paymentsPage.Controls.Add(_paymentsGrid);

        _tabs = new ModernTabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(_payablesPage);
        _tabs.TabPages.Add(_paymentsPage);

        // ---- Returns tab (purchases.manage) ------------------------------------------------------------
        if (_canReturn)
        {
            _returnsGrid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có phiếu trả hàng NCC trong kỳ.", SearchPlaceholder = "Tìm số phiếu, NCC, phiếu nhập…" };
            _returnsGrid.AddTextColumn(nameof(ReturnRow.Code), "Số phiếu", width: 140);
            _returnsGrid.AddDateColumn(nameof(ReturnRow.ReturnDate), "Ngày trả", width: 140, withTime: true);
            _returnsGrid.AddTextColumn(nameof(ReturnRow.SupplierName), "Nhà cung cấp");
            _returnsGrid.AddTextColumn(nameof(ReturnRow.GoodsReceiptCode), "Theo phiếu nhập", width: 140);
            _returnsGrid.AddNumberColumn(nameof(ReturnRow.LineCount), "Số SP", width: 70);
            _returnsGrid.AddMoneyColumn(nameof(ReturnRow.Total), "Giá trị", width: 130);
            _returnsGrid.AddMoneyColumn(nameof(ReturnRow.RefundAmount), "NCC hoàn tiền", width: 130);
            _returnsGrid.AddMoneyColumn(nameof(ReturnRow.DebtOffset), "Cấn trừ nợ", width: 130);
            _returnsGrid.SelectionChanged += (_, _) => UpdateButtons();
            _returnsGrid.RowActivated += async (_, e) => await ShowReturnAsync((ReturnRow)e.Item);
            _returnsPage = new ModernTabPage("Trả hàng NCC") { IconGlyph = Glyphs.Return, Padding = new Padding(0, 8, 0, 0) };
            _returnsPage.Controls.Add(_returnsGrid);
            _tabs.TabPages.Add(_returnsPage);
        }

        _tabs.SelectedIndexChanged += (_, _) => UpdateButtons();

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 24) };
        body.Controls.Add(_tabs);

        Controls.Add(body);
        Controls.Add(filter);
        Controls.Add(header);

        Load += async (_, _) => await ReloadAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F6 when _canPay:
                _ = PaySelectedAsync();
                return true;
            case Keys.F7:
                ShowLedgerOfSelected();
                return true;
            case Keys.F8 when _canReturn:
                _ = NewReturnAsync();
                return true;
            case Keys.F5:
                _ = ReloadAsync();
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

    private DateTime StoreTime(DateTimeOffset value) => value.ToOffset(_storeOffset).DateTime;

    /// <summary>Supplier of the selected row on the current tab.</summary>
    private int? SelectedSupplierId =>
        _tabs.SelectedTab == _payablesPage ? _payablesGrid.GetSelected<SupplierPayableDto>()?.SupplierId
        : _tabs.SelectedTab == _paymentsPage ? _paymentsGrid.GetSelected<PaymentRow>()?.SupplierId
        : _returnsGrid?.GetSelected<ReturnRow>()?.SupplierId;

    private void UpdateButtons()
    {
        var supplierId = SelectedSupplierId;
        _ledger.Enabled = supplierId is not null;
        _pay.Enabled = supplierId is { } id && _payables.Any(p => p.SupplierId == id);
        _cancelPayment.Enabled = _paymentsGrid.GetSelected<PaymentRow>() is { IsCancelled: false };
    }

    private async Task ReloadAsync()
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().ListPayablesAsync();
            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                MessageBox.Show(this, result.Error.Message, "Công nợ NCC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _payables = result.Value;
        }

        _payablesGrid.SetItems(_payables);
        _payablesPage.BadgeCount = _payables.Count;
        _total.Value = Vnd.Format(_payables.Sum(p => p.DebtBalance));
        _total.Note = _payables.Count == 0 ? "Không còn nợ NCC" : $"{_payables.Count:#,0} nhà cung cấp";
        if (_payables.FirstOrDefault() is { } top)
        {
            _largest.Value = Vnd.Format(top.DebtBalance);
            _largest.Note = top.Name;
        }
        else
        {
            _largest.Value = "–";
            _largest.Note = null;
        }

        await ReloadDocumentsAsync();
    }

    private async Task ReloadDocumentsAsync()
    {
        var from = _from.Value?.Date ?? DateTime.Today.AddDays(-30);
        var to = (_to.Value?.Date ?? DateTime.Today).AddDays(1);
        if (from >= to)
        {
            _from.SetError("Từ ngày phải trước đến ngày.");
            return;
        }

        _from.ClearValidation();
        var filter = new SupplierDocumentFilter(new DateTimeOffset(from, _storeOffset), new DateTimeOffset(to, _storeOffset));

        using var scope = _scopeFactory.CreateScope();
        var payments = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().SearchAsync(filter);
        var returns = _canReturn ? await scope.ServiceProvider.GetRequiredService<ISupplierReturnService>().SearchAsync(filter) : null;
        if (IsDisposed)
        {
            return;
        }

        if (payments.IsFailure)
        {
            MessageBox.Show(this, payments.Error.Message, "Công nợ NCC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var paymentRows = payments.Value.Select(p => new PaymentRow(
            p.Id,
            p.Code,
            StoreTime(p.PaidAt),
            p.SupplierId,
            p.SupplierName,
            p.MethodName,
            p.Amount,
            p.DebtAfter,
            p.Reference,
            p.IsCancelled,
            p.IsCancelled ? $"Đã hủy{(p.CancelReason is { } reason ? $": {reason}" : string.Empty)}" : "Đã chi")).ToList();
        _paymentsGrid.SetItems(paymentRows);
        var valid = paymentRows.Where(p => !p.IsCancelled).ToList();
        _paid.Value = Vnd.Format(valid.Sum(p => p.Amount));
        _paid.Note = $"{valid.Count:#,0} phiếu chi" + (paymentRows.Count > valid.Count ? $" · {paymentRows.Count - valid.Count} phiếu đã hủy" : string.Empty);

        if (returns is { IsSuccess: true } && _returnsGrid is not null)
        {
            var returnRows = returns.Value.Select(r => new ReturnRow(
                r.Id, r.Code, StoreTime(r.ReturnDate), r.SupplierId, r.SupplierName, r.GoodsReceiptCode ?? "–", r.LineCount, r.Total, r.RefundAmount, r.DebtOffset)).ToList();
            _returnsGrid.SetItems(returnRows);
            _returned.Value = Vnd.Format(returnRows.Sum(r => r.Total));
            _returned.Note = $"{returnRows.Count:#,0} phiếu · cấn trừ nợ {Vnd.Format(returnRows.Sum(r => r.DebtOffset))}";
        }
        else
        {
            _returned.Value = "–";
            _returned.Note = _canReturn ? null : "Cần quyền nhập hàng";
        }

        UpdateButtons();
    }

    private void OnPaymentCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _paymentsGrid.InnerGrid.Rows[e.RowIndex].Tag is not PaymentRow { IsCancelled: true } || e.CellStyle is not { } style)
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        style.ForeColor = _paymentsGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(PaymentRow.StatusName) ? colors.Danger : colors.TextMuted;
    }

    private async Task PaySelectedAsync()
    {
        if (!_canPay || SelectedSupplierId is not { } id || _payables.FirstOrDefault(p => p.SupplierId == id) is not { } payable)
        {
            return;
        }

        using var dialog = new SupplierPaymentDialog(_scopeFactory, payable);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Payment is { } payment)
        {
            MessageBox.Show(
                this,
                $"Đã lập phiếu chi {payment.Code}: {Vnd.Format(payment.Amount)} ({payment.MethodName}).\nCông nợ còn lại với {payment.SupplierName}: {Vnd.Format(payment.DebtAfter)}.",
                "Trả nợ nhà cung cấp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await ReloadAsync();
        }
    }

    private void ShowLedgerOfSelected()
    {
        if (SelectedSupplierId is not { } id)
        {
            return;
        }

        using var form = new SupplierDebtLedgerForm(_scopeFactory, id);
        form.ShowDialog(this);
    }

    private async Task CancelPaymentAsync()
    {
        if (!_canPay || _paymentsGrid.GetSelected<PaymentRow>() is not { IsCancelled: false } row)
        {
            return;
        }

        using var prompt = new ReasonPromptDialog(
            "Hủy phiếu chi",
            $"Hủy phiếu chi {row.Code} ({row.SupplierName}, {Vnd.Format(row.Amount)})? Công nợ sẽ tăng lại {Vnd.Format(row.Amount)}.",
            ["Lập nhầm phiếu", "Chuyển khoản bị hoàn", "Sai số tiền"],
            required: true);
        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().CancelAsync(row.Id, prompt.Reason);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Hủy phiếu chi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadAsync();
    }

    private async Task NewReturnAsync()
    {
        if (!_canReturn)
        {
            return;
        }

        IReadOnlyList<StockItem> catalog;
        IReadOnlyList<SupplierDto> suppliers;
        using (var scope = _scopeFactory.CreateScope())
        {
            var stock = await scope.ServiceProvider.GetRequiredService<IInventoryService>().GetStockListAsync();
            var list = await scope.ServiceProvider.GetRequiredService<ISupplierService>().ListAsync();
            if (IsDisposed)
            {
                return;
            }

            if (stock.IsFailure || list.IsFailure)
            {
                MessageBox.Show(this, (stock.IsFailure ? stock.Error : list.Error).Message, "Trả hàng NCC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            catalog = stock.Value;
            suppliers = list.Value;
        }

        var supplierId = _tabs.SelectedTab == _payablesPage ? _payablesGrid.GetSelected<SupplierPayableDto>()?.SupplierId : null;
        using var editor = new SupplierReturnEditorForm(_scopeFactory, catalog, suppliers, supplierId: supplierId);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync();
            if (_returnsPage is not null)
            {
                _tabs.SelectedTab = _returnsPage;
            }
        }
    }

    private async Task ShowReturnAsync(ReturnRow row)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierReturnService>().GetAsync(row.Id);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Trả hàng NCC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new SupplierReturnDetailForm(result.Value, _storeOffset);
        form.ShowDialog(this);
    }

    private sealed record PaymentRow(
        int Id,
        string Code,
        DateTime PaidAt,
        int SupplierId,
        string SupplierName,
        string MethodName,
        decimal Amount,
        decimal DebtAfter,
        string? Reference,
        bool IsCancelled,
        string StatusName);

    private sealed record ReturnRow(
        int Id,
        string Code,
        DateTime ReturnDate,
        int SupplierId,
        string SupplierName,
        string GoodsReceiptCode,
        int LineCount,
        decimal Total,
        decimal RefundAmount,
        decimal DebtOffset);
}
