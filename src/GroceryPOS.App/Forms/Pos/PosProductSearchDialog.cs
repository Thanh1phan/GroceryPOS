using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Quick product search for the counter (F3): type part of a name, code or barcode (accent-insensitive),
/// ↓ moves into the results, Enter / double-click picks.
/// </summary>
internal sealed class PosProductSearchDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernTextBox _term;
    private readonly ModernDataGrid _results;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };
    private int _searchVersion;

    public PosProductSearchDialog(IServiceScopeFactory scopeFactory, string initialTerm)
    {
        _scopeFactory = scopeFactory;
        Text = "Tìm sản phẩm";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(820, 520);
        Padding = new Padding(16);

        _term = new ModernTextBox
        {
            Dock = DockStyle.Top,
            IconGlyph = Glyphs.Search,
            PlaceholderText = "Tên, mã hoặc mã vạch sản phẩm…",
            Text = initialTerm,
        };
        _term.InnerTextBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        _results = new ModernDataGrid
        {
            Dock = DockStyle.Fill,
            ShowSearch = false,
            ShowPager = false,
            PageSize = 100,
            EmptyText = "Nhập từ khóa để tìm sản phẩm.",
        };
        _results.AddTextColumn(nameof(PosProductDto.Code), "Mã", width: 100);
        _results.AddTextColumn(nameof(PosProductDto.Name), "Tên sản phẩm");
        _results.AddTextColumn(nameof(PosProductDto.Unit), "ĐVT", width: 70);
        _results.AddMoneyColumn(nameof(PosProductDto.SellPrice), "Giá bán", width: 120);
        _results.AddNumberColumn(nameof(PosProductDto.StockQuantity), "Tồn", width: 80, decimals: 3);
        _results.RowActivated += (_, _) => Pick();
        _term.InnerTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down)
            {
                _results.InnerGrid.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                Pick();
                e.SuppressKeyPress = true;
            }
        };


        var pick = new ModernButton { Text = "Chọn (Enter)", Variant = ButtonVariant.Primary, Dock = DockStyle.Right, Width = 140 };
        pick.Click += (_, _) => Pick();
        var close = new ModernButton { Text = "Đóng (Esc)", Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 120, DialogResult = DialogResult.Cancel };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0) };
        footer.Controls.AddRange([pick, new Panel { Dock = DockStyle.Right, Width = 8 }, close]);

        CancelButton = close;
        Controls.Add(_results);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(_term);
        Controls.Add(footer);

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync();
        };

        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            _term.Focus();
            _term.InnerTextBox.SelectionStart = _term.Text.Length;
            await SearchAsync();
        };
    }

    public PosProductDto? SelectedProduct { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Pick()
    {
        if (_results.GetSelected<PosProductDto>() is { } product)
        {
            SelectedProduct = product;
            DialogResult = DialogResult.OK;
        }
    }

    private async Task SearchAsync()
    {
        var version = ++_searchVersion;
        var term = _term.Text;
        using var scope = _scopeFactory.CreateScope();
        var found = await scope.ServiceProvider.GetRequiredService<IPosLookupService>().SearchAsync(term, take: 50);
        if (IsDisposed || version != _searchVersion)
        {
            return;
        }

        _results.EmptyText = string.IsNullOrWhiteSpace(term) ? "Nhập từ khóa để tìm sản phẩm." : "Không tìm thấy sản phẩm phù hợp.";
        _results.SetItems(found);
    }
}
