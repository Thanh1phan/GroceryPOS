using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>Asks for the invoice number to reprint (pre-filled with the last invoice of this counter).</summary>
internal sealed class ReprintDialog : Form
{
    private readonly ModernTextBox _code;

    public ReprintDialog(string? lastCode)
    {
        Text = "In lại hóa đơn";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 200);

        var hint = new Label
        {
            Text = "Nhập hoặc quét số hóa đơn. Bản in lại được đánh dấu và ghi nhật ký.",
            AutoSize = false,
            Location = new Point(24, 16),
            Size = new Size(332, 40),
        };
        _code = new ModernTextBox
        {
            Location = new Point(24, 60),
            Width = 332,
            IconGlyph = Glyphs.Document,
            PlaceholderText = "Số hóa đơn, ví dụ HD261025-0001",
            Text = lastCode ?? string.Empty,
        };

        var ok = new ModernButton { Text = "Xem & in", Variant = ButtonVariant.Primary, Location = new Point(24, 140), Size = new Size(160, 40) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_code.Text))
            {
                _code.SetError("Vui lòng nhập số hóa đơn.");
                return;
            }

            DialogResult = DialogResult.OK;
        };
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Secondary,
            Location = new Point(196, 140),
            Size = new Size(160, 40),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([hint, _code, ok, cancel]);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _code.Focus();
            _code.SelectAll();
        };
    }

    public string Code => _code.Text.Trim();
}
