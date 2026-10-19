using System.ComponentModel;
using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// VND amount input: whole đồng, "₫" suffix, "25k" shortcut and ↑/↓ in steps of 1.000 đ.
/// Used for prices, cash received, discounts and cash counts.
/// </summary>
public class ModernCurrencyBox : ModernNumericBox
{
    public ModernCurrencyBox()
    {
        Format = NumericFormat.Currency;
        Increment = 1000;
        SuffixText = "₫";
    }

    [DefaultValue(true)]
    public new bool AllowThousandsShortcut
    {
        get => base.AllowThousandsShortcut;
        set => base.AllowThousandsShortcut = value;
    }

    [DefaultValue(typeof(decimal), "1000")]
    public new decimal Increment
    {
        get => base.Increment;
        set => base.Increment = value;
    }

    [DefaultValue("₫")]
    public new string? SuffixText
    {
        get => base.SuffixText;
        set => base.SuffixText = value;
    }

    /// <summary>VND has no minor unit in practice, so decimals are always 0.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new int DecimalPlaces => 0;
}
