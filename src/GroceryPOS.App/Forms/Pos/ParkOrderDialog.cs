using GroceryPOS.Application.Formatting;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>Lưu tạm đơn (F10): optional name to recognise the customer when they come back.</summary>
internal sealed class ParkOrderDialog : Form
{
    private readonly ModernTextBox _label;

    public ParkOrderDialog(int lineCount, decimal total, string defaultLabel)
    {
        Text = "Lưu tạm đơn";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 214);

        var hint = new Label
        {
            Text = $"Đơn {lineCount} mặt hàng · {Vnd.Format(total)} sẽ được lưu tạm để bán cho khách tiếp theo. " +
                   "Gọi lại bằng F11.",
            AutoSize = false,
            Location = new Point(24, 16),
            Size = new Size(352, 44),
        };
        _label = new ModernTextBox
        {
            Location = new Point(24, 64),
            Width = 352,
            IconGlyph = Glyphs.Edit,
            PlaceholderText = $"Ghi chú để nhận ra khách (mặc định: {defaultLabel})",
            HelperText = "Ví dụ: Chị áo xanh, Anh Tư giao hàng…",
            MaxLength = Application.Sales.ParkedOrderRules.MaxLabelLength,
        };

        var ok = new ModernButton { Text = "Lưu tạm", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Save, Location = new Point(24, 156), Size = new Size(170, 40), DialogResult = DialogResult.OK };
        var cancel = new ModernButton { Text = "Hủy", Variant = ButtonVariant.Secondary, Location = new Point(206, 156), Size = new Size(170, 40), DialogResult = DialogResult.Cancel };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([hint, _label, ok, cancel]);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _label.Focus();
        };
    }

    public string? OrderLabel => string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim();
}
