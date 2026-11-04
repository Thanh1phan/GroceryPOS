using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Kiểm tra giá (F12): scan or type a code to see price, unit, VAT and stock without touching the cart.
/// Stays open for several scans; "Thêm vào giỏ" (Ctrl+Enter) closes it and hands the product to the counter.
/// </summary>
internal sealed class PriceCheckDialog : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernTextBox _scan;
    private readonly ToneLabel _name;
    private readonly ToneLabel _codes;
    private readonly ToneLabel _price;
    private readonly ToneLabel _details;
    private readonly ModernBadge _stock;
    private readonly ModernButton _add;
    private PosProductDto? _product;

    public PriceCheckDialog(IServiceScopeFactory scopeFactory, bool canAdd)
    {
        _scopeFactory = scopeFactory;
        Text = "Kiểm tra giá";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 360);

        _scan = new ModernTextBox
        {
            Location = new Point(24, 20),
            Width = 432,
            IconGlyph = Glyphs.Barcode,
            PlaceholderText = "Quét mã vạch hoặc nhập mã sản phẩm rồi Enter",
            ReserveMessageSpace = false,
        };
        _scan.EnterPressed += async (_, _) => await CheckAsync();

        _name = new ToneLabel { Location = new Point(24, 80), Size = new Size(432, 56), Font = ThemeFonts.Get(FontRole.Title, FontStyle.Bold), Text = "Chưa có sản phẩm." , Tone = LabelTone.Muted };
        _codes = new ToneLabel { Location = new Point(24, 138), Size = new Size(432, 22), Tone = LabelTone.Muted };
        _price = new ToneLabel { Location = new Point(24, 164), Size = new Size(432, 64), Font = ThemeFonts.Get(FontRole.Display, FontStyle.Bold), Tone = LabelTone.Primary, TextAlign = ContentAlignment.MiddleLeft };
        _details = new ToneLabel { Location = new Point(24, 232), Size = new Size(300, 24), Tone = LabelTone.Muted };
        _stock = new ModernBadge { Location = new Point(24, 262), Visible = false };

        _add = new ModernButton
        {
            Text = "Thêm vào giỏ (Ctrl+Enter)",
            Variant = ButtonVariant.Primary,
            IconGlyph = Glyphs.Cart,
            Location = new Point(24, 300),
            Size = new Size(250, 40),
            Enabled = false,
            Visible = canAdd,
        };
        _add.Click += (_, _) => AddToCart();
        var close = new ModernButton { Text = "Đóng (Esc)", Variant = ButtonVariant.Secondary, Location = new Point(306, 300), Size = new Size(150, 40), DialogResult = DialogResult.Cancel };

        CancelButton = close;
        Controls.AddRange([_scan, _name, _codes, _price, _details, _stock, _add, close]);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _scan.Focus();
        };
    }

    /// <summary>Product to put in the cart when the dialog closes with OK.</summary>
    public PosProductDto? SelectedProduct { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            AddToCart();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task CheckAsync()
    {
        var text = _scan.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPosLookupService>().FindForScanAsync(PosCart.ParseScan(text).Text);
        if (IsDisposed)
        {
            return;
        }

        _scan.SelectAll();
        if (result.IsFailure)
        {
            Show(null, result.Error.Message);
            return;
        }

        Show(result.Value, null);
    }

    private void Show(PosProductDto? product, string? problem)
    {
        _product = product;
        _add.Enabled = product is not null;
        if (product is null)
        {
            _name.Text = problem ?? "Không tìm thấy sản phẩm.";
            _name.Tone = LabelTone.Danger;
            _codes.Text = string.Empty;
            _price.Text = string.Empty;
            _details.Text = string.Empty;
            _stock.Visible = false;
            return;
        }

        _name.Text = product.Name;
        _name.Tone = LabelTone.Normal;
        _codes.Text = product.Barcode is { Length: > 0 } barcode ? $"Mã {product.Code} · Mã vạch {barcode}" : $"Mã {product.Code}";
        _price.Text = Vnd.Format(product.SellPrice);
        _details.Text = $"/ {product.Unit} · VAT {product.VatRate:0.#}% (đã gồm trong giá)";

        var stock = CustomerDisplayState.FormatQuantity(product.StockQuantity);
        var (text, kind) = product.StockQuantity <= 0
            ? ($"Hết hàng (tồn {stock})", BadgeKind.Danger)
            : product.IsLowStock
                ? ($"Sắp hết · còn {stock} {product.Unit}", BadgeKind.Warning)
                : ($"Còn {stock} {product.Unit}", BadgeKind.Success);
        _stock.Set(text, kind);
        _stock.Visible = true;
    }

    private void AddToCart()
    {
        if (_product is not null && _add.Visible)
        {
            SelectedProduct = _product;
            DialogResult = DialogResult.OK;
        }
    }
}
