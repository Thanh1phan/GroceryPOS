using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Phiếu kiểm kê: scan items on the shelf (each scan +1, <c>12*mã</c> +12) or pick a category / all products and
/// type the counted quantity line by line (Enter moves to the next uncounted product). Shows variance and its value
/// per line and in total; "Hoàn tất kiểm kê" (F9) applies the counted lines only. State lives in <see cref="StockCountSheet"/>.
/// </summary>
internal sealed class StockCountForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockCountSheet _sheet;
    private readonly ModernTextBox _scan;
    private readonly Label _scanMessage;
    private readonly ModernComboBox _category;
    private readonly ModernDataGrid _grid;
    private readonly Label _selectedName;
    private readonly ModernNumericBox _counted;
    private readonly Label _totals;
    private readonly Label _blocker;
    private readonly ModernTextBox _note;
    private readonly ModernButton _complete;
    private bool _completed;

    public StockCountForm(IServiceScopeFactory scopeFactory, IReadOnlyList<StockItem> catalog)
    {
        _scopeFactory = scopeFactory;
        _sheet = new StockCountSheet(catalog);
        _sheet.Changed += (_, _) => RenderTotals();

        Text = "Kiểm kê kho";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(1180, 720);
        Padding = new Padding(20);
        KeyPreview = true;

        // ---- Top: scan + add -----------------------------------------------------------------------
        _scan = new ModernTextBox { Width = 360, IconGlyph = Glyphs.Barcode, PlaceholderText = "Quét mã vạch / nhập mã SP (12*mã = 12 cái)", ReserveMessageSpace = false };
        _scan.EnterPressed += (_, _) => ScanNow();
        _category = new ModernComboBox
        {
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(CategoryOption.Name),
            ValueMember = nameof(CategoryOption.Id),
            DataSource = catalog
                .Where(p => p.IsActive)
                .GroupBy(p => (p.CategoryId, p.CategoryName))
                .Select(g => new CategoryOption(g.Key.CategoryId, $"{g.Key.CategoryName} ({g.Count()})"))
                .OrderBy(c => c.Name, StringComparer.Create(Vnd.Culture, ignoreCase: true))
                .ToList(),
            Margin = new Padding(16, 0, 0, 0),
        };
        var addCategory = Button("Thêm cả nhóm", ButtonVariant.Secondary, () =>
        {
            if (_category.SelectedValue is int id)
            {
                Report(_sheet.AddCategory(id) is var n && n > 0 ? $"Đã thêm {n} sản phẩm của nhóm." : "Các sản phẩm của nhóm đã có trong phiếu.");
                RenderLines();
            }
        });
        var addAll = Button("Thêm tất cả SP", ButtonVariant.Secondary, () =>
        {
            Report($"Đã thêm {_sheet.AddAllActive()} sản phẩm.");
            RenderLines();
        });
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false };
        top.Controls.AddRange([_scan, _category, addCategory, addAll]);
        _scanMessage = new Label { Dock = DockStyle.Top, Height = 26, ForeColor = SystemColors.GrayText, Text = "Tồn sổ sách lấy tại thời điểm mở phiếu. Sản phẩm chưa đếm sẽ giữ nguyên tồn." };

        // ---- Grid --------------------------------------------------------------------------------
        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Quét mã hoặc thêm sản phẩm để bắt đầu kiểm kê.", PageSize = 100 };
        _grid.AddTextColumn(nameof(StockCountSheetLine.ProductCode), "Mã SP", width: 100);
        _grid.AddTextColumn(nameof(StockCountSheetLine.ProductName), "Tên sản phẩm");
        _grid.AddTextColumn(nameof(StockCountSheetLine.CategoryName), "Nhóm", width: 130);
        _grid.AddTextColumn(nameof(StockCountSheetLine.Unit), "ĐVT", width: 60);
        _grid.AddNumberColumn(nameof(StockCountSheetLine.Recorded), "Tồn sổ sách", width: 100, decimals: 3);
        _grid.AddNumberColumn(nameof(StockCountSheetLine.Counted), "Thực tế", width: 90, decimals: 3);
        _grid.AddNumberColumn(nameof(StockCountSheetLine.Variance), "Chênh lệch", width: 95, decimals: 3);
        _grid.AddMoneyColumn(nameof(StockCountSheetLine.VarianceValue), "Giá trị lệch", width: 120);
        _grid.AddTextColumn(nameof(StockCountSheetLine.StatusName), "Trạng thái", width: 90);
        _grid.SelectionChanged += (_, _) => RenderSelected();
        _grid.RowActivated += (_, _) => FocusCounted();
        _grid.InnerGrid.CellFormatting += OnCellFormatting;

        // ---- Right: line editor ------------------------------------------------------------------
        _selectedName = new Label { Dock = DockStyle.Top, Height = 48, Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Chọn một dòng để nhập số lượng" };
        var countedCaption = new Label { Dock = DockStyle.Top, Height = 22, Text = "Số lượng thực tế (Enter = ghi & sang SP kế tiếp)" };
        _counted = new ModernNumericBox { Dock = DockStyle.Top, DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, AllowEmpty = true, Enabled = false };
        _counted.EnterPressed += (_, _) => ApplyCounted(moveNext: true);
        var apply = Button("Ghi số lượng", ButtonVariant.Primary, () => ApplyCounted(moveNext: false));
        var clear = Button("Xóa số đếm", ButtonVariant.Ghost, () => SetSelected(null));
        var remove = Button("Bỏ dòng này", ButtonVariant.Ghost, RemoveSelected);
        var matchRest = Button("SP chưa đếm = sổ sách", ButtonVariant.Secondary, () =>
        {
            if (Confirm($"Ghi nhận {_sheet.UncountedCount} sản phẩm chưa đếm có số thực tế bằng tồn sổ sách?"))
            {
                _sheet.MarkUncountedAsRecorded();
                RenderLines();
            }
        });
        var dropRest = Button("Bỏ các SP chưa đếm", ButtonVariant.Secondary, () =>
        {
            Report($"Đã bỏ {_sheet.RemoveUncounted()} sản phẩm chưa đếm khỏi phiếu.");
            RenderLines();
        });
        var lineButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 150, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        foreach (var b in new[] { apply, clear, remove, matchRest, dropRest })
        {
            b.Width = 260;
            b.Margin = new Padding(0, 0, 0, 6);
        }

        lineButtons.Controls.AddRange([apply, clear, remove]);
        var sheetButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 16, 0, 0) };
        sheetButtons.Controls.AddRange([matchRest, dropRest]);

        var side = new Panel { Dock = DockStyle.Right, Width = 280, Padding = new Padding(16, 0, 0, 0) };
        side.Controls.Add(sheetButtons);
        side.Controls.Add(lineButtons);
        side.Controls.Add(_counted);
        side.Controls.Add(countedCaption);
        side.Controls.Add(_selectedName);

        // ---- Bottom: totals, note, complete ------------------------------------------------------
        _totals = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };
        _blocker = new Label { Dock = DockStyle.Top, Height = 22 };
        _note = new ModernTextBox { Width = 560, PlaceholderText = "Ghi chú phiếu kiểm kê (ví dụ: kiểm kê cuối tháng 10, quầy đồ uống)", MaxLength = 500, ReserveMessageSpace = false };
        _note.InnerTextBox.TextChanged += (_, _) => _sheet.SetNote(_note.Text);
        _complete = new ModernButton { Text = "Hoàn tất kiểm kê (F9)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Accept, Size = new Size(210, 40), Margin = new Padding(16, 0, 0, 0) };
        _complete.Click += async (_, _) => await CompleteAsync();
        var close = new ModernButton { Text = "Đóng", Variant = ButtonVariant.Ghost, Size = new Size(100, 40), Margin = new Padding(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        actions.Controls.AddRange([_note, _complete, close]);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(0, 8, 0, 0) };
        bottom.Controls.Add(actions);
        bottom.Controls.Add(_blocker);
        bottom.Controls.Add(_totals);

        Controls.Add(_grid);
        Controls.Add(side);
        Controls.Add(bottom);
        Controls.Add(_scanMessage);
        Controls.Add(top);

        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F9)
            {
                e.Handled = true;
                await CompleteAsync();
            }
            else if (e.KeyCode == Keys.F2)
            {
                e.Handled = true;
                _scan.Focus();
            }
        };
        FormClosing += (_, e) =>
        {
            if (!_completed && _sheet.CountedCount > 0
                && !Confirm($"Phiếu kiểm kê có {_sheet.CountedCount} sản phẩm đã đếm nhưng chưa hoàn tất. Đóng và bỏ phiếu?"))
            {
                e.Cancel = true;
            }
        };
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            RenderLines();
        };
        Shown += (_, _) => _scan.Focus();
    }

    private static ModernButton Button(string text, ButtonVariant variant, Action onClick)
    {
        var button = new ModernButton { Text = text, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "Kiểm kê kho", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void Report(string message, bool error = false)
    {
        _scanMessage.Text = message;
        _scanMessage.ForeColor = error ? Color.Firebrick : ThemeManager.Current.Colors.TextSecondary;
    }

    private StockCountSheetLine? Selected => _grid.GetSelected<StockCountSheetLine>();

    private void ScanNow()
    {
        var result = _sheet.Scan(_scan.Text);
        switch (result.Outcome)
        {
            case StockCountScanOutcome.Empty:
                return;
            case StockCountScanOutcome.NotFound:
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

    private void FocusCounted()
    {
        if (Selected is not null)
        {
            _counted.Focus();
            _counted.SelectAll();
        }
    }

    private void ApplyCounted(bool moveNext)
    {
        if (Selected is not { } line)
        {
            return;
        }

        _counted.Commit();
        SetSelected(_counted.Value);
        if (!moveNext)
        {
            return;
        }

        var lines = _sheet.Lines;
        var start = lines.ToList().IndexOf(line);
        var next = lines.Skip(start + 1).FirstOrDefault(l => !l.IsCounted) ?? lines.FirstOrDefault(l => !l.IsCounted);
        if (next is not null)
        {
            _grid.SelectItem(next);
            FocusCounted();
        }
        else
        {
            Report("Đã đếm hết các sản phẩm trong phiếu. Nhấn F9 để hoàn tất.");
            _scan.Focus();
        }
    }

    private void SetSelected(decimal? counted)
    {
        if (Selected is not { } line)
        {
            return;
        }

        var result = _sheet.SetCounted(line.ProductId, counted);
        if (result.IsFailure)
        {
            _counted.SetError(result.Error.Message);
            return;
        }

        _counted.ClearValidation();
        RenderLines(line);
    }

    private void RemoveSelected()
    {
        if (Selected is { } line)
        {
            _sheet.Remove(line.ProductId);
            RenderLines();
        }
    }

    private void RenderLines(StockCountSheetLine? select = null)
    {
        var keep = select ?? Selected;
        _grid.SetItems(_sheet.Lines);
        if (keep is not null)
        {
            _grid.SelectItem(keep);
        }

        RenderSelected();
        RenderTotals();
    }

    private void RenderSelected()
    {
        if (Selected is not { } line)
        {
            _selectedName.Text = "Chọn một dòng để nhập số lượng";
            _counted.Value = null;
            _counted.Enabled = false;
            return;
        }

        _selectedName.Text = $"{line.ProductCode} – {line.ProductName}\nTồn sổ sách: {StockCountSheet.FormatQty(line.Recorded)} {line.Unit}";
        _counted.Enabled = true;
        _counted.SuffixText = line.Unit;
        _counted.Value = line.Counted;
        _counted.ClearValidation();
    }

    private void RenderTotals()
    {
        _totals.Text =
            $"{_sheet.Lines.Count:#,0} SP trong phiếu · đã đếm {_sheet.CountedCount:#,0} · lệch {_sheet.MismatchCount:#,0} · "
            + $"thiếu {Vnd.Format(-_sheet.ShortageValue)} · thừa {Vnd.Format(_sheet.SurplusValue)} · chênh lệch ròng {Vnd.Format(_sheet.NetVarianceValue)}";
        var colors = ThemeManager.Current.Colors;
        if (_sheet.Blocker is { } blocker)
        {
            _blocker.Text = blocker;
            _blocker.ForeColor = colors.TextMuted;
        }
        else
        {
            _blocker.Text = _sheet.Warning ?? "Sẵn sàng hoàn tất kiểm kê.";
            _blocker.ForeColor = _sheet.Warning is null ? colors.Success : colors.Warning;
        }

        _complete.Enabled = _sheet.CanComplete;
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not StockCountSheetLine line || e.CellStyle is not { } style)
        {
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (property is not (nameof(StockCountSheetLine.Variance) or nameof(StockCountSheetLine.VarianceValue) or nameof(StockCountSheetLine.StatusName)))
        {
            return;
        }

        var colors = ThemeManager.Current.Colors;
        style.ForeColor = line.Variance switch
        {
            null => colors.TextMuted,
            < 0 => colors.Danger,
            > 0 => colors.Warning,
            _ => colors.Success,
        };
    }

    private async Task CompleteAsync()
    {
        var request = _sheet.BuildRequest();
        if (request.IsFailure)
        {
            Report(request.Error.Message, error: true);
            return;
        }

        var message =
            $"Hoàn tất kiểm kê {_sheet.CountedCount} sản phẩm?\n\n"
            + $"Lệch {_sheet.MismatchCount} sản phẩm · thiếu {Vnd.Format(-_sheet.ShortageValue)} · thừa {Vnd.Format(_sheet.SurplusValue)}.\n"
            + "Tồn kho của các sản phẩm đã đếm sẽ được đặt bằng số thực tế."
            + (_sheet.Warning is { } warning ? $"\n{warning}" : string.Empty);
        if (!Confirm(message))
        {
            return;
        }

        _complete.Enabled = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IInventoryService>().CountAsync(request.Value);
            if (IsDisposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                Report(result.Error.Message, error: true);
                return;
            }

            var count = result.Value;
            _completed = true;
            MessageBox.Show(
                this,
                $"Đã lưu phiếu kiểm kê {count.Code}.\n\n"
                + $"{count.Lines.Count} sản phẩm, lệch {count.MismatchCount}.\n"
                + $"Thiếu: {Vnd.Format(-count.ShortageValue)}\nThừa: {Vnd.Format(count.SurplusValue)}\nChênh lệch ròng: {Vnd.Format(count.NetVarianceValue)}",
                "Kiểm kê kho",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
        finally
        {
            if (!IsDisposed && !_completed)
            {
                _complete.Enabled = _sheet.CanComplete;
            }
        }
    }

    private sealed record CategoryOption(int Id, string Name);
}
