using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Product picker for the goods receipt editor (F3): searches the catalog already loaded by the editor
/// (accent-insensitive on code, barcode, name and category), ↓ moves into the results, Enter / double-click picks.
/// Only active products are listed – the domain refuses to receive the others.
/// </summary>
internal sealed class CatalogPickerDialog : Form
{
    private readonly IReadOnlyList<StockItem> _catalog;
    private readonly ModernTextBox _term;
    private readonly ModernDataGrid _results;

    public CatalogPickerDialog(IReadOnlyList<StockItem> catalog, string initialTerm)
    {
        _catalog = catalog.Where(p => p.IsActive).ToList();
        Text = "Chọn sản phẩm nhập";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(900, 540);
        Padding = new Padding(16);

        _term = new ModernTextBox { Dock = DockStyle.Top, IconGlyph = Glyphs.Search, PlaceholderText = "Tên, mã, mã vạch hoặc nhóm sản phẩm…", Text = initialTerm };
        _term.InnerTextBox.TextChanged += (_, _) => Filter();

        _results = new ModernDataGrid { Dock = DockStyle.Fill, ShowSearch = false, ShowPager = false, PageSize = 200, EmptyText = "Không tìm thấy sản phẩm phù hợp." };
        _results.AddTextColumn(nameof(StockItem.ProductCode), "Mã", width: 100);
        _results.AddTextColumn(nameof(StockItem.ProductName), "Tên sản phẩm");
        _results.AddTextColumn(nameof(StockItem.CategoryName), "Nhóm", width: 130);
        _results.AddTextColumn(nameof(StockItem.Unit), "ĐVT", width: 60);
        _results.AddNumberColumn(nameof(StockItem.StockQuantity), "Tồn", width: 80, decimals: 3);
        _results.AddMoneyColumn(nameof(StockItem.CostPrice), "Giá vốn", width: 110);
        _results.AddMoneyColumn(nameof(StockItem.SellPrice), "Giá bán", width: 110);
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

        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            Filter();
            _term.Focus();
            _term.InnerTextBox.SelectionStart = _term.Text.Length;
        };
    }

    public StockItem? SelectedProduct { get; private set; }

    private void Filter()
    {
        var term = _term.Text;
        _results.SetItems(string.IsNullOrWhiteSpace(term)
            ? _catalog
            : _catalog.Where(p => SearchText.Matches(term, p.ProductCode, p.Barcode, p.ProductName, p.CategoryName)).ToList());
    }

    private void Pick()
    {
        if (_results.GetSelected<StockItem>() is { } product)
        {
            SelectedProduct = product;
            DialogResult = DialogResult.OK;
        }
    }
}
