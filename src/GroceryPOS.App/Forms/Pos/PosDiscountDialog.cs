using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>Discount prompt (line or invoice): percent or VND amount, with a live preview on the base amount.</summary>
internal sealed class PosDiscountDialog : Form
{
    private readonly decimal _baseAmount;
    private readonly RadioButton _percent;
    private readonly RadioButton _amount;
    private readonly ModernNumericBox _value;
    private readonly Label _preview;

    public PosDiscountDialog(string title, decimal baseAmount, DiscountInput? current)
    {
        _baseAmount = baseAmount;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 268);

        var baseLabel = new Label
        {
            Text = $"Áp dụng trên: {Vnd.Format(baseAmount)}",
            AutoSize = true,
            Location = new Point(24, 16),
        };

        _percent = new RadioButton { Text = "Theo %", AutoSize = true, Location = new Point(24, 48) };
        _amount = new RadioButton { Text = "Theo số tiền (₫)", AutoSize = true, Location = new Point(140, 48) };
        _value = new ModernNumericBox { Location = new Point(24, 80), Width = 332 };
        _preview = new Label { AutoSize = false, Location = new Point(24, 136), Size = new Size(332, 40) };

        var isAmount = current?.Kind == DiscountKind.Amount;
        _amount.Checked = isAmount;
        _percent.Checked = !isAmount;
        ApplyMode();
        _value.Value = current?.Value ?? 0;

        _percent.CheckedChanged += (_, _) => { ApplyMode(); UpdatePreview(); };
        _value.ValueChanged += (_, _) => UpdatePreview();

        var ok = new ModernButton { Text = "Áp dụng", Variant = ButtonVariant.Primary, Location = new Point(24, 196), Size = new Size(104, 40) };
        ok.Click += (_, _) =>
        {
            _value.Commit();
            DialogResult = DialogResult.OK;
        };
        var remove = new ModernButton { Text = "Bỏ giảm giá", Variant = ButtonVariant.Secondary, Location = new Point(138, 196), Size = new Size(110, 40) };
        remove.Click += (_, _) =>
        {
            _value.Value = 0;
            DialogResult = DialogResult.OK;
        };
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Ghost,
            Location = new Point(258, 196),
            Size = new Size(98, 40),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([baseLabel, _percent, _amount, _value, _preview, ok, remove, cancel]);
        UpdatePreview();
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            _value.Focus();
            _value.SelectAll();
        };
    }

    /// <summary>Chosen discount; null when removed or zero.</summary>
    public DiscountInput? Discount
    {
        get
        {
            var value = _value.ValueOrZero;
            if (value <= 0)
            {
                return null;
            }

            return _percent.Checked ? DiscountInput.Percent(value) : DiscountInput.Amount(value);
        }
    }

    private void ApplyMode()
    {
        if (_percent.Checked)
        {
            _value.DecimalPlaces = 2;
            _value.Maximum = 100;
            _value.AllowThousandsShortcut = false;
            _value.SuffixText = "%";
        }
        else
        {
            _value.DecimalPlaces = 0;
            _value.Maximum = Math.Max(0, _baseAmount);
            _value.AllowThousandsShortcut = true;
            _value.SuffixText = "₫";
        }
    }

    private void UpdatePreview()
    {
        var discount = Discount;
        if (discount is null)
        {
            _preview.Text = "Không giảm giá.";
            return;
        }

        var amount = discount.Kind == DiscountKind.Percent
            ? Math.Min(Vnd.Round(_baseAmount * discount.Value / 100m), _baseAmount)
            : Math.Min(discount.Value, _baseAmount);
        _preview.Text = $"Giảm {Vnd.Format(amount)} → còn {Vnd.Format(_baseAmount - amount)}";
    }
}
