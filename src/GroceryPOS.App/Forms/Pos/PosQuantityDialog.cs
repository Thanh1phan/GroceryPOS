using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>Small prompt for the quantity of a cart line (3 decimals for weighed goods).</summary>
internal sealed class PosQuantityDialog : Form
{
    private readonly ModernNumericBox _quantity;

    public PosQuantityDialog(string productName, string unit, decimal current)
    {
        Text = "Số lượng";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 196);

        var title = new Label { Text = productName, AutoSize = false, Location = new Point(24, 16), Size = new Size(312, 40) };
        _quantity = new ModernNumericBox
        {
            Location = new Point(24, 60),
            Width = 312,
            DecimalPlaces = 3,
            Minimum = 0.001m,
            Maximum = 100_000,
            SuffixText = unit,
            Value = current,
        };

        var ok = new ModernButton { Text = "Đồng ý", Variant = ButtonVariant.Primary, Location = new Point(24, 136), Size = new Size(150, 40) };
        ok.Click += (_, _) =>
        {
            _quantity.Commit();
            if (_quantity.ValidateValue() && _quantity.ValueOrZero > 0)
            {
                DialogResult = DialogResult.OK;
            }
        };
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Secondary,
            Location = new Point(186, 136),
            Size = new Size(150, 40),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([title, _quantity, ok, cancel]);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _quantity.Focus();
            _quantity.SelectAll();
        };
    }

    public decimal Quantity => _quantity.ValueOrZero;
}
