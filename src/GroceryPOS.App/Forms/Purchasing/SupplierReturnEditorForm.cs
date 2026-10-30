using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Trả hàng nhà cung cấp: type or scan a goods receipt number (PN…) to return against it – every line listed with
/// received / already returned / stock and the landed cost as the price cap – or pick a supplier for a free return
/// and scan products from stock. Per line quantity and return price; settlement offsets the debt first ("Cấn trừ
/// công nợ trước") or takes a typed amount back in cash/transfer; reason chips and note; F9 posts the return.
/// State lives in <see cref="SupplierReturnSheet"/>.
/// </summary>
internal sealed class SupplierReturnEditorForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<StockItem> _catalog;
    private readonly IReadOnlyList<SupplierDto> _suppliers;
    private readonly TimeSpan _storeOffset;
    private SupplierReturnSheet? _sheet;
    private Font? _boldCellFont;

    private readonly ModernTextBox _receiptCode;
    private readonly ModernComboBox _supplier;
    private readonly Label _modeInfo;
    private readonly ModernTextBox _scan;
    private readonly ModernButton _search;
    private readonly ModernButton _all;
    private readonly Label _message;
    private readonly ModernDataGrid _grid;

    private readonly Label _lineTitle;
    private readonly Label _lineInfo;
    private readonly ModernNumericBox _qty;
    private readonly ModernCurrencyBox _price;
    private readonly ModernButton _applyLine;
    private readonly ModernButton _removeLine;

    private readonly Label _totals;
    private readonly CheckBox _autoSettle;
    private readonly ModernCurrencyBox _refund;
    private readonly RadioButton _cash;
    private readonly RadioButton _transfer;
    private readonly Label _debt;
    private readonly ModernTextBox _reason;
    private readonly ModernTextBox _note;
    private readonly Label _status;
    private readonly ModernButton _submit;
    private bool _rendering;
    private bool _busy;

    public SupplierReturnEditorForm(
        IServiceScopeFactory scopeFactory,
        IReadOnlyList<StockItem> catalog,
        IReadOnlyList<SupplierDto> suppliers,
        string? receiptCode = null,
        int? supplierId = null)
    {
        _scopeFactory = scopeFactory;
        _catalog = catalog;
        _suppliers = suppliers;
        using (var scope = scopeFactory.CreateScope())
        {
            _storeOffset = (scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default).StoreUtcOffset;
        }

        Text = "Trả hàng nhà cung cấp";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1080, 640);
        Padding = new Padding(20);
        KeyPreview = true;

        // ---- Mode: by receipt or free return to a supplier ----------------------------------------------
        var heading = new Label { Dock = DockStyle.Top, Height = 34, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Text = "Phiếu trả hàng nhà cung cấp" };
        _receiptCode = new ModernTextBox { Width = 300, IconGlyph = Glyphs.Document, PlaceholderText = "Số phiếu nhập (PN…) rồi Enter", MaxLength = 32, ReserveMessageSpace = false };
        _receiptCode.EnterPressed += async (_, _) => await LoadReceiptAsync(_receiptCode.Text);
        _supplier = new ModernComboBox
        {
            Width = 340,
            DropDownStyle = ComboBoxStyle.DropDown,
            FilterAsYouType = true,
            LimitToList = true,
            PlaceholderText = "…hoặc chọn NCC để trả không theo phiếu",
            IconGlyph = Glyphs.People,
            ReserveMessageSpace = false,
            DisplayMember = nameof(SupplierOption.Display),
            ValueMember = nameof(SupplierOption.Id),
            DataSource = _suppliers.Select(s => new SupplierOption(s.Id, $"{s.Code} – {s.Name}")).ToList(),
            Margin = new Padding(12, 0, 0, 0),
        };
        _supplier.SelectedIndexChanged += (_, _) =>
        {
            if (!_rendering && _supplier.SelectedValue is int id)
            {
                StartFreeReturn(id);
            }
        };
        var modeRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        modeRow.Controls.AddRange([_receiptCode, _supplier]);
        _modeInfo = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = SystemColors.GrayText };

        // ---- Scan row -----------------------------------------------------------------------------------
        _scan = new ModernTextBox { Width = 380, IconGlyph = Glyphs.Barcode, PlaceholderText = "Quét mã vạch / mã SP (6*mã = 6 cái)  F2", ReserveMessageSpace = false };
        _scan.EnterPressed += (_, _) => ScanNow();
        _search = SmallButton("Tìm SP (F3)", Glyphs.Search, PickProduct);
        _all = SmallButton("Trả hết theo phiếu (F7)", Glyphs.Return, ReturnAll);
        var scanRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        scanRow.Controls.AddRange([_scan, _search, _all]);
        _message = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = SystemColors.GrayText };

        // ---- Lines grid ---------------------------------------------------------------------------------
        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Nhập số phiếu nhập, hoặc chọn nhà cung cấp rồi quét mã sản phẩm cần trả.", PageSize = 200, ShowPager = false };
        _grid.AddTextColumn(nameof(SupplierReturnSheetLine.ProductCode), "Mã SP", width: 90);
        _grid.AddTextColumn(nameof(SupplierReturnSheetLine.ProductName), "Tên sản phẩm");
        _grid.AddTextColumn(nameof(SupplierReturnSheetLine.Unit), "ĐVT", width: 55);
        _grid.AddNumberColumn(nameof(SupplierReturnSheetLine.ReceivedQuantity), "SL nhập", width: 80, decimals: 3);
        _grid.AddNumberColumn(nameof(SupplierReturnSheetLine.ReturnedBefore), "Đã trả", width: 75, decimals: 3);
        _grid.AddNumberColumn(nameof(SupplierReturnSheetLine.StockQuantity), "Tồn", width: 75, decimals: 3);
        _grid.AddNumberColumn(nameof(SupplierReturnSheetLine.Quantity), "SL trả", width: 80, decimals: 3);
        _grid.AddMoneyColumn(nameof(SupplierReturnSheetLine.UnitPrice), "Giá trả", width: 105);
        _grid.AddMoneyColumn(nameof(SupplierReturnSheetLine.Amount), "Thành tiền", width: 120);
        _grid.AddMoneyColumn(nameof(SupplierReturnSheetLine.CostPrice), "Giá vốn", width: 100);
        _grid.AddTextColumn(nameof(SupplierReturnSheetLine.LimitText), "Lưu ý", width: 120);
        _grid.SelectionChanged += (_, _) => RenderSelected();
        _grid.RowActivated += (_, _) => FocusLineEditor();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;
        _grid.InnerGrid.KeyDown += (_, e) =>
        {
            if (Selected is not { } line || _sheet is null)
            {
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Delete:
                    RemoveSelected();
                    break;
                case Keys.Space:
                    _sheet.ToggleLine(line.ProductId);
                    RenderLines(line);
                    break;
                case Keys.Add or Keys.Oemplus:
                    _sheet.Step(line.ProductId, 1);
                    RenderSelected();
                    break;
                case Keys.Subtract or Keys.OemMinus:
                    _sheet.Step(line.ProductId, -1);
                    RenderSelected();
                    break;
                default:
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        // ---- Right: line editor -------------------------------------------------------------------------
        _lineTitle = new Label { Dock = DockStyle.Top, Height = 44, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Chọn một dòng để sửa" };
        _lineInfo = new Label { Dock = DockStyle.Top, Height = 60, ForeColor = SystemColors.GrayText };
        _qty = new ModernNumericBox { Dock = DockStyle.Top, DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, Enabled = false };
        _price = new ModernCurrencyBox { Dock = DockStyle.Top, Minimum = 0, Maximum = 1_000_000_000, Enabled = false };
        _qty.EnterPressed += (_, _) => ApplyLine(nextToPrice: true);
        _price.EnterPressed += (_, _) => ApplyLine(nextToPrice: false);
        _applyLine = new ModernButton { Text = "Ghi dòng (Enter)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Accept, Width = 260, Height = 38, Margin = new Padding(0, 0, 0, 6), Enabled = false };
        _applyLine.Click += (_, _) => ApplyLine(nextToPrice: false);
        _removeLine = new ModernButton { Text = "Bỏ dòng (Del)", Variant = ButtonVariant.Ghost, IconGlyph = Glyphs.Delete, Width = 260, Height = 38, Enabled = false };
        _removeLine.Click += (_, _) => RemoveSelected();
        var lineButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 96, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        lineButtons.Controls.AddRange([_applyLine, _removeLine]);

        var side = new Panel { Dock = DockStyle.Right, Width = 280, Padding = new Padding(16, 0, 0, 0) };
        side.Controls.Add(lineButtons);
        side.Controls.Add(_price);
        side.Controls.Add(Caption("Giá trả / đơn vị"));
        side.Controls.Add(_qty);
        side.Controls.Add(Caption("Số lượng trả"));
        side.Controls.Add(_lineInfo);
        side.Controls.Add(_lineTitle);

        // ---- Bottom: totals, settlement, reason, actions ------------------------------------------------
        _totals = new Label { Dock = DockStyle.Top, Height = 30, Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };

        _autoSettle = new CheckBox { Text = "Cấn trừ công nợ trước", AutoSize = true, Checked = true, Margin = new Padding(0, 12, 16, 0) };
        _autoSettle.CheckedChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _sheet?.SetAutoSettle(_autoSettle.Checked);
            }
        };
        _refund = new ModernCurrencyBox { Width = 150, Minimum = 0, Maximum = 10_000_000_000, ReserveMessageSpace = false };
        _refund.ValueChanged += (_, _) =>
        {
            if (!_rendering && _refund.ContainsFocus)
            {
                _sheet?.SetRefundAmount(_refund.ValueOrZero);
            }
        };
        _cash = new RadioButton { Text = "Tiền mặt", AutoSize = true, Checked = true, Margin = new Padding(12, 12, 0, 0) };
        _transfer = new RadioButton { Text = "Chuyển khoản", AutoSize = true, Margin = new Padding(8, 12, 16, 0) };
        _cash.CheckedChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _sheet?.SetRefundMethod(_cash.Checked ? PaymentMethod.Cash : PaymentMethod.BankTransfer);
            }
        };
        _debt = new Label { AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };
        var settleRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
        settleRow.Controls.AddRange([
            _autoSettle,
            new Label { Text = "NCC hoàn tiền", AutoSize = true, Margin = new Padding(0, 12, 6, 0) }, _refund,
            _cash, _transfer, _debt,
        ]);

        _reason = new ModernTextBox { Width = 300, PlaceholderText = "Lý do trả hàng (bắt buộc)", MaxLength = 200, ReserveMessageSpace = false };
        _reason.InnerTextBox.TextChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _sheet?.SetReason(_reason.Text);
            }
        };
        var reasonRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
        reasonRow.Controls.Add(_reason);
        foreach (var reason in SupplierReturnSheet.QuickReasons)
        {
            var chip = new ModernButton { Text = reason, Variant = ButtonVariant.Ghost, AutoSize = true, Height = 32, Margin = new Padding(6, 4, 0, 0) };
            chip.Click += (_, _) => _reason.Text = reason;
            reasonRow.Controls.Add(chip);
        }

        _note = new ModernTextBox { Dock = DockStyle.Top, PlaceholderText = "Ghi chú (người nhận hàng của NCC, biển số xe…)", MaxLength = 500, ReserveMessageSpace = false };
        _note.InnerTextBox.TextChanged += (_, _) =>
        {
            if (!_rendering)
            {
                _sheet?.SetNote(_note.Text);
            }
        };
        _status = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 6, 0, 0) };

        _submit = new ModernButton { Text = "Trả hàng (F9)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Return, Size = new Size(180, 40), Margin = new Padding(0, 0, 8, 0), Enabled = false };
        _submit.Click += async (_, _) => await SubmitAsync();
        var close = new ModernButton { Text = "Đóng", Variant = ButtonVariant.Ghost, Size = new Size(100, 40) };
        close.Click += (_, _) => Close();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([_submit, close]);
        var actionRow = new Panel { Dock = DockStyle.Top, Height = 44 };
        actionRow.Controls.Add(actions);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 260, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(actionRow);
        bottom.Controls.Add(_status);
        bottom.Controls.Add(_note);
        bottom.Controls.Add(reasonRow);
        bottom.Controls.Add(settleRow);
        bottom.Controls.Add(_totals);

        Controls.Add(_grid);
        Controls.Add(side);
        Controls.Add(bottom);
        Controls.Add(_message);
        Controls.Add(scanRow);
        Controls.Add(_modeInfo);
        Controls.Add(modeRow);
        Controls.Add(heading);

        Disposed += (_, _) => _boldCellFont?.Dispose();
        FormClosing += (_, e) =>
        {
            if (!_busy && DialogResult != DialogResult.OK && _sheet is { SelectedLineCount: > 0 }
                && MessageBox.Show(this, "Phiếu trả hàng chưa được lưu. Đóng và bỏ phiếu này?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        };
        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            _rendering = true;
            _supplier.SelectedIndex = -1;
            _rendering = false;
            RenderAll();
            if (!string.IsNullOrWhiteSpace(receiptCode))
            {
                _receiptCode.Text = receiptCode;
                await LoadReceiptAsync(receiptCode);
            }
            else if (supplierId is { } id && _suppliers.Any(s => s.Id == id))
            {
                _rendering = true;
                _supplier.SelectedValue = id;
                _rendering = false;
                StartFreeReturn(id);
            }
        };
        Shown += (_, _) =>
        {
            if (_sheet is null)
            {
                _receiptCode.Focus();
            }
            else
            {
                _scan.Focus();
            }
        };
    }

    /// <summary>The posted return (set after a successful F9).</summary>
    public SupplierReturnReceipt? Posted { get; private set; }

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
            case Keys.F7:
                ReturnAll();
                return true;
            case Keys.F9:
                _ = SubmitAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static Label Caption(string text) => new() { Dock = DockStyle.Top, Height = 24, Text = text, Padding = new Padding(0, 6, 0, 0) };

    private static ModernButton SmallButton(string text, string glyph, Action onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = ButtonVariant.Secondary, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private SupplierReturnSheetLine? Selected => _grid.GetSelected<SupplierReturnSheetLine>();

    private void Report(string message, bool error = false)
    {
        _message.Text = message;
        _message.ForeColor = error ? ThemeManager.Current.Colors.Danger : ThemeManager.Current.Colors.TextSecondary;
    }

    private bool ConfirmDiscard() =>
        _sheet is not { SelectedLineCount: > 0 }
        || MessageBox.Show(this, "Bỏ các sản phẩm đã chọn trên phiếu hiện tại?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void UseSheet(SupplierReturnSheet sheet)
    {
        var reason = _sheet?.Reason;
        var note = _sheet?.Note;
        _sheet = sheet;
        _sheet.SetReason(reason);
        _sheet.SetNote(note);
        _sheet.SetRefundMethod(_transfer.Checked ? PaymentMethod.BankTransfer : PaymentMethod.Cash);
        _sheet.Changed += (_, _) => RenderTotals();
        RenderAll();
    }

    private async Task LoadReceiptAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || _busy)
        {
            return;
        }

        if (!ConfirmDiscard())
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierReturnService>().GetReturnableAsync(code);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            System.Media.SystemSounds.Beep.Play();
            _receiptCode.SetError(result.Error.Message);
            return;
        }

        _receiptCode.ClearValidation();
        UseSheet(SupplierReturnSheet.FromReceipt(result.Value, _catalog));
        Report(result.Value.CanReturn
            ? "Chọn số lượng trả: quét mã, Space = trả hết dòng, F7 = trả hết theo phiếu. Giá trả mặc định là giá nhập thực tế của phiếu."
            : "Phiếu nhập này không còn hàng nào có thể trả.", error: !result.Value.CanReturn);
        _grid.Focus();
    }

    private void StartFreeReturn(int supplierId)
    {
        if (_sheet is { IsAgainstReceipt: false } current && current.SupplierId == supplierId)
        {
            return;
        }

        if (!ConfirmDiscard() || _suppliers.FirstOrDefault(s => s.Id == supplierId) is not { } supplier)
        {
            RenderHeader();
            return;
        }

        _receiptCode.Text = string.Empty;
        _receiptCode.ClearValidation();
        UseSheet(SupplierReturnSheet.ForSupplier(supplier.Id, supplier.Name, supplier.DebtBalance, _catalog));
        Report("Trả không theo phiếu nhập: quét mã sản phẩm cần trả. Giá trả mặc định là giá vốn hiện tại.");
        _scan.Focus();
    }

    private void ScanNow()
    {
        if (_sheet is null)
        {
            Report("Nhập số phiếu nhập hoặc chọn nhà cung cấp trước.", error: true);
            return;
        }

        var result = _sheet.Scan(_scan.Text);
        if (result.Outcome != SupplierReturnScanOutcome.Empty)
        {
            if (!result.IsSuccess)
            {
                System.Media.SystemSounds.Beep.Play();
            }

            Report(result.Message, error: !result.IsSuccess);
            RenderLines(result.Line);
        }

        _scan.Clear();
        _scan.Focus();
    }

    private void PickProduct()
    {
        if (_sheet is null)
        {
            Report("Nhập số phiếu nhập hoặc chọn nhà cung cấp trước.", error: true);
            return;
        }

        var choices = _sheet.IsAgainstReceipt
            ? _catalog.Where(p => _sheet.Find(p.ProductId) is not null).ToList()
            : _catalog.Where(p => p.StockQuantity > 0).ToList();
        using var dialog = new CatalogPickerDialog(choices, _scan.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedProduct is not { } product)
        {
            return;
        }

        var result = _sheet.Add(product.ProductId);
        Report(result.Message, error: !result.IsSuccess);
        _scan.Clear();
        RenderLines(result.Line);
        FocusLineEditor();
    }

    private void ReturnAll()
    {
        if (_sheet is not { IsAgainstReceipt: true })
        {
            return;
        }

        _sheet.ReturnAll();
        Report($"Đã chọn trả toàn bộ hàng còn trả được của phiếu {_sheet.Receipt!.Code}.");
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

    private void ApplyLine(bool nextToPrice)
    {
        if (_sheet is null || Selected is not { } line)
        {
            return;
        }

        _qty.Commit();
        _price.Commit();
        _qty.ClearValidation();
        _price.ClearValidation();
        var price = _sheet.SetUnitPrice(line.ProductId, _price.ValueOrZero);
        if (price.IsFailure)
        {
            _price.SetError(price.Error.Message);
            _price.Focus();
            return;
        }

        var wanted = _qty.ValueOrZero;
        _sheet.SetQuantity(line.ProductId, wanted);
        if (line.Quantity < wanted)
        {
            Report($"\"{line.ProductName}\" chỉ có thể trả tối đa {SupplierReturnSheet.FormatQty(line.MaxQuantity)} {line.Unit}.", error: true);
        }

        RenderLines(line);
        if (nextToPrice)
        {
            _price.Focus();
            _price.SelectAll();
        }
        else
        {
            _scan.Focus();
        }
    }

    private void RemoveSelected()
    {
        if (_sheet is not null && Selected is { } line && _sheet.Remove(line.ProductId))
        {
            Report(_sheet.IsAgainstReceipt ? $"Không trả {line.ProductName}." : $"Đã bỏ {line.ProductName} khỏi phiếu.");
            RenderLines();
        }
    }

    // ---- Rendering --------------------------------------------------------------------------------------

    private void RenderAll()
    {
        RenderHeader();
        RenderLines();
    }

    private void RenderHeader()
    {
        _rendering = true;
        try
        {
            if (_sheet is { IsAgainstReceipt: true } byReceipt)
            {
                _supplier.SelectedIndex = -1;
                var receipt = byReceipt.Receipt!;
                _modeInfo.Text = $"Theo phiếu nhập {receipt.Code} ngày {receipt.ReceiptDate.ToOffset(_storeOffset):dd/MM/yyyy} · {receipt.SupplierName} · công nợ hiện tại {Vnd.Format(byReceipt.SupplierDebt)}";
            }
            else if (_sheet is { } free)
            {
                _supplier.SelectedValue = free.SupplierId;
                _modeInfo.Text = $"Trả không theo phiếu · {free.SupplierName} · công nợ hiện tại {Vnd.Format(free.SupplierDebt)}";
            }
            else
            {
                _supplier.SelectedIndex = -1;
                _modeInfo.Text = "Trả theo phiếu nhập (giá trả tối đa bằng giá nhập thực tế) hoặc chọn nhà cung cấp để trả không theo phiếu.";
            }

            _all.Visible = _sheet?.IsAgainstReceipt == true;
            _scan.Enabled = _search.Enabled = _sheet is not null;
            _grid.Columns[nameof(SupplierReturnSheetLine.ReceivedQuantity)].Visible = _sheet?.IsAgainstReceipt != false;
            _grid.Columns[nameof(SupplierReturnSheetLine.ReturnedBefore)].Visible = _sheet?.IsAgainstReceipt != false;
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderLines(SupplierReturnSheetLine? select = null)
    {
        var keep = select ?? Selected;
        _grid.SetItems(_sheet?.Lines ?? []);
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
        var enabled = line is not null && !line.IsExhausted;
        _qty.Enabled = _price.Enabled = _applyLine.Enabled = enabled;
        _removeLine.Enabled = line is not null;
        _qty.ClearValidation();
        _price.ClearValidation();
        if (line is null)
        {
            _lineTitle.Text = "Chọn một dòng để sửa";
            _lineInfo.Text = string.Empty;
            _qty.Value = _price.Value = null;
            return;
        }

        _lineTitle.Text = $"{line.ProductCode} – {line.ProductName}";
        _lineInfo.Text =
            $"Trả được tối đa {SupplierReturnSheet.FormatQty(line.MaxQuantity)} {line.Unit} · tồn {SupplierReturnSheet.FormatQty(line.StockQuantity)}\n"
            + (line.MaxUnitPrice is { } max ? $"Giá nhập thực tế {Vnd.Format(max)} (giá trả tối đa)\n" : string.Empty)
            + $"Giá vốn hiện tại {Vnd.Format(line.CostPrice)}";
        _qty.SuffixText = line.Unit;
        _qty.Maximum = Math.Max(0, line.MaxQuantity);
        _qty.Value = line.Quantity;
        _price.Value = line.UnitPrice;
    }

    private void RenderTotals()
    {
        _rendering = true;
        try
        {
            var colors = ThemeManager.Current.Colors;
            if (_sheet is not { } sheet)
            {
                _totals.Text = string.Empty;
                _debt.Text = string.Empty;
                _status.Text = "Chưa chọn phiếu nhập hoặc nhà cung cấp.";
                _status.ForeColor = colors.TextMuted;
                _submit.Enabled = false;
                return;
            }

            var diff = sheet.CostDifference;
            _totals.Text = $"{sheet.SelectedLineCount} SP · SL {SupplierReturnSheet.FormatQty(sheet.TotalQuantity)} · giá trị hàng trả {Vnd.Format(sheet.Total)}"
                + (sheet.SelectedLineCount > 0 && diff != 0
                    ? $" · {(diff < 0 ? "lỗ" : "lãi")} {Vnd.Format(Math.Abs(diff))} so với giá vốn"
                    : string.Empty);
            _totals.ForeColor = diff < 0 ? colors.Warning : colors.TextPrimary;

            _autoSettle.Checked = sheet.AutoSettle;
            if (sheet.AutoSettle || !_refund.ContainsFocus)
            {
                _refund.Value = sheet.RefundAmount;
            }

            _cash.Checked = sheet.RefundMethod == PaymentMethod.Cash;
            _transfer.Checked = sheet.RefundMethod == PaymentMethod.BankTransfer;
            _cash.Enabled = _transfer.Enabled = sheet.RefundAmount > 0;
            _debt.Text = $"Cấn trừ nợ {Vnd.Format(sheet.DebtOffset)} · nợ còn {Vnd.Format(sheet.DebtAfter)}";
            _debt.ForeColor = sheet.DebtAfter < 0 ? colors.Danger : colors.TextSecondary;

            if (sheet.Blocker is { } blocker)
            {
                _status.Text = blocker;
                _status.ForeColor = sheet.SelectedLineCount == 0 || sheet.Reason is null ? colors.TextMuted : colors.Danger;
            }
            else
            {
                _status.Text = sheet.Summary + ".";
                _status.ForeColor = colors.Success;
            }

            _submit.Enabled = !_busy && sheet.CanSubmit;
            _grid.RefreshFields();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not SupplierReturnSheetLine line || e.CellStyle is not { } style)
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        if (line.IsExhausted)
        {
            style.ForeColor = colors.TextMuted;
            return;
        }

        switch (_grid.Columns[e.ColumnIndex].DataPropertyName)
        {
            case nameof(SupplierReturnSheetLine.Quantity) or nameof(SupplierReturnSheetLine.Amount) when line.IsSelected:
                style.ForeColor = colors.Primary;
                style.Font = _boldCellFont ??= new Font(_grid.InnerGrid.Font, FontStyle.Bold);
                break;
            case nameof(SupplierReturnSheetLine.UnitPrice) when line.IsSelected && line.UnitPrice < line.CostPrice:
                style.ForeColor = colors.Warning;
                break;
            case nameof(SupplierReturnSheetLine.LimitText):
                style.ForeColor = colors.Warning;
                break;
        }
    }

    // ---- Submit -----------------------------------------------------------------------------------------

    private async Task SubmitAsync()
    {
        if (_busy || _sheet is null)
        {
            return;
        }

        var request = _sheet.BuildRequest();
        if (request.IsFailure)
        {
            Report(request.Error.Message, error: true);
            return;
        }

        var confirm = $"{_sheet.Summary}?\n\nHàng sẽ được xuất khỏi kho ngay và phiếu trả hàng không sửa được sau khi lưu.";
        if (MessageBox.Show(this, confirm, "Trả hàng nhà cung cấp", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _busy = true;
        UseWaitCursor = true;
        _submit.Enabled = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ISupplierReturnService>().CreateAsync(request.Value);
            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                Report(result.Error.Message, error: true);
                return;
            }

            Posted = result.Value;
            var ret = result.Value.Return;
            MessageBox.Show(
                this,
                $"Đã lập phiếu trả hàng {ret.Code}.\n\n"
                + $"Giá trị hàng trả: {Vnd.Format(ret.Total)}\n"
                + (ret.RefundAmount > 0 ? $"NCC hoàn lại: {Vnd.Format(ret.RefundAmount)} ({ret.RefundMethodName})\n" : string.Empty)
                + (ret.DebtOffset > 0 ? $"Cấn trừ công nợ: {Vnd.Format(ret.DebtOffset)}\n" : string.Empty)
                + $"Công nợ còn lại với {ret.SupplierName}: {Vnd.Format(result.Value.SupplierDebtBalance)}",
                "Trả hàng nhà cung cấp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
        finally
        {
            if (!IsDisposed)
            {
                _busy = false;
                UseWaitCursor = false;
                _submit.Enabled = _sheet?.CanSubmit == true;
            }
        }
    }

    private sealed record SupplierOption(int Id, string Display);
}
