using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Trả hàng: scan or type the invoice number from the customer's receipt, pick what comes back per line
/// (quantity, back on the shelf or written off as damaged), give a reason and refund in cash or by transfer,
/// then print the refund slip (PHIẾU TRẢ HÀNG). All state lives in the UI-free <see cref="ReturnSheet"/>.
/// Keys: F2 số hóa đơn · F4 số lượng · F6 hàng hỏng/nhập kho · F7 trả cả hóa đơn · +/− số lượng · Space trả hết dòng ·
/// F9 hoàn tiền · Esc làm lại.
/// </summary>
internal sealed class ReturnScreen : UserControl
{
    private const int SidePanelWidth = 380;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUser _currentUser;
    private readonly SalesOptions _options;
    private readonly StoreProfile _store;

    private readonly ModernTextBox _scan;
    private readonly ModernBadge _windowBadge;
    private readonly ToneLabel _invoiceInfo;
    private readonly ToneLabel _status;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton[] _lineButtons;
    private readonly ModernButton _restock;
    private readonly ModernButton _returnAll;

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

    private ReturnSheet? _sheet;
    private bool _rendering;
    private bool _busy;
    private bool? _hasOpenShift;

    public ReturnScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _currentUser = currentUser;
        Dock = DockStyle.Fill;

        using (var scope = scopeFactory.CreateScope())
        {
            _options = scope.ServiceProvider.GetService<SalesOptions>() ?? SalesOptions.Default;
            _store = scope.ServiceProvider.GetService<StoreProfile>() ?? StoreProfile.Default;
        }

        // ---- Left: invoice scan, header, lines -----------------------------------------------------
        _scan = new ModernTextBox
        {
            Dock = DockStyle.Top,
            IconGlyph = Glyphs.Barcode,
            TrailingGlyph = Glyphs.Search,
            PlaceholderText = "Quét mã vạch hoặc nhập số hóa đơn trên phiếu của khách (F2), VD: HD261027-0001",
            ReserveMessageSpace = false,
        };
        _scan.EnterPressed += async (_, _) => await LoadInvoiceAsync();
        _scan.TrailingIconClick += async (_, _) => await LoadInvoiceAsync();

        _windowBadge = new ModernBadge { Visible = false, Margin = new Padding(0, 8, 8, 0) };
        _invoiceInfo = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(0, 8, 0, 0), Text = "Chưa chọn hóa đơn." };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false };
        header.Controls.AddRange([_windowBadge, _invoiceInfo]);

        _status = new ToneLabel
        {
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = ThemeFonts.Get(FontRole.Subtitle),
            Tone = LabelTone.Muted,
            Text = $"Khách được đổi trả trong {_options.ReturnWindowDays} ngày kể từ ngày mua.",
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
        _grid.RowActivated += (_, _) => EditQuantity();
        _grid.SelectionChanged += (_, _) => UpdateLineButtons();

        var plus = ToolbarButton("+1", null, (_, _) => Step(1));
        var minus = ToolbarButton("−1", null, (_, _) => Step(-1));
        var all = ToolbarButton("Trả hết dòng (Space)", Glyphs.Accept, (_, _) => ToggleLine());
        var quantity = ToolbarButton("Số lượng (F4)", Glyphs.Edit, (_, _) => EditQuantity());
        _restock = ToolbarButton("Hàng hỏng (F6)", Glyphs.Warning, (_, _) => ToggleRestock());
        _lineButtons = [plus, minus, all, quantity, _restock];
        _returnAll = ToolbarButton("Trả cả hóa đơn (F7)", Glyphs.Return, (_, _) => Sheet(s => s.ReturnAll()));
        var clear = ToolbarButton("Bỏ chọn", Glyphs.Clear, (_, _) => Sheet(s => s.Clear()));
        clear.Variant = ButtonVariant.Ghost;
        _grid.ToolbarItems.Controls.AddRange([.. _lineButtons, _returnAll, clear]);

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
                    Sheet(s => s.SetRefundMethod(_cash.Checked ? PaymentMethod.Cash : PaymentMethod.BankTransfer));
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
                Sheet(s => s.SetReason(_reason.Text));
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
        _submit.Click += async (_, _) => await SubmitAsync();

        var reset = new ModernButton { Text = "Làm lại (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Fill };
        reset.Click += (_, _) => Reset(confirm: true);

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

        Load += async (_, _) =>
        {
            Render();
            _scan.Focus();
            await LoadShiftAsync();
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Keys that type into a text box (Space, +, −) only act when the focus is on the grid.
        var typing = _scan.ContainsFocus || _reason.ContainsFocus;
        switch (keyData)
        {
            case Keys.F2:
                _scan.Focus();
                _scan.SelectAll();
                return true;
            case Keys.F4:
                EditQuantity();
                return true;
            case Keys.F6:
                ToggleRestock();
                return true;
            case Keys.F7:
                Sheet(s => s.ReturnAll());
                return true;
            case Keys.F9:
                _ = SubmitAsync();
                return true;
            case Keys.Escape:
                Reset(confirm: true);
                return true;
            case Keys.Space when !typing:
                ToggleLine();
                return true;
            case Keys.Add or Keys.Oemplus when !typing:
                Step(1);
                return true;
            case Keys.Subtract or Keys.OemMinus when !typing:
                Step(-1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Loading -----------------------------------------------------------------------------------

    /// <summary>Whether the cashier has an open shift (null = unknown, e.g. no shift permission); cash refunds need one.</summary>
    private async Task LoadShiftAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().GetCurrentAsync();
        if (IsDisposed)
        {
            return;
        }

        _hasOpenShift = result.IsSuccess ? result.Value.Current is not null : null;
        Render();
    }

    private async Task LoadInvoiceAsync(string? code = null)
    {
        code ??= _scan.Text.Trim();
        if (code.Length == 0 || _busy)
        {
            return;
        }

        if (_sheet is { SelectedLineCount: > 0 } && !code.Equals(_sheet.Sale.Code, StringComparison.OrdinalIgnoreCase)
            && !Confirm($"Bỏ phiếu trả đang làm cho hóa đơn {_sheet.Sale.Code}?"))
        {
            return;
        }

        _busy = true;
        try
        {
            Result<ReturnableSaleDto> result;
            Result<IReadOnlyList<SaleReturnSummaryDto>>? history = null;
            using (var scope = _scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IReturnService>();
                result = await service.GetReturnableAsync(code);
                if (result.IsSuccess)
                {
                    history = await service.ListBySaleAsync(result.Value.SaleId);
                }
            }

            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                System.Media.SystemSounds.Beep.Play();
                ShowStatus(result.Error.Message, LabelTone.Danger);
                _scan.SelectAll();
                return;
            }

            SetSheet(new ReturnSheet(result.Value));
            _scan.Text = result.Value.Code;
            RenderHistory(history is { IsSuccess: true } ? history.Value : []);
            ShowStatus(DescribeInvoiceState(result.Value), result.Value.CanReturn ? LabelTone.Muted : LabelTone.Warning);
            _grid.InnerGrid.Focus();
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetSheet(ReturnSheet? sheet)
    {
        _sheet = sheet;
        if (sheet is not null)
        {
            sheet.Changed += (_, _) => Render();
        }

        _rendering = true;
        try
        {
            _reason.Text = string.Empty;
            _cash.Checked = true;
        }
        finally
        {
            _rendering = false;
        }

        _grid.SetItems(sheet?.Lines ?? (IReadOnlyList<ReturnSheetLine>)[]);
        if (sheet is null)
        {
            RenderHistory([]);
        }

        Render();
    }

    private string DescribeInvoiceState(ReturnableSaleDto sale)
    {
        if (sale.IsFullyReturned)
        {
            return "Hóa đơn này đã được trả hết hàng.";
        }

        if (!sale.IsWithinReturnWindow)
        {
            return sale.CanReturn
                ? $"Hóa đơn đã quá {_options.ReturnWindowDays} ngày – bạn đang trả hàng với quyền quản lý."
                : $"Hóa đơn đã quá {_options.ReturnWindowDays} ngày đổi trả. Vui lòng nhờ quản lý thực hiện.";
        }

        return "Chọn sản phẩm khách trả: Enter/F4 nhập số lượng, Space trả hết dòng, F6 đánh dấu hàng hỏng.";
    }

    private void RenderHistory(IReadOnlyList<SaleReturnSummaryDto> returns)
    {
        _previousReturns.Text = returns.Count == 0
            ? "Chưa có phiếu trả nào cho hóa đơn này."
            : string.Join(
                Environment.NewLine,
                returns.Select(r =>
                    $"{r.Code} · {StoreTime(r.ReturnedAt):dd/MM HH:mm} · {Vnd.Format(r.RefundTotal)} ({SaleLabels.Of(r.RefundMethod)}) – {r.Reason}"));
    }

    // ---- Line actions ------------------------------------------------------------------------------

    private ReturnSheetLine? SelectedLine => _grid.GetSelected<ReturnSheetLine>();

    private void Step(int delta)
    {
        if (SelectedLine is { IsExhausted: false } line)
        {
            Sheet(s => s.Step(line.SaleLineId, delta));
        }
    }

    private void ToggleLine()
    {
        if (SelectedLine is { IsExhausted: false } line)
        {
            Sheet(s => s.ToggleLine(line.SaleLineId));
        }
    }

    private void ToggleRestock()
    {
        if (SelectedLine is { IsExhausted: false } line)
        {
            Sheet(s =>
            {
                // Marking a line as damaged implies it is being returned.
                if (!line.IsSelected)
                {
                    s.ToggleLine(line.SaleLineId);
                }

                s.SetRestock(line.SaleLineId, !line.Restock);
            });
        }
    }

    private void EditQuantity()
    {
        if (SelectedLine is not { IsExhausted: false } line)
        {
            return;
        }

        var start = line.IsSelected ? line.Quantity : line.Line.RemainingQuantity;
        using var dialog = new PosQuantityDialog(
            $"{line.Line.ProductName} – còn trả được {line.Line.RemainingQuantity.ToString("#,0.###", Vnd.Culture)} {line.Line.Unit}",
            line.Line.Unit,
            start);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.Quantity > line.Line.RemainingQuantity)
            {
                ShowStatus($"Chỉ còn trả được tối đa {line.Line.RemainingQuantity.ToString("#,0.###", Vnd.Culture)} {line.Line.Unit}.", LabelTone.Warning);
            }

            Sheet(s => s.SetQuantity(line.SaleLineId, dialog.Quantity));
        }
    }

    private void Sheet(Action<ReturnSheet> action)
    {
        if (_sheet is { } sheet && !_busy)
        {
            action(sheet);
        }
    }

    private void UpdateLineButtons()
    {
        var line = SelectedLine;
        var editable = _sheet is { CanReturnAnything: true } && line is { IsExhausted: false };
        foreach (var button in _lineButtons)
        {
            button.Enabled = editable;
        }

        _restock.Text = line is { IsSelected: true, Restock: false } ? "Nhập lại kho (F6)" : "Hàng hỏng (F6)";
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

    // ---- Submit ------------------------------------------------------------------------------------

    private string? ScreenBlocker =>
        _sheet is { RefundMethod: PaymentMethod.Cash } && _options.RequireOpenShift && _hasOpenShift == false
            ? "Bạn chưa mở ca nên không thể hoàn tiền mặt. Mở ca ở màn hình Ca làm việc hoặc hoàn bằng chuyển khoản."
            : null;

    private async Task SubmitAsync()
    {
        if (_sheet is not { } sheet || _busy)
        {
            return;
        }

        var request = sheet.BuildRequest();
        if (request.IsFailure || ScreenBlocker is not null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        if (!Confirm($"{sheet.Summary} – hóa đơn {sheet.Sale.Code}?"))
        {
            return;
        }

        _busy = true;
        _submit.Enabled = false;
        Result<ReturnReceipt> result;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            result = await scope.ServiceProvider.GetRequiredService<IReturnService>().CreateAsync(request.Value);
        }
        finally
        {
            _busy = false;
        }

        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowStatus(result.Error.Message, LabelTone.Danger);
            Render();
            return;
        }

        var slip = result.Value;
        SetSheet(null);
        _scan.Clear();
        ShowStatus(Describe(slip), LabelTone.Success);
        ShowSlip(slip);
        _scan.Focus();
    }

    private void ShowSlip(ReturnReceipt slip)
    {
        var cashier = _currentUser.Session?.FullName;
        using var preview = new ReceiptPreviewForm(
            columns => ReturnSlipFormatter.Format(slip, _store, cashier, columns, _options.StoreUtcOffset),
            $"Phiếu trả hàng {slip.Code}",
            $"Phiếu trả hàng {slip.Code}",
            _store.ReceiptPaperWidthMm);
        preview.ShowDialog(this);
    }

    private static string Describe(ReturnReceipt slip)
    {
        var text = $"✔ {slip.Code}: hoàn {Vnd.Format(slip.RefundTotal)} ({SaleLabels.Of(slip.RefundMethod)}) cho HĐ {slip.SaleCode}";
        if (slip.PointsReversed > 0)
        {
            text += $" · trừ {slip.PointsReversed.ToString("#,0", Vnd.Culture)} điểm";
        }

        if (slip.WrittenOffCost > 0)
        {
            text += $" · hủy hàng giá vốn {Vnd.Format(slip.WrittenOffCost)}";
        }

        return text;
    }

    private void Reset(bool confirm)
    {
        if (_sheet is null)
        {
            _scan.Clear();
            _scan.Focus();
            return;
        }

        if (confirm && _sheet.SelectedLineCount > 0 && !Confirm("Bỏ phiếu trả hàng đang làm?"))
        {
            return;
        }

        SetSheet(null);
        _scan.Clear();
        ShowStatus($"Khách được đổi trả trong {_options.ReturnWindowDays} ngày kể từ ngày mua.", LabelTone.Muted);
        _scan.Focus();
    }

    // ---- Rendering ---------------------------------------------------------------------------------

    private void Render()
    {
        _rendering = true;
        try
        {
            var sheet = _sheet;
            if (sheet is null)
            {
                _windowBadge.Visible = false;
                _invoiceInfo.Text = "Chưa chọn hóa đơn.";
                _lineCount.Text = "0";
                _writtenOff.Text = "0 ₫";
                _refund.Text = "0 ₫";
                _points.Text = string.Empty;
                _blocker.Text = "Quét hoặc nhập số hóa đơn của khách.";
                _submit.Enabled = false;
                _returnAll.Enabled = false;
                UpdateLineButtons();
                return;
            }

            var sale = sheet.Sale;
            _windowBadge.Visible = true;
            if (sale.IsFullyReturned)
            {
                _windowBadge.Set("Đã trả hết", BadgeKind.Neutral);
            }
            else if (sale.IsWithinReturnWindow)
            {
                _windowBadge.Set("Trong hạn đổi trả", BadgeKind.Success);
            }
            else
            {
                _windowBadge.Set("Quá hạn đổi trả", sale.CanReturn ? BadgeKind.Warning : BadgeKind.Danger);
            }

            var parts = new List<string>
            {
                sale.Code,
                StoreTime(sale.SaleDate).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                sale.CustomerName ?? "Khách lẻ",
                $"Tổng {Vnd.Format(sale.Total)}",
            };
            if (sale.RefundedTotal > 0)
            {
                parts.Add($"đã hoàn {Vnd.Format(sale.RefundedTotal)}");
            }

            _invoiceInfo.Text = string.Join(" · ", parts);

            _lineCount.Text = $"{sheet.SelectedLineCount} dòng · SL {sheet.SelectedQuantity.ToString("#,0.###", Vnd.Culture)}";
            _writtenOff.Text = Vnd.Format(sheet.WrittenOffRefund);
            _refund.Text = Vnd.Format(sheet.RefundTotal);
            _points.Text = DescribePoints(sheet);

            _cash.Checked = sheet.RefundMethod == PaymentMethod.Cash;
            _transfer.Checked = sheet.RefundMethod == PaymentMethod.BankTransfer;

            var blocker = sheet.Blocker ?? ScreenBlocker;
            _blocker.Text = blocker ?? sheet.Summary + ".";
            _blocker.Tone = blocker is null ? LabelTone.Muted : LabelTone.Warning;
            _submit.Enabled = blocker is null && !_busy;
            _returnAll.Enabled = sheet.CanReturnAnything;

            _grid.Reload();
            UpdateLineButtons();
        }
        finally
        {
            _rendering = false;
        }
    }

    private static string DescribePoints(ReturnSheet sheet)
    {
        if (sheet.Sale.CustomerId is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (sheet.PointsToReverse > 0)
        {
            parts.Add($"Trừ {sheet.PointsToReverse.ToString("#,0", Vnd.Culture)} điểm đã tích");
        }

        if (sheet.PointsToRestore > 0)
        {
            parts.Add($"hoàn lại {sheet.PointsToRestore.ToString("#,0", Vnd.Culture)} điểm đã dùng");
        }

        return parts.Count == 0 ? "Không thay đổi điểm tích lũy." : string.Join(", ", parts) + ".";
    }

    private DateTime StoreTime(DateTimeOffset value) => value.ToOffset(_options.StoreUtcOffset).DateTime;

    private void ShowStatus(string text, LabelTone tone)
    {
        _status.Text = text;
        _status.Tone = tone;
    }

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "Trả hàng", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

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
