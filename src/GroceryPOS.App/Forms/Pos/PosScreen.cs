using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Counter screen (Bán hàng). Left: scan box + cart. Right: member, totals, tender and pay.
/// All state lives in the UI-free <see cref="PosCart"/>; this control renders it and maps keys:
/// F2 quét mã · F3 tìm sản phẩm · F4 số lượng · F5 khách hàng · F6 giảm giá dòng · F7 giảm giá hóa đơn ·
/// F8 khách đưa · F9 thanh toán · +/− số lượng · Delete xóa dòng · Esc hủy đơn.
/// </summary>
internal sealed class PosScreen : UserControl
{
    private const int SidePanelWidth = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canDiscount;
    private readonly PosCart _cart;

    private readonly ModernTextBox _scan;
    private readonly ToneLabel _status;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _quantity;
    private readonly ModernButton _lineDiscount;
    private readonly ModernButton _removeLine;

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
    private bool _busy;
    private decimal _quickCashTotal = -1;

    public PosScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canDiscount = currentUser.HasPermission(Permissions.SalesDiscount);
        Dock = DockStyle.Fill;

        using (var scope = scopeFactory.CreateScope())
        {
            var options = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
            _cart = new PosCart(options.Loyalty);
        }

        // ---- Left: scan box, status, cart ---------------------------------------------------------
        _scan = new ModernTextBox
        {
            Dock = DockStyle.Top,
            IconGlyph = Glyphs.Barcode,
            TrailingGlyph = Glyphs.Search,
            PlaceholderText = "Quét mã vạch / nhập mã hoặc tên sản phẩm (F2) – 3*mã để bán 3 cái",
            ReserveMessageSpace = false,
        };
        _scan.EnterPressed += async (_, _) => await ScanAsync();
        _scan.TrailingIconClick += (_, _) => OpenSearch(_scan.Text);

        _status = new ToneLabel
        {
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = ThemeFonts.Get(FontRole.Subtitle),
            Tone = LabelTone.Muted,
            Text = "Sẵn sàng bán hàng.",
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
        _grid.RowActivated += (_, _) => EditQuantity();
        _grid.SelectionChanged += (_, _) => UpdateLineButtons();

        var plus = ToolbarButton("+1", null, (_, _) => Step(1));
        var minus = ToolbarButton("−1", null, (_, _) => Step(-1));
        _quantity = ToolbarButton("Số lượng (F4)", Glyphs.Edit, (_, _) => EditQuantity());
        _lineDiscount = ToolbarButton("Giảm giá dòng (F6)", Glyphs.Money, (_, _) => EditLineDiscount());
        _removeLine = ToolbarButton("Xóa dòng (Del)", Glyphs.Delete, (_, _) => RemoveLine());
        _removeLine.Variant = ButtonVariant.Danger;
        _grid.ToolbarItems.Controls.AddRange([plus, minus, _quantity, _lineDiscount, _removeLine]);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 8, 16) };
        left.Controls.Add(_grid);
        left.Controls.Add(_status);
        left.Controls.Add(_scan);

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
        _phone.EnterPressed += async (_, _) => await FindCustomerAsync();
        _phone.TrailingIconClick += async (_, _) => await FindCustomerAsync();

        _customerInfo = Value(LabelTone.Muted, "Khách lẻ");
        _customerInfo.TextAlign = ContentAlignment.MiddleLeft;

        _points = new ModernNumericBox { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1_000_000, SuffixText = "điểm", Enabled = false, ReserveMessageSpace = false };
        _points.ValueChanged += (_, _) => ApplyPoints();
        _maxPoints = new ModernButton { Text = "Dùng tối đa", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill, Enabled = false };
        _maxPoints.Click += (_, _) => _points.Value = _cart.MaxRedeemablePoints;
        _detachCustomer = new ModernButton { Text = "Bỏ chọn khách", Variant = ButtonVariant.Ghost, Dock = DockStyle.Fill, Enabled = false };
        _detachCustomer.Click += (_, _) =>
        {
            _cart.DetachCustomer();
            _phone.Clear();
        };

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
        _invoiceDiscountButton.Click += (_, _) => EditInvoiceDiscount();

        _cash = new RadioButton { Text = "Tiền mặt", AutoSize = true, Checked = true };
        _transfer = new RadioButton { Text = "Chuyển khoản", AutoSize = true };
        _card = new RadioButton { Text = "Thẻ", AutoSize = true };
        foreach (var radio in new[] { _cash, _transfer, _card })
        {
            radio.CheckedChanged += (_, _) => OnMethodChanged();
        }

        var methods = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        methods.Controls.AddRange([_cash, _transfer, _card]);

        _tendered = new ModernCurrencyBox { Dock = DockStyle.Fill, ReserveMessageSpace = false, PlaceholderText = "Khách đưa (F8)" };
        _tendered.ValueChanged += (_, _) =>
        {
            if (!_rendering)
            {
                TryCart(() => _cart.SetCashTendered(_tendered.ValueOrZero));
            }
        };

        _quickCash = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0) };

        _reference = new ModernTextBox { Dock = DockStyle.Fill, PlaceholderText = "Mã giao dịch (không bắt buộc)", Visible = false, ReserveMessageSpace = false };
        _reference.InnerTextBox.TextChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _cart.SetPaymentReference(_reference.Text);
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
        _pay.Click += async (_, _) => await CheckoutAsync();

        var cancelSale = new ModernButton { Text = "Hủy đơn (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill };
        cancelSale.Click += (_, _) => CancelSale();

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

        _cart.Changed += (_, _) => Render();
        Load += (_, _) =>
        {
            Render();
            FocusScan();
        };
    }

    // ---- Keyboard ----------------------------------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_busy)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var gridFocused = _grid.InnerGrid.Focused;
        var scanEmpty = _scan.ContainsFocus && _scan.Text.Length == 0;
        switch (keyData)
        {
            case Keys.F2:
                FocusScan();
                return true;
            case Keys.F3:
                OpenSearch(_scan.Text);
                return true;
            case Keys.F4:
                EditQuantity();
                return true;
            case Keys.F5:
                _phone.Focus();
                _phone.SelectAll();
                return true;
            case Keys.F6:
                EditLineDiscount();
                return true;
            case Keys.F7:
                EditInvoiceDiscount();
                return true;
            case Keys.F8:
                _cash.Checked = true;
                _tendered.Focus();
                _tendered.SelectAll();
                return true;
            case Keys.F9:
                _ = CheckoutAsync();
                return true;
            case Keys.Escape:
                CancelSale();
                return true;
            case Keys.Delete when gridFocused || scanEmpty:
                RemoveLine();
                return true;
            case Keys.Add or Keys.Oemplus when gridFocused || scanEmpty:
                Step(1);
                return true;
            case Keys.Subtract or Keys.OemMinus when gridFocused || scanEmpty:
                Step(-1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Scan & search -----------------------------------------------------------------------------

    private async Task ScanAsync()
    {
        var (quantity, text) = PosCart.ParseScan(_scan.Text);
        if (text.Length == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IPosLookupService>();
        var found = await lookup.FindForScanAsync(text);
        if (IsDisposed)
        {
            return;
        }

        if (found.IsSuccess)
        {
            AddToCart(found.Value, quantity);
            _scan.Clear();
            return;
        }

        // Not a barcode/code: treat it as a name search. One hit is added directly.
        var matches = await lookup.SearchAsync(text, take: 2);
        if (IsDisposed)
        {
            return;
        }

        if (matches.Count == 1)
        {
            AddToCart(matches[0], quantity);
            _scan.Clear();
        }
        else if (matches.Count > 1)
        {
            OpenSearch(text, quantity);
        }
        else
        {
            ShowStatus(found.Error.Message, LabelTone.Danger);
            System.Media.SystemSounds.Beep.Play();
            _scan.SelectAll();
        }
    }

    private void OpenSearch(string term, decimal quantity = 1)
    {
        using var dialog = new PosProductSearchDialog(_scopeFactory, PosCart.ParseScan(term).Text);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedProduct is { } product)
        {
            AddToCart(product, quantity);
            _scan.Clear();
        }

        FocusScan();
    }

    private void AddToCart(PosProductDto product, decimal quantity)
    {
        PosCartLine? line = null;
        if (!TryCart(() => line = _cart.Add(product, quantity)) || line is null)
        {
            return;
        }

        _grid.SelectItem(line);
        var text = $"Đã thêm {line.Name} × {line.Quantity.ToString("0.###", Vnd.Culture)} – {Vnd.Format(line.NetAmount)}";
        ShowStatus(line.ExceedsStock ? $"{text} (vượt tồn kho: còn {product.StockQuantity.ToString("0.###", Vnd.Culture)})" : text,
            line.ExceedsStock ? LabelTone.Warning : LabelTone.Success);
    }

    // ---- Line actions ------------------------------------------------------------------------------

    private PosCartLine? SelectedLine => _grid.GetSelected<PosCartLine>() ?? _cart.Lines.LastOrDefault();

    private void Step(decimal delta)
    {
        if (SelectedLine is { } line)
        {
            TryCart(() => _cart.Increment(line, delta));
            if (_cart.Lines.Contains(line))
            {
                _grid.SelectItem(line);
            }
        }
    }

    private void EditQuantity()
    {
        if (SelectedLine is not { } line)
        {
            return;
        }

        using var dialog = new PosQuantityDialog(line.Name, line.Unit, line.Quantity);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            TryCart(() => _cart.SetQuantity(line, dialog.Quantity));
            _grid.SelectItem(line);
        }

        FocusScan();
    }

    private void EditLineDiscount()
    {
        if (!_canDiscount)
        {
            ShowStatus("Bạn không có quyền giảm giá khi bán.", LabelTone.Danger);
            return;
        }

        if (SelectedLine is not { } line)
        {
            return;
        }

        using var dialog = new PosDiscountDialog($"Giảm giá – {line.Name}", line.GrossAmount, line.Discount);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            TryCart(() => _cart.SetLineDiscount(line, dialog.Discount));
            _grid.SelectItem(line);
        }

        FocusScan();
    }

    private void RemoveLine()
    {
        if (SelectedLine is { } line)
        {
            TryCart(() => _cart.Remove(line));
            ShowStatus($"Đã xóa {line.Name}.", LabelTone.Muted);
        }
    }

    private void UpdateLineButtons()
    {
        var hasLine = !_cart.IsEmpty;
        _quantity.Enabled = hasLine;
        _removeLine.Enabled = hasLine;
        _lineDiscount.Enabled = hasLine && _canDiscount;
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

    // ---- Customer & points -------------------------------------------------------------------------

    private async Task FindCustomerAsync()
    {
        if (string.IsNullOrWhiteSpace(_phone.Text))
        {
            _cart.DetachCustomer();
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPosLookupService>().FindCustomerByPhoneAsync(_phone.Text);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            _phone.SetError(result.Error.Message);
            return;
        }

        _phone.ClearValidation();
        _cart.AttachCustomer(PosCustomer.From(result.Value));
        ShowStatus($"Khách hàng: {result.Value.Name} ({result.Value.LoyaltyPoints:#,0} điểm)", LabelTone.Success);
        FocusScan();
    }

    private void ApplyPoints()
    {
        if (_rendering)
        {
            return;
        }

        var requested = (int)_points.ValueOrZero;
        if (!TryCart(() => _cart.RedeemPoints(requested)))
        {
            // Fall back to the most the bill allows rather than leaving an invalid number in the box.
            TryCart(() => _cart.RedeemPoints(_cart.MaxRedeemablePoints));
        }
    }

    // ---- Invoice discount & payment ----------------------------------------------------------------

    private void EditInvoiceDiscount()
    {
        if (!_canDiscount)
        {
            ShowStatus("Bạn không có quyền giảm giá khi bán.", LabelTone.Danger);
            return;
        }

        if (_cart.IsEmpty)
        {
            return;
        }

        using var dialog = new PosDiscountDialog("Giảm giá hóa đơn", _cart.Subtotal, _cart.InvoiceDiscount);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            TryCart(() => _cart.SetInvoiceDiscount(dialog.Discount));
        }

        FocusScan();
    }

    private void OnMethodChanged()
    {
        if (_rendering)
        {
            return;
        }

        var method = _transfer.Checked ? PaymentMethod.BankTransfer : _card.Checked ? PaymentMethod.Card : PaymentMethod.Cash;
        if (method != _cart.PaymentMethod)
        {
            _cart.SetPaymentMethod(method);
        }
    }

    private async Task CheckoutAsync()
    {
        if (_busy)
        {
            return;
        }

        _tendered.Commit();
        var request = _cart.BuildRequest();
        if (request.IsFailure)
        {
            ShowStatus(request.Error.Message, LabelTone.Danger);
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        _busy = true;
        _pay.Enabled = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICheckoutService>().CheckoutAsync(request.Value);
            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowStatus(result.Error.Message, LabelTone.Danger);
                MessageBox.Show(this, result.Error.Message, "Thanh toán", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var receipt = result.Value;
            _cart.Clear();
            _phone.Clear();
            _phone.ClearValidation();
            ShowStatus(DescribeReceipt(receipt), LabelTone.Success);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                _pay.Enabled = true;
                FocusScan();
            }
        }
    }

    private static string DescribeReceipt(CheckoutReceipt receipt)
    {
        var text = $"✔ {receipt.Code}: {Vnd.Format(receipt.Total)}";
        if (receipt.Change > 0)
        {
            text += $" · Tiền thừa {Vnd.Format(receipt.Change)}";
        }

        if (receipt.PointsEarned > 0)
        {
            text += $" · +{receipt.PointsEarned} điểm";
        }

        if (receipt.LowStockAlerts.Count > 0)
        {
            text += $" · Sắp hết: {string.Join(", ", receipt.LowStockAlerts.Take(3).Select(a => a.ProductName))}";
        }

        return text;
    }

    private void CancelSale()
    {
        if (_cart.IsEmpty)
        {
            FocusScan();
            return;
        }

        if (MessageBox.Show(this, "Hủy đơn hàng đang bán?", "Hủy đơn", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _cart.Clear();
            _phone.Clear();
            _phone.ClearValidation();
            ShowStatus("Đã hủy đơn hàng.", LabelTone.Muted);
        }

        FocusScan();
    }

    // ---- Rendering ---------------------------------------------------------------------------------

    private void Render()
    {
        _rendering = true;
        try
        {
            var selected = _grid.GetSelected<PosCartLine>();
            _grid.SetItems(_cart.Lines);
            if (selected is not null && _cart.Lines.Contains(selected))
            {
                _grid.SelectItem(selected);
            }
            else if (_cart.Lines.Count > 0)
            {
                _grid.SelectItem(_cart.Lines[^1]);
            }

            UpdateLineButtons();

            if (_cart.Customer is { } customer)
            {
                _customerInfo.Text = $"{customer.Name} · {customer.Code}\r\nĐiểm hiện có: {customer.LoyaltyPoints:#,0} (đổi tối đa {_cart.MaxRedeemablePoints:#,0})";
                _customerInfo.Tone = LabelTone.Normal;
            }
            else
            {
                _customerInfo.Text = "Khách lẻ";
                _customerInfo.Tone = LabelTone.Muted;
            }

            var hasMember = _cart.Customer is not null;
            _points.Enabled = hasMember;
            _maxPoints.Enabled = hasMember && _cart.MaxRedeemablePoints > 0;
            _detachCustomer.Enabled = hasMember;
            if ((int)_points.ValueOrZero != _cart.PointsToRedeem)
            {
                _points.Value = _cart.PointsToRedeem;
            }

            _gross.Text = Vnd.Format(_cart.GrossAmount);
            _lineDiscounts.Text = Minus(_cart.LineDiscountTotal);
            _invoiceDiscount.Text = Minus(_cart.InvoiceDiscountAmount) + (_cart.InvoiceDiscount is { } d ? $" ({FormatDiscount(d)})" : string.Empty);
            _pointsDiscount.Text = _cart.PointsToRedeem > 0 ? $"{Minus(_cart.PointsDiscountAmount)} ({_cart.PointsToRedeem:#,0} điểm)" : Minus(0);
            _total.Text = Vnd.Format(_cart.Total);
            _earn.Text = hasMember ? $"Khách được cộng {_cart.PointsToEarn:#,0} điểm" : string.Empty;
            _invoiceDiscountButton.Enabled = _canDiscount && !_cart.IsEmpty;

            _cash.Checked = _cart.PaymentMethod == PaymentMethod.Cash;
            _transfer.Checked = _cart.PaymentMethod == PaymentMethod.BankTransfer;
            _card.Checked = _cart.PaymentMethod == PaymentMethod.Card;

            var isCash = _cart.PaymentMethod == PaymentMethod.Cash;
            _tendered.Visible = isCash;
            _quickCash.Visible = isCash;
            _reference.Visible = !isCash;
            if (_cart.CashTendered is null && _tendered.ValueOrZero != 0 && !_tendered.ContainsFocus)
            {
                _tendered.Value = 0;
            }

            if (_cart.PaymentReference is null && _reference.Text.Length > 0)
            {
                _reference.Clear();
            }

            RenderQuickCash();

            _change.Text = Vnd.Format(_cart.Change);
            _blocker.Text = _cart.IsEmpty ? string.Empty : _cart.CheckoutBlocker ?? string.Empty;
            _pay.Enabled = !_busy && _cart.CanCheckout;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderQuickCash()
    {
        if (_cart.Total == _quickCashTotal)
        {
            return;
        }

        _quickCashTotal = _cart.Total;
        _quickCash.SuspendLayout();
        foreach (Control control in _quickCash.Controls)
        {
            control.Dispose();
        }

        _quickCash.Controls.Clear();
        foreach (var amount in _cart.SuggestCashAmounts())
        {
            var button = new ModernButton
            {
                Text = Vnd.FormatNumber(amount),
                Variant = ButtonVariant.Secondary,
                Size = new Size(116, 34),
                Margin = new Padding(0, 0, 6, 6),
            };
            button.Click += (_, _) =>
            {
                _tendered.Value = amount;
                _pay.Focus();
            };
            _quickCash.Controls.Add(button);
        }

        ThemeManager.Apply(_quickCash);
        _quickCash.ResumeLayout(true);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>Runs a cart change, turning domain rule violations into the status line instead of exceptions.</summary>
    private bool TryCart(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (DomainException ex)
        {
            ShowStatus(ex.Message, LabelTone.Danger);
            return false;
        }
    }

    private void ShowStatus(string text, LabelTone tone)
    {
        _status.Text = text;
        _status.Tone = tone;
    }

    private void FocusScan()
    {
        if (!IsDisposed)
        {
            _scan.Focus();
            _scan.SelectAll();
        }
    }

    private static string Minus(decimal amount) => amount == 0 ? "0 ₫" : "−" + Vnd.Format(amount);

    private static string FormatDiscount(DiscountInput discount) =>
        discount.Kind == DiscountKind.Percent ? $"{discount.Value.ToString("0.##", Vnd.Culture)}%" : Vnd.Format(discount.Value);

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
