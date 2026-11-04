using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Đơn đang chờ (F11): parked carts, oldest first. Enter / double-click resumes, Delete discards after a confirmation.
/// Orders parked more than <see cref="ParkedOrderRules.StaleAfter"/> ago are shown in amber as probably forgotten.
/// </summary>
internal sealed class ParkedOrdersDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _storeOffset;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _resume;
    private readonly ModernButton _discard;

    public ParkedOrdersDialog(IServiceScopeFactory scopeFactory, TimeProvider clock, TimeSpan storeOffset, bool cartBusy)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _storeOffset = storeOffset;
        Text = "Đơn đang chờ";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(900, 480);
        Padding = new Padding(16);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = cartBusy
                ? "Đơn đang bán sẽ được lưu tạm trước khi gọi lại đơn đã chọn."
                : "Chọn đơn để tiếp tục bán. Giá được cập nhật theo bảng giá hiện tại.",
        };

        _grid = new ModernDataGrid
        {
            Dock = DockStyle.Fill,
            ShowSearch = false,
            ShowPager = false,
            PageSize = ParkedOrderRules.MaxParked,
            EmptyText = "Không có đơn nào đang chờ.",
        };
        _grid.AddTextColumn(nameof(Row.Label), "Đơn", width: 170);
        _grid.AddTextColumn(nameof(Row.Preview), "Sản phẩm");
        _grid.AddNumberColumn(nameof(Row.LineCount), "Mặt hàng", width: 80);
        _grid.AddMoneyColumn(nameof(Row.Total), "Tạm tính", width: 120);
        _grid.AddTextColumn(nameof(Row.Customer), "Khách hàng", width: 140);
        _grid.AddTextColumn(nameof(Row.ParkedBy), "Người lưu", width: 120);
        _grid.AddTextColumn(nameof(Row.Age), "Lưu lúc", width: 110);
        _grid.RowActivated += (_, _) => Resume();
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.InnerGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.InnerGrid.Rows[e.RowIndex].DataBoundItem is Row { IsStale: true })
            {
                e.CellStyle!.ForeColor = ThemeManager.Current.Colors.Warning;
            }
        };
        _grid.InnerGrid.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                await DiscardAsync();
            }
        };

        _resume = new ModernButton { Text = "Gọi lại (Enter)", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Cart, Dock = DockStyle.Right, Width = 160 };
        _resume.Click += (_, _) => Resume();
        _discard = new ModernButton { Text = "Xóa đơn (Del)", Variant = ButtonVariant.Danger, IconGlyph = Glyphs.Delete, Dock = DockStyle.Left, Width = 150 };
        _discard.Click += async (_, _) => await DiscardAsync();
        var close = new ModernButton { Text = "Đóng (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 120, DialogResult = DialogResult.Cancel };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0) };
        footer.Controls.AddRange([_resume, new Panel { Dock = DockStyle.Right, Width = 8 }, close, _discard]);

        CancelButton = close;
        Controls.Add(_grid);
        Controls.Add(hint);
        Controls.Add(footer);

        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            await LoadAsync();
            _grid.InnerGrid.Focus();
        };
    }

    /// <summary>The order the cashier chose to resume.</summary>
    public Guid? SelectedOrderId { get; private set; }

    /// <summary>Orders still parked when the dialog closed (for the badge on the POS button).</summary>
    public int RemainingCount { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && _grid.InnerGrid.Focused)
        {
            Resume();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var orders = await scope.ServiceProvider.GetRequiredService<IParkedOrderService>().ListAsync();
        if (IsDisposed)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        RemainingCount = orders.Count;
        _grid.SetItems(orders.Select(o => new Row(o, now, _storeOffset)).ToList());
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = _grid.GetSelected<Row>() is not null;
        _resume.Enabled = selected;
        _discard.Enabled = selected;
    }

    private void Resume()
    {
        if (_grid.GetSelected<Row>() is { } row)
        {
            SelectedOrderId = row.Id;
            DialogResult = DialogResult.OK;
        }
    }

    private async Task DiscardAsync()
    {
        if (_grid.GetSelected<Row>() is not { } row)
        {
            return;
        }

        if (!ModernDialog.Confirm(
                this,
                $"Xóa đơn lưu tạm \"{row.Label}\" ({row.LineCount} mặt hàng)? Khách sẽ phải quét lại từ đầu.",
                "Xóa đơn lưu tạm",
                "Xóa đơn",
                "Giữ lại",
                danger: true))
        {
            return;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IParkedOrderService>().DiscardAsync(row.Id);
            if (result.IsFailure)
            {
                ModernDialog.Warning(this, result.Error.Message, "Xóa đơn lưu tạm");
            }
        }

        await LoadAsync();
    }

    private sealed class Row
    {
        public Row(ParkedOrder order, DateTimeOffset now, TimeSpan storeOffset)
        {
            Id = order.Id;
            Label = order.Label;
            Preview = order.Preview;
            LineCount = order.LineCount;
            Total = order.Total;
            Customer = order.CustomerName ?? string.Empty;
            ParkedBy = order.ParkedByName;
            Age = ParkedOrderRules.Age(order.ParkedAt, now, storeOffset);
            IsStale = ParkedOrderRules.IsStale(order.ParkedAt, now);
        }

        public Guid Id { get; }
        public string Label { get; }
        public string Preview { get; }
        public int LineCount { get; }
        public decimal Total { get; }
        public string Customer { get; }
        public string ParkedBy { get; }
        public string Age { get; }
        public bool IsStale { get; }
    }
}
