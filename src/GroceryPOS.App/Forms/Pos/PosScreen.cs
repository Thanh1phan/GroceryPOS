using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.App.Forms.Shifts;
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
/// Counter screen (Bán hàng). Left: scan box + cart. Right: member, totals, tender and pay.
/// Passive MVP view: the logic lives in <see cref="PosPresenter"/>, the cart in the UI-free <see cref="PosCart"/>,
/// and everything beside the cart comes from the tested <see cref="PosBoard"/>. Keys:
/// F2 quét mã · F3 tìm sản phẩm · F4 số lượng · F5 khách hàng · F6 giảm giá dòng · F7 giảm giá hóa đơn ·
/// F8 khách đưa · F9 thanh toán · F10 lưu tạm đơn · F11 đơn đang chờ · F12 kiểm tra giá · Ctrl+P in lại hóa đơn ·
/// +/− số lượng · Delete xóa dòng · Esc hủy đơn. "Màn hình khách" opens the customer-facing display (second monitor).
/// When <see cref="SalesOptions.RequireOpenShift"/> is on, the cashier is asked to open a shift on entry and cannot pay without one.
/// </summary>
internal sealed class PosScreen : BaseUserControl, IPosView
{
    private const int SidePanelWidth = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosPresenter _presenter;
    private readonly ToneLabel _shiftInfo;
    private readonly ModernButton _openShift;

    private readonly ModernTextBox _scan;
    private readonly ToneLabel _status;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _quantity;
    private readonly ModernButton _lineDiscount;
    private readonly ModernButton _removeLine;
    private readonly ModernButton _park;
    private readonly ModernButton _parked;
    private readonly ModernButton _customerDisplay;
    private readonly TimeProvider _clock;
    private readonly bool _canDiscount;

    private readonly ModernTextBox _phone;
    private readonly ToneLabel _customerInfo;
    private readonly ModernNumericBox _points;
    private readonly ModernButton _maxPoints;
    private readonly ModernButton _detachCustomer;

    private readonly ToneLabel _gross;
    private readonly ToneLabel _lineDiscounts;
    private readonly ToneLabel _invoiceDiscount;
    private readonly ToneLabel _pointsDiscount;
    private readonly ToneLabel _total;
    private readonly ToneLabel _earn;
    private readonly ModernButton _invoiceDiscountButton;

    private readonly RadioButton _cash;
    private readonly RadioButton _transfer;
    private readonly RadioButton _card;
    private readonly ModernCurrencyBox _tendered;
    private readonly FlowLayoutPanel _quickCash;
    private readonly ModernTextBox _reference;
    private readonly ToneLabel _change;
    private readonly ToneLabel _blocker;
    private readonly ModernButton _pay;

    private bool _rendering;
    private string _quickCashKey = string.Empty;
    private CustomerDisplayForm? _display;

    public PosScreen(IServiceProvider services)
    {
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _presenter = services.GetRequiredService<PosPresenter>();
        _clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        _canDiscount = _presenter.CanDiscount;
        RefreshOnF5 = false; // F5 = khách hàng on this screen

        // ---- Left: scan box, status, cart ---------------------------------------------------------
        _scan = new ModernTextBox
        {
            Dock = DockStyle.Top,
            IconGlyph = Glyphs.Barcode,
            TrailingGlyph = Glyphs.Search,
            PlaceholderText = "Quét mã vạch / nhập mã hoặc tên sản phẩm (F2) – 3*mã để bán 3 cái",
            ReserveMessageSpace = false,
        };
        _scan.EnterPressed += async (_, _) =>
        {
            // Empty the box at once so a fast scanner can start the next code while this one is looked up.
            var text = _scan.Text;
            _scan.Clear();
            await _presenter.ScanAsync(text);
        };
        _scan.TrailingIconClick += (_, _) => _presenter.OpenSearch(_scan.Text);

        _status = new ToneLabel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(0, 4, 0, 4),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = ThemeFonts.Get(FontRole.Subtitle),
            Tone = LabelTone.Muted,
        };

        _grid = new ModernDataGrid
        {
            Dock = DockStyle.Fill,
            ShowSearch = false,
            ShowPager = false,
            PageSize = 1000,
            EmptyText = "Chưa có sản phẩm. Quét mã vạch hoặc nhấn F3 để tìm.",
        };
        _grid.AddTextColumn(nameof(PosCartLine.Code), "Mã", width: 110);
        _grid.AddTextColumn(nameof(PosCartLine.Name), "Tên sản phẩm");
        _grid.AddTextColumn(nameof(PosCartLine.Unit), "ĐVT", width: 70);
        _grid.AddNumberColumn(nameof(PosCartLine.Quantity), "SL", width: 80, decimals: 3);
        _grid.AddMoneyColumn(nameof(PosCartLine.UnitPrice), "Đơn giá", width: 110);
        _grid.AddTextColumn(nameof(PosCartLine.DiscountText), "Giảm", width: 90);
        _grid.AddMoneyColumn(nameof(PosCartLine.NetAmount), "Thành tiền", width: 130);
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        _grid.InnerGrid.CellFormatting += OnCellFormatting;
        _grid.RowActivated += (_, _) => _presenter.EditQuantity(SelectedLine);

        var plus = ToolbarButton("+1", null, (_, _) => _presenter.Step(SelectedLine, 1));
        var minus = ToolbarButton("−1", null, (_, _) => _presenter.Step(SelectedLine, -1));
        _quantity = ToolbarButton("Số lượng (F4)", Glyphs.Edit, (_, _) => _presenter.EditQuantity(SelectedLine));
        _lineDiscount = ToolbarButton("Giảm giá dòng (F6)", Glyphs.Money, (_, _) => _presenter.EditLineDiscount(SelectedLine));
        _removeLine = ToolbarButton("Xóa dòng (Del)", Glyphs.Delete, (_, _) => _presenter.RemoveLine(SelectedLine));
        _removeLine.Variant = ButtonVariant.Danger;
        var reprint = ToolbarButton("In lại HĐ (Ctrl+P)", Glyphs.Print, async (_, _) => await _presenter.ReprintAsync());
        _park = ToolbarButton("Lưu tạm (F10)", Glyphs.Save, async (_, _) => await _presenter.ParkAsync());
        _parked = ToolbarButton("Đơn chờ (F11) · 00", Glyphs.History, async (_, _) => await _presenter.ShowParkedAsync());
        _parked.Text = PosTexts.ParkedCaption(0);
        var priceCheck = ToolbarButton("Kiểm giá (F12)", Glyphs.Search, (_, _) => _presenter.PriceCheck());
        _customerDisplay = ToolbarButton("Màn hình khách: tắt", Glyphs.Shop, (_, _) => ToggleCustomerDisplay());
        _customerDisplay.Text = "Màn hình khách";
        _grid.ToolbarItems.Controls.AddRange([plus, minus, _quantity, _lineDiscount, _removeLine, reprint, _park, _parked, priceCheck, _customerDisplay]);

        // ---- Shift bar: current shift, or "Mở ca" when none is open ------------------------------
        _shiftInfo = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(0, 10, 12, 0) };
        _openShift = new ModernButton { Text = "Mở ca", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Add, Size = new Size(110, 34), Visible = false };
        _openShift.Click += async (_, _) => await _presenter.OpenShiftAsync();
        var shiftBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Padding = new Padding(0, 0, 0, 6) };
        shiftBar.Controls.AddRange([_shiftInfo, _openShift]);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 8, 16) };
        left.Controls.Add(_grid);
        left.Controls.Add(_status);
        left.Controls.Add(_scan);
        left.Controls.Add(shiftBar);

        // ---- Right: customer, totals, payment ----------------------------------------------------
        var side = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(0),
        };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _phone = new ModernTextBox
        {
            Dock = DockStyle.Fill,
            IconGlyph = Glyphs.Phone,
            TrailingGlyph = Glyphs.Search,
            PlaceholderText = "SĐT khách hàng (F5)",
            ReserveMessageSpace = false,
        };
        _phone.EnterPressed += async (_, _) => await _presenter.FindCustomerAsync(_phone.Text);
        _phone.TrailingIconClick += async (_, _) => await _presenter.FindCustomerAsync(_phone.Text);

        _customerInfo = Value(LabelTone.Muted, "Khách lẻ");
        _customerInfo.TextAlign = ContentAlignment.MiddleLeft;

        _points = new ModernNumericBox { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1_000_000, SuffixText = "điểm", Enabled = false, ReserveMessageSpace = false };
        _points.ValueChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _presenter.RedeemPoints((int)_points.ValueOrZero);
            }
        };
        _maxPoints = new ModernButton { Text = "Dùng tối đa", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill, Enabled = false };
        _maxPoints.Click += (_, _) => _presenter.UseMaxPoints();
        _detachCustomer = new ModernButton { Text = "Bỏ chọn khách", Variant = ButtonVariant.Ghost, Dock = DockStyle.Fill, Enabled = false };
        _detachCustomer.Click += (_, _) => _presenter.DetachCustomer();

        _gross = Value();
        _lineDiscounts = Value();
        _invoiceDiscount = Value();
        _pointsDiscount = Value();
        _earn = Value(LabelTone.Muted);
        _total = Value(LabelTone.Primary);
        _total.Font = ThemeFonts.Get(FontRole.Display, FontStyle.Bold);
        _total.Height = 52;

        _invoiceDiscountButton = new ModernButton
        {
            Text = "Giảm giá hóa đơn (F7)",
            Variant = ButtonVariant.Secondary,
            IconGlyph = Glyphs.Money,
            Dock = DockStyle.Fill,
            Enabled = _canDiscount,
        };
        _invoiceDiscountButton.Click += (_, _) => _presenter.EditInvoiceDiscount();

        _cash = new RadioButton { Text = "Tiền mặt", AutoSize = true, Checked = true };
        _transfer = new RadioButton { Text = "Chuyển khoản", AutoSize = true };
        _card = new RadioButton { Text = "Thẻ", AutoSize = true };
        foreach (var radio in new[] { _cash, _transfer, _card })
        {
            radio.CheckedChanged += (_, _) =>
            {
                if (!_rendering && radio.Checked)
                {
                    _presenter.SetPaymentMethod(_transfer.Checked ? PaymentMethod.BankTransfer : _card.Checked ? PaymentMethod.Card : PaymentMethod.Cash);
                }
            };
        }

        var methods = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        methods.Controls.AddRange([_cash, _transfer, _card]);

        _tendered = new ModernCurrencyBox { Dock = DockStyle.Fill, ReserveMessageSpace = false, PlaceholderText = "Khách đưa (F8)" };
        _tendered.ValueChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _presenter.SetCashTendered(_tendered.ValueOrZero);
            }
        };

        _quickCash = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };

        _reference = new ModernTextBox { Dock = DockStyle.Fill, PlaceholderText = "Mã giao dịch (không bắt buộc)", Visible = false, ReserveMessageSpace = false };
        _reference.InnerTextBox.TextChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _presenter.SetPaymentReference(_reference.Text);
            }
        };

        _change = Value(LabelTone.Success);
        _change.Font = ThemeFonts.Get(FontRole.Headline, FontStyle.Bold);
        _change.Height = 40;

        _blocker = new ToneLabel { Dock = DockStyle.Fill, Height = 24, Tone = LabelTone.Warning, TextAlign = ContentAlignment.MiddleLeft };

        _pay = new ModernButton
        {
            Text = "Thanh toán (F9)",
            Variant = ButtonVariant.Primary,
            IconGlyph = Glyphs.Accept,
            Dock = DockStyle.Fill,
            Height = 56,
            Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold),
        };
        _pay.Click += async (_, _) =>
        {
            _tendered.Commit();
            await _presenter.CheckoutAsync();
        };

        var cancelSale = new ModernButton { Text = "Hủy đơn (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill };
        cancelSale.Click += (_, _) => _presenter.CancelSale();

        AddSpan(side, Section("Khách hàng"));
        AddSpan(side, _phone, 44);
        AddSpan(side, _customerInfo, 40);
        AddPair(side, _points, _maxPoints, 44);
        AddSpan(side, _detachCustomer, 36);

        AddSpan(side, Section("Hóa đơn"));
        AddPair(side, Caption("Tổng tiền hàng"), _gross);
        AddPair(side, Caption("Giảm giá sản phẩm"), _lineDiscounts);
        AddPair(side, Caption("Giảm giá hóa đơn"), _invoiceDiscount);
        AddPair(side, Caption("Đổi điểm"), _pointsDiscount);
        AddSpan(side, _invoiceDiscountButton, 40);
        AddSpan(side, Caption("KHÁCH CẦN TRẢ"), 24);
        AddSpan(side, _total, 56);
        AddSpan(side, _earn, 24);

        AddSpan(side, Section("Thanh toán"));
        AddSpan(side, methods);
        AddSpan(side, Caption("Khách đưa"), 24);
        AddSpan(side, _tendered, 44);
        AddSpan(side, _quickCash);
        AddSpan(side, _reference, 44);
        AddSpan(side, Caption("Tiền thừa trả khách"), 24);
        AddSpan(side, _change, 44);
        AddSpan(side, _blocker, 28);
        AddSpan(side, _pay, 60);
        AddSpan(side, cancelSale, 40);

        var sideCard = new ModernCard
        {
            Dock = DockStyle.Right,
            Width = SidePanelWidth,
            AutoScroll = true,
            Margin = new Padding(0),
        };
        sideCard.Controls.Add(side);

        var sideHost = new Panel { Dock = DockStyle.Right, Width = SidePanelWidth + 24, Padding = new Padding(8, 16, 16, 16) };
        sideHost.Controls.Add(sideCard);

        Controls.Add(left);
        Controls.Add(sideHost);

        Disposed += (_, _) => _display?.Close();
        _presenter.Attach(this);
    }

    bool IView.IsAlive => IsAlive;

    private PosCartLine? SelectedLine => _grid.GetSelected<PosCartLine>();

    public override bool CanLeave() => _presenter.CanLeave();

    protected override async Task OnFirstLoadAsync()
    {
        ((IPosView)this).FocusScan();
        await _presenter.StartAsync();
    }

    // ---- Keyboard ----------------------------------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_presenter.IsBusy)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var gridFocused = _grid.InnerGrid.Focused;
        var scanEmpty = _scan.ContainsFocus && _scan.Text.Length == 0;
        switch (keyData)
        {
            case Keys.F2:
                ((IPosView)this).FocusScan();
                return true;
            case Keys.F3:
                _presenter.OpenSearch(_scan.Text);
                return true;
            case Keys.F4:
                _presenter.EditQuantity(SelectedLine);
                return true;
            case Keys.F5:
                _phone.Focus();
                _phone.SelectAll();
                return true;
            case Keys.F6:
                _presenter.EditLineDiscount(SelectedLine);
                return true;
            case Keys.F7:
                _presenter.EditInvoiceDiscount();
                return true;
            case Keys.F8:
                _presenter.SetPaymentMethod(PaymentMethod.Cash);
                _tendered.Focus();
                _tendered.SelectAll();
                return true;
            case Keys.F9:
                _tendered.Commit();
                _ = _presenter.CheckoutAsync();
                return true;
            case Keys.F10:
                _ = _presenter.ParkAsync();
                return true;
            case Keys.F11:
                _ = _presenter.ShowParkedAsync();
                return true;
            case Keys.F12:
                _presenter.PriceCheck();
                return true;
            case Keys.Control | Keys.P:
                _ = _presenter.ReprintAsync();
                return true;
            case Keys.Escape:
                _presenter.CancelSale();
                return true;
            case Keys.Delete when gridFocused || scanEmpty:
                _presenter.RemoveLine(SelectedLine);
                return true;
            case Keys.Add or Keys.Oemplus when gridFocused || scanEmpty:
                _presenter.Step(SelectedLine, 1);
                return true;
            case Keys.Subtract or Keys.OemMinus when gridFocused || scanEmpty:
                _presenter.Step(SelectedLine, -1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not PosCartLine { ExceedsStock: true })
        {
            return;
        }

        if (_grid.Columns[e.ColumnIndex].DataPropertyName == nameof(PosCartLine.Quantity) && e.CellStyle is { } style)
        {
            style.ForeColor = ThemeManager.Current.Colors.Danger;
            style.Font = ThemeFonts.Get(FontRole.Body, FontStyle.Bold);
        }
    }

    // ---- Customer display (second monitor) ---------------------------------------------------------

    private void ToggleCustomerDisplay()
    {
        if (_display is { IsDisposed: false })
        {
            _display.Close();
            return;
        }

        _display = new CustomerDisplayForm();
        _display.FormClosed += (_, _) =>
        {
            _display = null;
            if (!IsDisposed)
            {
                _customerDisplay.Variant = ButtonVariant.Secondary;
            }
        };
        _customerDisplay.Variant = ButtonVariant.Primary;
        _display.Render(_presenter.DisplayState);
        _display.Show(); // not owned: an owned window would stay on top of the counter
        ((IPosView)this).FocusScan();
    }

    // ---- IPosView ----------------------------------------------------------------------------------

    void IPosView.ShowBoard(PosBoard board)
    {
        _rendering = true;
        try
        {
            var selected = _grid.GetSelected<PosCartLine>();
            _grid.SetItems(board.Lines);
            if (selected is not null && board.Lines.Contains(selected))
            {
                _grid.SelectItem(selected);
            }
            else if (board.Lines.Count > 0)
            {
                _grid.SelectItem(board.Lines[^1]);
            }

            _quantity.Enabled = board.CanEditLines;
            _removeLine.Enabled = board.CanEditLines;
            _lineDiscount.Enabled = board.CanDiscountLines;

            _customerInfo.Text = board.CustomerInfo;
            _customerInfo.Tone = board.CustomerTone.ToLabelTone();
            _points.Enabled = board.HasMember;
            _maxPoints.Enabled = board.CanUseMaxPoints;
            _detachCustomer.Enabled = board.HasMember;
            if ((int)_points.ValueOrZero != board.PointsToRedeem)
            {
                _points.Value = board.PointsToRedeem;
            }

            _gross.Text = board.Gross;
            _lineDiscounts.Text = board.LineDiscounts;
            _invoiceDiscount.Text = board.InvoiceDiscount;
            _pointsDiscount.Text = board.PointsDiscount;
            _total.Text = board.Total;
            _earn.Text = board.Earn;
            _invoiceDiscountButton.Enabled = board.CanDiscountInvoice;

            _cash.Checked = board.Method == PaymentMethod.Cash;
            _transfer.Checked = board.Method == PaymentMethod.BankTransfer;
            _card.Checked = board.Method == PaymentMethod.Card;

            _tendered.Visible = board.IsCash;
            _quickCash.Visible = board.IsCash;
            _reference.Visible = !board.IsCash;
            if (!_tendered.ContainsFocus && _tendered.ValueOrZero != (board.CashTendered ?? 0))
            {
                _tendered.Value = board.CashTendered ?? 0;
            }

            if (board.PaymentReference is null && _reference.Text.Length > 0)
            {
                _reference.Clear();
            }

            RenderQuickCash(board.QuickCash);

            _change.Text = board.Change;
            _blocker.Text = board.Blocker;
            _pay.Enabled = board.CanPay;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderQuickCash(IReadOnlyList<decimal> amounts)
    {
        var key = string.Join("|", amounts);
        if (key == _quickCashKey)
        {
            return;
        }

        _quickCashKey = key;
        _quickCash.SuspendLayout();
        foreach (Control control in _quickCash.Controls)
        {
            control.Dispose();
        }

        _quickCash.Controls.Clear();
        foreach (var amount in amounts)
        {
            var button = new ModernButton
            {
                Text = Vnd.FormatNumber(amount),
                Variant = ButtonVariant.Secondary,
                Size = new Size(116, 34),
                Margin = new Padding(0, 0, 6, 6),
            };
            button.Click += (_, _) => _presenter.QuickCash(amount);
            _quickCash.Controls.Add(button);
        }

        ThemeManager.Apply(_quickCash);
        _quickCash.ResumeLayout(true);
    }

    void IPosView.SelectLine(PosCartLine line) => _grid.SelectItem(line);

    void IPosView.ShowShift(PosShiftBar bar)
    {
        _shiftInfo.Text = bar.Text;
        _shiftInfo.Tone = bar.Tone.ToLabelTone();
        _openShift.Visible = bar.ShowOpenButton;
    }

    void IPosView.ShowStatus(string text, Tone tone)
    {
        _status.Text = text;
        _status.Tone = tone.ToLabelTone();
    }

    void IPosView.ShowParkedCount(int count)
    {
        _parked.Text = PosTexts.ParkedCaption(count);
        _parked.Variant = count > 0 ? ButtonVariant.Primary : ButtonVariant.Secondary;
    }

    void IPosView.SetPhone(string text)
    {
        if (_phone.Text != text)
        {
            _phone.Text = text;
        }
    }

    void IPosView.ShowPhoneError(string? message)
    {
        if (message is null)
        {
            _phone.ClearValidation();
        }
        else
        {
            _phone.SetError(message);
        }
    }

    void IPosView.RestoreScan(string text)
    {
        if (_scan.Text.Length == 0)
        {
            _scan.Text = text;
        }

        _scan.SelectAll();
    }

    void IPosView.ClearScan() => _scan.Clear();

    void IPosView.FocusScan()
    {
        if (IsAlive)
        {
            _scan.Focus();
            _scan.SelectAll();
        }
    }

    void IPosView.FocusPay() => _pay.Focus();

    void IPosView.ShowCustomerDisplay(CustomerDisplayState state)
    {
        if (_display is { IsDisposed: false } display)
        {
            display.Render(state);
        }
    }

    decimal? IPosView.AskQuantity(string name, string unit, decimal start)
    {
        using var dialog = new PosQuantityDialog(name, unit, start);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Quantity : null;
    }

    (bool Ok, DiscountInput? Discount) IPosView.AskDiscount(string title, decimal baseAmount, DiscountInput? current)
    {
        using var dialog = new PosDiscountDialog(title, baseAmount, current);
        return dialog.ShowDialog(this) == DialogResult.OK ? (true, dialog.Discount) : (false, null);
    }

    PosProductDto? IPosView.SearchProduct(string term)
    {
        using var dialog = new PosProductSearchDialog(_scopeFactory, term);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedProduct : null;
    }

    PosProductDto? IPosView.PriceCheck()
    {
        using var dialog = new PriceCheckDialog(_scopeFactory, canAdd: true);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedProduct : null;
    }

    ShiftSummaryDto? IPosView.OpenShift(decimal suggestedFloat, string? cashierName)
    {
        using var dialog = new OpenShiftDialog(_scopeFactory, suggestedFloat, cashierName);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Opened : null;
    }

    (bool Ok, string? Label) IPosView.AskParkLabel(int lineCount, decimal total, string defaultLabel)
    {
        using var dialog = new ParkOrderDialog(lineCount, total, defaultLabel);
        return dialog.ShowDialog(this) == DialogResult.OK ? (true, dialog.OrderLabel) : (false, null);
    }

    (Guid? Chosen, int Remaining) IPosView.PickParkedOrder(bool cartBusy)
    {
        using var dialog = new ParkedOrdersDialog(_scopeFactory, _clock, _presenter.Options.StoreUtcOffset, cartBusy);
        var answer = dialog.ShowDialog(this);
        return (answer == DialogResult.OK ? dialog.SelectedOrderId : null, dialog.RemainingCount);
    }

    string? IPosView.AskReprintCode(string? lastCode)
    {
        using var dialog = new ReprintDialog(lastCode);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Code : null;
    }

    bool IPosView.ShowReceipt(ReceiptData receipt, int paperWidthMm)
    {
        using var preview = new ReceiptPreviewForm(receipt, paperWidthMm);
        preview.ShowDialog(this);
        return preview.Printed;
    }

    void IPosView.Beep() => System.Media.SystemSounds.Beep.Play();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, cancelText: "Tiếp tục bán", danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);


    // ---- Helpers -----------------------------------------------------------------------------------

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

    private static ToneLabel Value(LabelTone tone = LabelTone.Normal, string text = "") => new()
    {
        Text = text,
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
