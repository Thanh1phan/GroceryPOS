using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Phiếu nhập hàng editor: supplier, supplier invoice number and note; scan (<c>24*mã</c>), F3 search or F4 reorder
/// suggestions to add lines at the last cost; per-line quantity, supplier price, line discount and optional new shelf
/// price with a live preview of landed cost, new average cost and margin; receipt discount, amount paid ("Trả đủ")
/// and the resulting debt. Ctrl+S saves the draft, F9 saves and posts it, then shows the completion summary.
/// State lives in <see cref="GoodsReceiptDraft"/>.
/// </summary>
internal sealed class GoodsReceiptEditorForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GoodsReceiptDraft _draft;
    private readonly IReadOnlyList<SupplierDto> _suppliers;

    private readonly Label _heading;
    private readonly ModernComboBox _supplier;
    private readonly Label _supplierInfo;
    private readonly ModernTextBox _invoiceNo;
    private readonly ModernTextBox _note;
    private readonly ModernTextBox _scan;
    private readonly Label _message;
    private readonly ModernDataGrid _grid;

    private readonly Label _lineTitle;
    private readonly Label _lineInfo;
    private readonly ModernNumericBox _qty;
    private readonly ModernCurrencyBox _cost;
    private readonly ModernCurrencyBox _lineDiscount;
    private readonly ModernCurrencyBox _newPrice;
    private readonly Label _linePreview;
    private readonly ModernButton _applyLine;
    private readonly ModernButton _removeLine;

    private readonly Label _amounts;
    private readonly ModernCurrencyBox _discount;
    private readonly Label _total;
    private readonly ModernCurrencyBox _paid;
    private readonly CheckBox _payInFull;
    private readonly Label _debt;
    private readonly Label _status;
    private readonly ModernButton _save;
    private readonly ModernButton _complete;
    private bool _rendering;
    private bool _busy;

    public GoodsReceiptEditorForm(
        IServiceScopeFactory scopeFactory,
        GoodsReceiptDraft draft,
        IReadOnlyList<SupplierDto> suppliers)
    {
        _scopeFactory = scopeFactory;
        _draft = draft;
        // Inactive suppliers are refused by the service; keep the draft's own supplier visible so it can be changed.
        _suppliers = suppliers.Where(s => s.IsActive || s.Id == draft.SupplierId).ToList();
        _draft.Changed += (_, _) => RenderTotals();

        Text = "Phiếu nhập hàng";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1320, 780);
        MinimumSize = new Size(1100, 640);
        Padding = new Padding(20);
        KeyPreview = true;

        // ---- Header: supplier + references --------------------------------------------------------------
        _heading = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font(Font.FontFamily, 13, FontStyle.Bold) };
        _supplier = new ModernComboBox
        {
            Width = 320,
            DropDownStyle = ComboBoxStyle.DropDown,
            FilterAsYouType = true,
            LimitToList = true,
            PlaceholderText = "Chọn nhà cung cấp…",
            IconGlyph = Glyphs.People,
            ReserveMessageSpace = false,
            DisplayMember = nameof(SupplierOption.Display),
            ValueMember = nameof(SupplierOption.Id),
            DataSource = _suppliers.Select(s => new SupplierOption(s.Id, $"{s.Code} – {s.Name}")).ToList(),
        };
        _supplier.SelectedIndexChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _draft.SetSupplier(_supplier.SelectedValue as int?);
                RenderSupplierInfo();
            }
        };
        _invoiceNo = new ModernTextBox { Width = 170, PlaceholderText = "Số HĐ của NCC", MaxLength = 50, ReserveMessageSpace = false, Margin = new Padding(12, 0, 0, 0) };
        _note = new ModernTextBox { Width = 420, PlaceholderText = "Ghi chú (giao hàng, người nhận…)", MaxLength = 500, ReserveMessageSpace = false, Margin = new Padding(12, 0, 0, 0) };
        _invoiceNo.InnerTextBox.TextChanged += (_, _) => UpdateReference();
        _note.InnerTextBox.TextChanged += (_, _) => UpdateReference();
        var headerRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        headerRow.Controls.AddRange([_supplier, _invoiceNo, _note]);
        _supplierInfo = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = SystemColors.GrayText };

        // ---- Scan row -----------------------------------------------------------------------------------
        _scan = new ModernTextBox { Width = 380, IconGlyph = Glyphs.Barcode, PlaceholderText = "Quét mã vạch / nhập mã SP (24*mã = 24 cái)  F2", ReserveMessageSpace = false };
        _scan.EnterPressed += (_, _) => ScanNow();
        var search = SmallButton("Tìm SP (F3)", Glyphs.Search, ButtonVariant.Secondary, PickProduct);
        var suggest = SmallButton("Thêm hàng sắp hết (F4)", Glyphs.Warning, ButtonVariant.Secondary, async () => await AddSuggestionsAsync());
        var scanRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        scanRow.Controls.AddRange([_scan, search, suggest]);
        _message = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = SystemColors.GrayText, Text = "Giá nhập mặc định là giá vốn hiện tại; sửa ở khung bên phải. Tồn kho chỉ tăng khi hoàn thành phiếu." };

        // ---- Lines grid ---------------------------------------------------------------------------------
        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Quét mã, nhấn F3 để tìm hoặc F4 để thêm các SP sắp hết hàng.", PageSize = 200, ShowPager = false };
        _grid.AddTextColumn(nameof(GoodsReceiptDraftLine.ProductCode), "Mã SP", width: 90);
        _grid.AddTextColumn(nameof(GoodsReceiptDraftLine.ProductName), "Tên sản phẩm");
        _grid.AddTextColumn(nameof(GoodsReceiptDraftLine.Unit), "ĐVT", width: 55);
        _grid.AddNumberColumn(nameof(GoodsReceiptDraftLine.StockBefore), "Tồn", width: 70, decimals: 3);
        _grid.AddNumberColumn(nameof(GoodsReceiptDraftLine.Quantity), "SL nhập", width: 80, decimals: 3);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.UnitCost), "Giá nhập", width: 105);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.DiscountAmount), "CK", width: 90);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.NetAmount), "Thành tiền", width: 120);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.EffectiveUnitCost), "Giá thực", width: 100);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.NewAverageCost), "Giá vốn mới", width: 105);
        _grid.AddMoneyColumn(nameof(GoodsReceiptDraftLine.SellPriceAfter), "Giá bán", width: 100);
        _grid.AddNumberColumn(nameof(GoodsReceiptDraftLine.MarginPercent), "Lãi %", width: 65, decimals: 1);
        _grid.AddTextColumn(nameof(GoodsReceiptDraftLine.StatusName), "Lưu ý", width: 110);
        _grid.SelectionChanged += (_, _) => RenderSelected();
        _grid.RowActivated += (_, _) => FocusLineEditor();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;
        _grid.InnerGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
                e.Handled = true;
            }
        };

        // ---- Right: line editor -------------------------------------------------------------------------
        _lineTitle = new Label { Dock = DockStyle.Top, Height = 44, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Chọn một dòng để sửa" };
        _lineInfo = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = SystemColors.GrayText };
        _qty = new ModernNumericBox { Dock = DockStyle.Top, DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, Enabled = false };
        _cost = new ModernCurrencyBox { Dock = DockStyle.Top, Minimum = 0, Maximum = 1_000_000_000, Enabled = false };
        _lineDiscount = new ModernCurrencyBox { Dock = DockStyle.Top, Minimum = 0, Maximum = 10_000_000_000, Enabled = false };
        _newPrice = new ModernCurrencyBox { Dock = DockStyle.Top, Minimum = 0, Maximum = 1_000_000_000, AllowEmpty = true, Enabled = false };
        foreach (var box in new ModernNumericBox[] { _qty, _cost, _lineDiscount, _newPrice })
        {
            box.EnterPressed += (_, _) => ApplyLine(backToScan: box == _newPrice || (box == _lineDiscount && !_draft.CanChangePrices));
        }

        _linePreview = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(0, 6, 0, 0) };
        _applyLine = new ModernButton { Text = "Ghi dòng (Enter)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Accept, Width = 280, Height = 38, Margin = new Padding(0, 0, 0, 6), Enabled = false };
        _applyLine.Click += (_, _) => ApplyLine(backToScan: true);
        _removeLine = new ModernButton { Text = "Bỏ dòng (Del)", Variant = ButtonVariant.Ghost, IconGlyph = Glyphs.Delete, Width = 280, Height = 38, Enabled = false };
        _removeLine.Click += (_, _) => RemoveSelected();
        var lineButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        lineButtons.Controls.AddRange([_applyLine, _removeLine]);

        var side = new Panel { Dock = DockStyle.Right, Width = 300, Padding = new Padding(16, 0, 0, 0) };
        side.Controls.Add(lineButtons);
        side.Controls.Add(_linePreview);
        side.Controls.Add(_newPrice);
        side.Controls.Add(Caption(_draft.CanChangePrices ? "Giá bán mới (để trống = giữ nguyên)" : "Giá bán mới (cần quyền đổi giá)"));
        side.Controls.Add(_lineDiscount);
        side.Controls.Add(Caption("Chiết khấu dòng (₫)"));
        side.Controls.Add(_cost);
        side.Controls.Add(Caption("Giá nhập / đơn vị (chưa CK)"));
        side.Controls.Add(_qty);
        side.Controls.Add(Caption("Số lượng nhập"));
        side.Controls.Add(_lineInfo);
        side.Controls.Add(_lineTitle);

        // ---- Bottom: totals + actions -------------------------------------------------------------------
        _amounts = new Label { AutoSize = true, Margin = new Padding(0, 12, 16, 0) };
        _discount = new ModernCurrencyBox { Width = 150, Minimum = 0, Maximum = 10_000_000_000, ReserveMessageSpace = false };
        _discount.ValueChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _draft.SetDiscount(_discount.ValueOrZero);
            }
        };
        _total = new Label { AutoSize = true, Margin = new Padding(16, 8, 16, 0), Font = new Font(Font.FontFamily, 13, FontStyle.Bold) };
        _paid = new ModernCurrencyBox { Width = 150, Minimum = 0, Maximum = 10_000_000_000, ReserveMessageSpace = false };
        _paid.ValueChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _draft.SetPaidAmount(_paid.ValueOrZero);
            }
        };
        _payInFull = new CheckBox { Text = "Trả đủ", AutoSize = true, Margin = new Padding(8, 12, 16, 0) };
        _payInFull.CheckedChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _draft.SetPayInFull(_payInFull.Checked);
            }
        };
        _debt = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
        var totalsRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
        totalsRow.Controls.AddRange([
            _amounts,
            new Label { Text = "CK phiếu", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _discount,
            _total,
            new Label { Text = "Đã trả NCC", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _paid, _payInFull,
            _debt,
        ]);
        _status = new Label { Dock = DockStyle.Top, Height = 40 };

        _save = new ModernButton { Text = "Lưu phiếu tạm (Ctrl+S)", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Save, Size = new Size(210, 40), Margin = new Padding(0, 0, 8, 0) };
        _save.Click += async (_, _) => await SaveAsync(showMessage: true);
        _complete = new ModernButton { Text = "Hoàn thành nhập kho (F9)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Accept, Size = new Size(240, 40), Margin = new Padding(0, 0, 8, 0) };
        _complete.Click += async (_, _) => await CompleteAsync();
        var close = new ModernButton { Text = "Đóng", Variant = ButtonVariant.Ghost, Size = new Size(100, 40) };
        close.Click += (_, _) => Close();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([_save, _complete, close]);
        var actionRow = new Panel { Dock = DockStyle.Top, Height = 44 };
        actionRow.Controls.Add(actions);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 140, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(actionRow);
        bottom.Controls.Add(_status);
        bottom.Controls.Add(totalsRow);

        Controls.Add(_grid);
        Controls.Add(side);
        Controls.Add(bottom);
        Controls.Add(_message);
        Controls.Add(scanRow);
        Controls.Add(_supplierInfo);
        Controls.Add(headerRow);
        Controls.Add(_heading);

        FormClosing += (_, e) =>
        {
            if (!_busy && DialogResult != DialogResult.OK && _draft.IsDirty && _draft.Lines.Count > 0)
            {
                var answer = MessageBox.Show(
                    this,
                    "Phiếu nhập có thay đổi chưa lưu. Lưu phiếu tạm trước khi đóng?",
                    "Phiếu nhập hàng",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                }
                else if (answer == DialogResult.Yes)
                {
                    e.Cancel = true;
                    _ = SaveAndCloseAsync();
                }
            }
        };
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            RenderHeader();
            RenderLines();
        };
        Shown += (_, _) =>
        {
            if (_draft.SupplierId is null)
            {
                _supplier.Focus();
            }
            else
            {
                _scan.Focus();
            }
        };
    }

    /// <summary>The draft was saved at least once (the list should reload).</summary>
    public bool Saved { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F2:
                _scan.Focus();
                return true;
            case Keys.F3:
                PickProduct();
                return true;
            case Keys.F4:
                _ = AddSuggestionsAsync();
                return true;
            case Keys.F9:
                _ = CompleteAsync();
                return true;
            case Keys.Control | Keys.S:
                _ = SaveAsync(showMessage: true);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static Label Caption(string text) => new() { Dock = DockStyle.Top, Height = 24, Text = text, Padding = new Padding(0, 6, 0, 0) };

    private static ModernButton SmallButton(string text, string glyph, ButtonVariant variant, Action onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private GoodsReceiptDraftLine? Selected => _grid.GetSelected<GoodsReceiptDraftLine>();

    private void Report(string message, bool error = false)
    {
        _message.Text = message;
        _message.ForeColor = error ? ThemeManager.Current.Colors.Danger : ThemeManager.Current.Colors.TextSecondary;
    }

    private void UpdateReference()
    {
        if (!_rendering)
        {
            _draft.SetReference(_invoiceNo.Text, _note.Text);
        }
    }

    private void ScanNow()
    {
        var result = _draft.Scan(_scan.Text);
        switch (result.Outcome)
        {
            case GoodsReceiptScanOutcome.Empty:
                return;
            case GoodsReceiptScanOutcome.NotFound:
            case GoodsReceiptScanOutcome.Inactive:
                System.Media.SystemSounds.Beep.Play();
                Report(result.Message, error: true);
                break;
            default:
                Report(result.Message);
                RenderLines(result.Line);
                break;
        }

        _scan.Clear();
        _scan.Focus();
    }

    private void PickProduct()
    {
        using var dialog = new CatalogPickerDialog(_draft.Catalog, _scan.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedProduct is not { } product)
        {
            return;
        }

        var existed = _draft.Find(product.ProductId) is not null;
        var added = _draft.Add(product.ProductId);
        if (added.IsFailure)
        {
            Report(added.Error.Message, error: true);
            return;
        }

        _scan.Clear();
        Report(existed ? $"{product.ProductName}: tăng thêm 1 {product.Unit}." : $"Đã thêm {product.ProductName}. Nhập số lượng và giá nhập.");
        RenderLines(added.Value);
        FocusLineEditor();
    }

    private async Task AddSuggestionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().SuggestReorderAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            Report(result.Error.Message, error: true);
            return;
        }

        var added = _draft.AddSuggestions(result.Value);
        Report(result.Value.Count == 0
            ? "Không có sản phẩm nào dưới định mức tồn kho."
            : added == 0 ? "Các sản phẩm sắp hết đã có trong phiếu."
            : $"Đã thêm {added} sản phẩm sắp hết với số lượng đề xuất (bù lên 2× định mức) – kiểm tra lại theo hàng NCC giao.");
        RenderLines();
    }

    private void FocusLineEditor()
    {
        if (Selected is not null)
        {
            _qty.Focus();
            _qty.SelectAll();
        }
    }

    private void ApplyLine(bool backToScan)
    {
        if (Selected is not { } line)
        {
            return;
        }

        foreach (var box in new ModernNumericBox[] { _qty, _cost, _lineDiscount, _newPrice })
        {
            box.Commit();
            box.ClearValidation();
        }

        var result = _draft.UpdateLine(line.ProductId, _qty.ValueOrZero, _cost.ValueOrZero, _lineDiscount.ValueOrZero, _newPrice.Value);
        if (result.IsFailure)
        {
            var message = result.Error.Message;
            var target = message.Contains("Số lượng", StringComparison.Ordinal) ? (ModernNumericBox)_qty
                : message.Contains("Chiết khấu", StringComparison.Ordinal) ? _lineDiscount
                : message.Contains("giá bán", StringComparison.OrdinalIgnoreCase) ? _newPrice
                : _cost;
            target.SetError(message);
            target.Focus();
            return;
        }

        RenderLines(line);
        if (backToScan)
        {
            _scan.Focus();
        }
        else
        {
            SelectNextNumericBox();
        }
    }

    private void SelectNextNumericBox()
    {
        ModernNumericBox? next =
            _qty.ContainsFocus ? _cost
            : _cost.ContainsFocus ? _lineDiscount
            : _lineDiscount.ContainsFocus && _draft.CanChangePrices ? _newPrice
            : null;
        if (next is null)
        {
            _scan.Focus();
            return;
        }

        next.Focus();
        next.SelectAll();
    }

    private void RemoveSelected()
    {
        if (Selected is { } line && _draft.Remove(line.ProductId))
        {
            Report($"Đã bỏ {line.ProductName} khỏi phiếu.");
            RenderLines();
        }
    }

    // ---- Rendering --------------------------------------------------------------------------------------

    private void RenderHeader()
    {
        _rendering = true;
        try
        {
            _heading.Text = _draft.Code is { } code ? $"Phiếu nhập {code} · Phiếu tạm" : "Phiếu nhập hàng mới";
            Text = _draft.Code is { } c ? $"Phiếu nhập {c}" : "Phiếu nhập hàng mới";
            _supplier.SelectedValue = _draft.SupplierId;
            if (_draft.SupplierId is null)
            {
                _supplier.SelectedIndex = -1;
            }

            _invoiceNo.Text = _draft.SupplierInvoiceNo ?? string.Empty;
            _note.Text = _draft.Note ?? string.Empty;
        }
        finally
        {
            _rendering = false;
        }

        RenderSupplierInfo();
    }

    private void RenderSupplierInfo()
    {
        var supplier = _suppliers.FirstOrDefault(s => s.Id == _draft.SupplierId);
        _supplierInfo.Text = supplier is null
            ? "Chọn nhà cung cấp giao hàng (gõ để lọc theo mã hoặc tên)."
            : $"{supplier.Name}{(supplier.Phone is { } phone ? $" · {phone}" : string.Empty)} · công nợ hiện tại {Vnd.Format(supplier.DebtBalance)}"
              + (supplier.IsActive ? string.Empty : " · ĐÃ NGỪNG GIAO DỊCH");
    }

    private void RenderLines(GoodsReceiptDraftLine? select = null)
    {
        var keep = select ?? Selected;
        _grid.SetItems(_draft.Lines);
        if (keep is not null)
        {
            _grid.SelectItem(keep);
        }

        RenderSelected();
        RenderTotals();
    }

    private void RenderSelected()
    {
        var line = Selected;
        var enabled = line is not null;
        _qty.Enabled = _cost.Enabled = _lineDiscount.Enabled = enabled;
        _newPrice.Enabled = enabled && _draft.CanChangePrices;
        _applyLine.Enabled = _removeLine.Enabled = enabled;
        foreach (var box in new ModernNumericBox[] { _qty, _cost, _lineDiscount, _newPrice })
        {
            box.ClearValidation();
        }

        if (line is null)
        {
            _lineTitle.Text = "Chọn một dòng để sửa";
            _lineInfo.Text = string.Empty;
            _linePreview.Text = string.Empty;
            _qty.Value = _cost.Value = _lineDiscount.Value = _newPrice.Value = null;
            return;
        }

        _lineTitle.Text = $"{line.ProductCode} – {line.ProductName}";
        _lineInfo.Text = $"Tồn {GoodsReceiptDraft.FormatQty(line.StockBefore)} {line.Unit} · giá vốn {Vnd.Format(line.CurrentCost)}\nGiá bán hiện tại {Vnd.Format(line.CurrentSellPrice)}";
        _qty.SuffixText = line.Unit;
        _qty.Value = line.Quantity;
        _cost.Value = line.UnitCost;
        _lineDiscount.Value = line.DiscountAmount;
        _newPrice.Value = line.NewSellPrice;
        RenderPreview(line);
    }

    private void RenderPreview(GoodsReceiptDraftLine line)
    {
        var colors = ThemeManager.Current.Colors;
        var change = line.CostChangePercent is { } pct ? $" ({(pct > 0 ? "+" : string.Empty)}{pct.ToString("0.#", Vnd.Culture)}%)" : string.Empty;
        var margin = line.MarginPercent is { } m ? $"{m.ToString("0.#", Vnd.Culture)}%" : "–";
        _linePreview.Text =
            $"Giá nhập thực tế: {Vnd.Format(line.EffectiveUnitCost)}{change}\n"
            + $"Giá vốn sau nhập: {Vnd.Format(line.NewAverageCost)} · tồn sau {GoodsReceiptDraft.FormatQty(line.StockAfter)}\n"
            + $"Giá bán {Vnd.Format(line.SellPriceAfter)} · lãi gộp {margin}";
        _linePreview.ForeColor = line.IsBelowCost ? colors.Danger
            : line.CostChangePercent >= GoodsReceiptDraft.CostJumpPercent ? colors.Warning
            : colors.TextSecondary;
    }

    private void RenderTotals()
    {
        _rendering = true;
        try
        {
            _amounts.Text = $"{_draft.Lines.Count} SP · SL {GoodsReceiptDraft.FormatQty(_draft.TotalQuantity)} · tiền hàng {Vnd.Format(_draft.Subtotal)}"
                + (_draft.LineDiscountTotal > 0 ? $" (đã CK dòng {Vnd.Format(_draft.LineDiscountTotal)})" : string.Empty);
            if (!_discount.ContainsFocus)
            {
                _discount.Value = _draft.DiscountAmount;
            }

            _total.Text = $"Tổng {Vnd.Format(_draft.Total)}";
            _payInFull.Checked = _draft.PayInFull;
            if (_draft.PayInFull || !_paid.ContainsFocus)
            {
                _paid.Value = _draft.PaidAmount;
            }

            _debt.Text = $"Ghi nợ NCC: {Vnd.Format(_draft.DebtAmount)}";
            var colors = ThemeManager.Current.Colors;
            _debt.ForeColor = _draft.DebtAmount > 0 ? colors.Warning : colors.TextSecondary;

            if (_draft.Blocker is { } blocker)
            {
                _status.Text = blocker;
                _status.ForeColor = _draft.Lines.Count == 0 || _draft.SupplierId is null ? colors.TextMuted : colors.Danger;
            }
            else if (_draft.Warnings is { Count: > 0 } warnings)
            {
                _status.Text = string.Join("\n", warnings.Take(2));
                _status.ForeColor = colors.Warning;
            }
            else
            {
                _status.Text = _draft.IsDirty ? "Có thay đổi chưa lưu." : "Sẵn sàng hoàn thành nhập kho.";
                _status.ForeColor = _draft.IsDirty ? colors.TextSecondary : colors.Success;
            }

            _save.Enabled = !_busy && _draft.CanSave;
            _complete.Enabled = !_busy && _draft.CanSave;
            _heading.Text = (_draft.Code is { } code ? $"Phiếu nhập {code} · Phiếu tạm" : "Phiếu nhập hàng mới") + (_draft.IsDirty ? " *" : string.Empty);

            if (Selected is { } line)
            {
                RenderPreview(line);
            }

            _grid.RefreshFields();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not GoodsReceiptDraftLine line || e.CellStyle is not { } style)
        {
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        var colors = ThemeManager.Current.Colors;
        switch (property)
        {
            case nameof(GoodsReceiptDraftLine.StatusName):
            case nameof(GoodsReceiptDraftLine.MarginPercent):
            case nameof(GoodsReceiptDraftLine.NewAverageCost):
                if (line.IsBelowCost || !line.Product.IsActive)
                {
                    style.ForeColor = colors.Danger;
                }
                else if (property != nameof(GoodsReceiptDraftLine.MarginPercent) && line.CostChangePercent >= GoodsReceiptDraft.CostJumpPercent)
                {
                    style.ForeColor = colors.Warning;
                }

                break;
            case nameof(GoodsReceiptDraftLine.SellPriceAfter) when line.NewSellPrice is { } price && price != line.CurrentSellPrice:
                style.ForeColor = colors.Primary;
                break;
        }
    }

    // ---- Save / complete -------------------------------------------------------------------------------

    private async Task<bool> SaveAsync(bool showMessage)
    {
        if (_busy)
        {
            return false;
        }

        var request = _draft.BuildRequest();
        if (request.IsFailure)
        {
            Report(request.Error.Message, error: true);
            return false;
        }

        SetBusy(true);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().SaveDraftAsync(request.Value);
            if (IsDisposed)
            {
                return false;
            }

            if (result.IsFailure)
            {
                Report(result.Error.Message, error: true);
                return false;
            }

            _draft.MarkSaved(result.Value);
            Saved = true;
            if (showMessage)
            {
                Report($"Đã lưu phiếu tạm {result.Value.Code}. Tồn kho chưa thay đổi cho tới khi hoàn thành phiếu.");
            }

            return true;
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private async Task SaveAndCloseAsync()
    {
        if (await SaveAsync(showMessage: false) && !IsDisposed)
        {
            DialogResult = DialogResult.OK;
        }
    }

    private async Task CompleteAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_draft.Blocker is { } blocker)
        {
            Report(blocker, error: true);
            return;
        }

        var supplier = _suppliers.FirstOrDefault(s => s.Id == _draft.SupplierId);
        var below = _draft.BelowCostLines.ToList();
        var message =
            $"Hoàn thành phiếu nhập từ {supplier?.Name}?\n\n"
            + $"{_draft.Lines.Count} sản phẩm · tổng {Vnd.Format(_draft.Total)} · đã trả {Vnd.Format(_draft.PaidAmount)} · ghi nợ {Vnd.Format(_draft.DebtAmount)}.\n"
            + "Tồn kho và giá vốn sẽ được cập nhật; phiếu đã hoàn thành không sửa hay hủy được (chỉ trả hàng NCC)."
            + (below.Count > 0 ? $"\n\n⚠ {below.Count} sản phẩm sẽ có giá bán ≤ giá vốn mới." : string.Empty);
        if (MessageBox.Show(this, message, "Hoàn thành nhập kho", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        if ((_draft.IsDirty || _draft.Id is null) && !await SaveAsync(showMessage: false))
        {
            return;
        }

        SetBusy(true);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IGoodsReceiptService>().CompleteAsync(_draft.Id!.Value);
            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                Report(result.Error.Message, error: true);
                return;
            }

            using (var summary = new GoodsReceiptCompletionForm(result.Value, supplier?.Name ?? string.Empty))
            {
                summary.ShowDialog(this);
            }

            DialogResult = DialogResult.OK;
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _save.Enabled = !busy && _draft.CanSave;
        _complete.Enabled = !busy && _draft.CanSave;
    }

    private sealed record SupplierOption(int Id, string Display);
}
