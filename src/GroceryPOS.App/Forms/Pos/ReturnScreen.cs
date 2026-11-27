using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Trả hàng: scan or type the invoice number from the customer's receipt, pick what comes back per line
/// (quantity, back on the shelf or written off as damaged), give a reason and refund in cash or by transfer,
/// then print the refund slip (PHIẾU TRẢ HÀNG).
/// Keys: F2 số hóa đơn · F4 số lượng · F5 tải lại · F6 hàng hỏng/nhập kho · F7 trả cả hóa đơn · +/− số lượng ·
/// Space trả hết dòng · F9 hoàn tiền · Esc làm lại.
/// Passive MVP view – the logic lives in <see cref="ReturnPresenter"/>, the sheet in <see cref="ReturnSheet"/>.
/// </summary>
internal sealed class ReturnScreen : BaseUserControl, IReturnView
{
    private const int SidePanelWidth = 380;

    private readonly ReturnPresenter _presenter;

    private readonly ModernTextBox _scan;
    private readonly ModernBadge _windowBadge;
    private readonly ToneLabel _invoiceInfo;
    private readonly ToneLabel _status;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton[] _lineButtons;
    private readonly ModernButton _restock;
    private readonly ModernButton _returnAll;
    private readonly ModernButton _clear;

    private readonly ToneLabel _lineCount;
    private readonly ToneLabel _refund;
    private readonly ToneLabel _writtenOff;
    private readonly ToneLabel _points;
    private readonly RadioButton _cash;
    private readonly RadioButton _transfer;
    private readonly ModernTextBox _reason;
    private readonly ToneLabel _previousReturns;
    private readonly ToneLabel _blocker;
    private readonly ModernButton _submit;

    private bool _rendering;

    public ReturnScreen(IServiceProvider services)
    {
        _presenter = services.GetRequiredService<ReturnPresenter>();

        // ---- Left: invoice scan, header, lines -----------------------------------------------------
        _scan = new ModernTextBox
        {
            Dock = DockStyle.Top,
            IconGlyph = Glyphs.Barcode,
            TrailingGlyph = Glyphs.Search,
            PlaceholderText = "Quét mã vạch hoặc nhập số hóa đơn trên phiếu của khách (F2), VD: HD261027-0001",
            ReserveMessageSpace = false,
        };
        _scan.EnterPressed += async (_, _) => await _presenter.LoadInvoiceAsync(_scan.Text);
        _scan.TrailingIconClick += async (_, _) => await _presenter.LoadInvoiceAsync(_scan.Text);

        _windowBadge = new ModernBadge { Visible = false, Margin = new Padding(0, 8, 8, 0) };
        _invoiceInfo = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(0, 8, 0, 0) };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        header.Controls.AddRange([_windowBadge, _invoiceInfo]);

        _status = new ToneLabel
        {
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ThemeFonts.Get(FontRole.Subtitle),
            Tone = LabelTone.Muted,
        };

        _grid = new ModernDataGrid
        {
            Dock = DockStyle.Fill,
            ShowSearch = false,
            ShowPager = false,
            PageSize = 1000,
            EmptyText = "Quét số hóa đơn để xem các sản phẩm khách đã mua.",
        };
        _grid.AddTextColumn("Line.ProductCode", "Mã", width: 100);
        _grid.AddTextColumn("Line.ProductName", "Tên sản phẩm");
        _grid.AddTextColumn("Line.Unit", "ĐVT", width: 64);
        _grid.AddNumberColumn("Line.SoldQuantity", "Đã mua", width: 80, decimals: 3);
        _grid.AddNumberColumn("Line.ReturnedQuantity", "Đã trả", width: 80, decimals: 3);
        _grid.AddMoneyColumn("Line.UnitRefund", "Giá trả/ĐV", width: 110);
        _grid.AddNumberColumn(nameof(ReturnSheetLine.Quantity), "SL trả", width: 80, decimals: 3);
        _grid.AddMoneyColumn(nameof(ReturnSheetLine.Refund), "Hoàn tiền", width: 120);
        _grid.AddTextColumn(nameof(ReturnSheetLine.HandlingText), "Xử lý", width: 130);
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        _grid.InnerGrid.CellFormatting += OnCellFormatting;
        _grid.RowActivated += (_, _) => _presenter.EditQuantity(SelectedLine);
        _grid.SelectionChanged += (_, _) => UpdateLineButtons();

        var plus = ToolbarButton("+1", null, (_, _) => _presenter.Step(SelectedLine, 1));
        var minus = ToolbarButton("−1", null, (_, _) => _presenter.Step(SelectedLine, -1));
        var all = ToolbarButton("Trả hết dòng (Space)", Glyphs.Accept, (_, _) => _presenter.ToggleLine(SelectedLine));
        var quantity = ToolbarButton("Số lượng (F4)", Glyphs.Edit, (_, _) => _presenter.EditQuantity(SelectedLine));
        _restock = ToolbarButton("Nhập lại kho (F6)", Glyphs.Warning, (_, _) => _presenter.ToggleRestock(SelectedLine));
        _restock.Text = "Hàng hỏng (F6)"; // sized for the longer caption, which it shows on a damaged line
        _lineButtons = [plus, minus, all, quantity, _restock];
        _returnAll = ToolbarButton("Trả cả hóa đơn (F7)", Glyphs.Return, (_, _) => _presenter.ReturnAll());
        _clear = ToolbarButton("Bỏ chọn", Glyphs.Clear, (_, _) => _presenter.ClearPicks());
        _clear.Variant = ButtonVariant.Ghost;
        _grid.ToolbarItems.Controls.AddRange([.. _lineButtons, _returnAll, _clear]);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 8, 16) };
        left.Controls.Add(_grid);
        left.Controls.Add(_status);
        left.Controls.Add(header);
        left.Controls.Add(_scan);

        // ---- Right: refund summary, method, reason, submit ---------------------------------------
        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
        };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _lineCount = Value();
        _writtenOff = Value();
        _points = Value(LabelTone.Muted);
        _points.TextAlign = ContentAlignment.MiddleLeft;
        _refund = Value(LabelTone.Danger);
        _refund.Font = ThemeFonts.Get(FontRole.Display, FontStyle.Bold);
        _refund.Height = 52;

        _cash = new RadioButton { Text = "Tiền mặt", AutoSize = true, Checked = true };
        _transfer = new RadioButton { Text = "Chuyển khoản", AutoSize = true };
        foreach (var radio in new[] { _cash, _transfer })
        {
            radio.CheckedChanged += (_, _) =>
            {
                if (!_rendering && radio.Checked)
                {
                    _presenter.SetRefundMethod(_cash.Checked ? PaymentMethod.Cash : PaymentMethod.BankTransfer);
                }
            };
        }

        var methods = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        methods.Controls.AddRange([_cash, _transfer]);

        _reason = new ModernTextBox { Dock = DockStyle.Fill, PlaceholderText = "Lý do trả hàng (bắt buộc)", ReserveMessageSpace = false };
        _reason.InnerTextBox.TextChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _presenter.SetReason(_reason.Text);
            }
        };

        var quickReasons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        foreach (var reason in ReturnSheet.QuickReasons)
        {
            var chip = new ModernButton { Text = reason, Variant = ButtonVariant.Ghost, AutoSize = false, Height = 30, Margin = new Padding(0, 0, 6, 6) };
            chip.Width = TextRenderer.MeasureText(reason, chip.Font).Width + 28;
            chip.Click += (_, _) =>
            {
                _reason.Text = reason;
                _reason.Focus();
            };
            quickReasons.Controls.Add(chip);
        }

        _previousReturns = new ToneLabel { Dock = DockStyle.Fill, AutoSize = true, Tone = LabelTone.Muted, MaximumSize = new Size(SidePanelWidth - 40, 0) };
        _blocker = new ToneLabel { Dock = DockStyle.Fill, AutoSize = true, Tone = LabelTone.Warning, MaximumSize = new Size(SidePanelWidth - 40, 0) };

        _submit = new ModernButton
        {
            Text = "Hoàn tiền (F9)",
            Variant = ButtonVariant.Danger,
            IconGlyph = Glyphs.Return,
            Dock = DockStyle.Fill,
            Height = 56,
            Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold),
        };
        _submit.Click += async (_, _) => await _presenter.SubmitAsync();

        var reset = new ModernButton { Text = "Làm lại (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill };
        reset.Click += (_, _) => _presenter.Reset();

        AddSpan(side, Section("Phiếu trả hàng"));
        AddPair(side, Caption("Sản phẩm trả"), _lineCount);
        AddPair(side, Caption("Trong đó hàng hỏng"), _writtenOff);
        AddSpan(side, Caption("TIỀN HOÀN KHÁCH"), 24);
        AddSpan(side, _refund, 56);
        AddSpan(side, _points);

        AddSpan(side, Section("Hoàn tiền"));
        AddSpan(side, methods);
        AddSpan(side, Caption("Lý do"), 24);
        AddSpan(side, _reason, 44);
        AddSpan(side, quickReasons);
        AddSpan(side, _blocker);
        AddSpan(side, _submit, 60);
        AddSpan(side, reset, 40);
        AddSpan(side, Section("Đã trả trước đó"));
        AddSpan(side, _previousReturns);

        var sideCard = new ModernCard { Dock = DockStyle.Right, Width = SidePanelWidth, AutoScroll = true };
        sideCard.Controls.Add(side);
        var sideHost = new Panel { Dock = DockStyle.Right, Width = SidePanelWidth + 24, Padding = new Padding(8, 16, 16, 16) };
        sideHost.Controls.Add(sideCard);

        Controls.Add(left);
        Controls.Add(sideHost);

        _presenter.Attach(this);
    }

    bool IView.IsAlive => IsAlive;

    private ReturnSheetLine? SelectedLine => _grid.GetSelected<ReturnSheetLine>();

    public override Task RefreshDataAsync() => _presenter.RefreshAsync();

    public override bool CanLeave() => _presenter.CanLeave();

    protected override async Task OnFirstLoadAsync()
    {
        _scan.Focus();
        await _presenter.StartAsync();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Keys that type into a text box (Space, +, −) only act when the focus is on the grid.
        var typing = _scan.ContainsFocus || _reason.ContainsFocus;
        switch (keyData)
        {
            case Keys.F2:
                ((IReturnView)this).FocusScan();
                return true;
            case Keys.F4:
                _presenter.EditQuantity(SelectedLine);
                return true;
            case Keys.F6:
                _presenter.ToggleRestock(SelectedLine);
                return true;
            case Keys.F7:
                _presenter.ReturnAll();
                return true;
            case Keys.F9:
                _ = _presenter.SubmitAsync();
                return true;
            case Keys.Escape:
                _presenter.Reset();
                return true;
            case Keys.Space when !typing:
                _presenter.ToggleLine(SelectedLine);
                return true;
            case Keys.Add or Keys.Oemplus when !typing:
                _presenter.Step(SelectedLine, 1);
                return true;
            case Keys.Subtract or Keys.OemMinus when !typing:
                _presenter.Step(SelectedLine, -1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateLineButtons()
    {
        var actions = _presenter.ActionsFor(SelectedLine);
        foreach (var button in _lineButtons)
        {
            button.Enabled = actions.CanEdit;
        }

        _restock.Text = actions.RestockCaption;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not ReturnSheetLine line || e.CellStyle is not { } style)
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        if (line.IsExhausted)
        {
            style.ForeColor = colors.TextSecondary;
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (line.IsSelected && property is nameof(ReturnSheetLine.Quantity) or nameof(ReturnSheetLine.Refund))
        {
            style.ForeColor = colors.Primary;
            style.Font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        }
        else if (line.IsSelected && !line.Restock && property == nameof(ReturnSheetLine.HandlingText))
        {
            style.ForeColor = colors.Danger;
        }
    }

    // ---- IReturnView -------------------------------------------------------------------------------

    void IReturnView.ShowSheet(ReturnSheet? sheet)
    {
        _rendering = true;
        try
        {
            _reason.Text = sheet?.Reason ?? string.Empty;
            _cash.Checked = sheet is null || sheet.RefundMethod == PaymentMethod.Cash;
            _transfer.Checked = !_cash.Checked;
        }
        finally
        {
            _rendering = false;
        }

        _grid.SetItems(sheet?.Lines ?? (IReadOnlyList<ReturnSheetLine>)[]);
    }

    void IReturnView.ShowState(ReturnScreenState state)
    {
        _rendering = true;
        try
        {
            _windowBadge.Visible = state.BadgeText is not null;
            if (state.BadgeText is not null)
            {
                _windowBadge.Set(state.BadgeText, state.BadgeTone.ToBadgeKind());
            }

            _invoiceInfo.Text = state.InvoiceInfo;
            _lineCount.Text = state.LineCount;
            _writtenOff.Text = state.WrittenOff;
            _refund.Text = state.Refund;
            _points.Text = state.Points;
            _blocker.Text = state.Message;
            _blocker.Tone = state.MessageTone.ToLabelTone();
            _submit.Enabled = state.CanSubmit;
            _returnAll.Enabled = state.CanReturnAll;
            _clear.Enabled = state.HasInvoice;
            if (state.HasInvoice)
            {
                _cash.Checked = state.RefundMethod == PaymentMethod.Cash;
                _transfer.Checked = state.RefundMethod == PaymentMethod.BankTransfer;
            }

            _grid.Reload();
            UpdateLineButtons();
        }
        finally
        {
            _rendering = false;
        }
    }

    void IReturnView.ShowStatus(string text, Tone tone)
    {
        _status.Text = text;
        _status.Tone = tone.ToLabelTone();
    }

    void IReturnView.ShowHistory(string text, Tone tone)
    {
        _previousReturns.Text = text;
        _previousReturns.Tone = tone.ToLabelTone();
    }

    void IReturnView.SetScanText(string text) => _scan.Text = text;

    void IReturnView.FocusScan()
    {
        _scan.Focus();
        _scan.SelectAll();
    }

    void IReturnView.FocusLines() => _grid.InnerGrid.Focus();

    decimal? IReturnView.AskQuantity(string prompt, string unit, decimal start)
    {
        using var dialog = new PosQuantityDialog(prompt, unit, start);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Quantity : null;
    }

    void IReturnView.ShowSlip(string title, Func<int, IReadOnlyList<ReceiptLine>> layout, int paperWidthMm)
    {
        using var preview = new ReceiptPreviewForm(layout, title, title, paperWidthMm);
        preview.ShowDialog(this);
    }

    void IReturnView.Beep() => System.Media.SystemSounds.Beep.Play();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, cancelText: "Không", danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    // ---- Layout helpers ----------------------------------------------------------------------------

    private static ModernButton ToolbarButton(string text, string? glyph, EventHandler onClick)
    {
        var button = new ModernButton { Text = text, Variant = ButtonVariant.Secondary, IconGlyph = glyph, AutoSize = false, Height = 36 };
        button.Width = TextRenderer.MeasureText(text, button.Font).Width + (glyph is null ? 32 : 56);
        button.Click += onClick;
        return button;
    }

    private static ToneLabel Section(string text) => new()
    {
        Text = text,
        Font = ThemeFonts.Get(FontRole.Subtitle, FontStyle.Bold),
        Tone = LabelTone.Muted,
        Dock = DockStyle.Fill,
        Height = 32,
        TextAlign = ContentAlignment.BottomLeft,
    };

    private static ToneLabel Caption(string text) => new()
    {
        Text = text,
        Tone = LabelTone.Muted,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static ToneLabel Value(LabelTone tone = LabelTone.Normal) => new()
    {
        Tone = tone,
        Dock = DockStyle.Fill,
        Height = 28,
        TextAlign = ContentAlignment.MiddleRight,
        AutoEllipsis = true,
    };

    private static void AddSpan(TableLayoutPanel table, Control control, int height = 0)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, height) : new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 2, 0, 2);
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static void AddPair(TableLayoutPanel table, Control left, Control right, int height = 30)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        left.Margin = new Padding(0, 2, 4, 2);
        right.Margin = new Padding(4, 2, 0, 2);
        table.Controls.Add(left, 0, row);
        table.Controls.Add(right, 1, row);
    }
}
