using System.ComponentModel;
using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Numeric input with vi-VN grouping as you type ("1.250.000"), range clamping, arrow-key stepping
/// and an optional unit suffix. Rules come from <see cref="NumericFormat"/> so they are unit-tested.
/// </summary>
/// <remarks>
/// Keys: ↑/↓ step by <see cref="Increment"/> (Shift ×10), the keypad decimal key types the decimal comma,
/// "k" multiplies by 1.000 when <see cref="AllowThousandsShortcut"/> is on. The value is clamped and
/// reformatted when focus leaves the box.
/// </remarks>
[DefaultEvent(nameof(ValueChanged))]
[DefaultProperty(nameof(Value))]
public class ModernNumericBox : ModernTextBox
{
    private NumericFormat _format = NumericFormat.Integer;
    private decimal? _value;
    private decimal _increment = 1;
    private bool _allowEmpty;
    private bool _syncing;
    private bool _lastKeyWasDecimal;

    public ModernNumericBox()
    {
        TextAlign = HorizontalAlignment.Right;
        Width = 160;
        SetValueCore(0, updateText: true, raise: false);
    }

    /// <summary>Raised whenever <see cref="Value"/> changes, while typing and on commit.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Current value; null only when <see cref="AllowEmpty"/> is on and the box is empty.</summary>
    [Category("Data")]
    [Bindable(true)]
    [DefaultValue(typeof(decimal), "0")]
    public decimal? Value
    {
        get => _value;
        set => SetValueCore(value is null ? null : NumericText.Clamp(value.Value, _format), updateText: true, raise: true);
    }

    /// <summary><see cref="Value"/> or 0 when empty – convenient for calculations.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal ValueOrZero => _value ?? 0;

    [Category("Data")]
    [DefaultValue(typeof(decimal), "0")]
    public decimal Minimum
    {
        get => _format.Minimum;
        set => UpdateFormat(_format with { Minimum = value, Maximum = Math.Max(value, _format.Maximum) });
    }

    [Category("Data")]
    [DefaultValue(typeof(decimal), "999999999999")]
    public decimal Maximum
    {
        get => _format.Maximum;
        set => UpdateFormat(_format with { Maximum = value, Minimum = Math.Min(value, _format.Minimum) });
    }

    [Category("Data")]
    [DefaultValue(0)]
    public int DecimalPlaces
    {
        get => _format.DecimalPlaces;
        set => UpdateFormat(_format with { DecimalPlaces = value });
    }

    /// <summary>Step for the arrow keys.</summary>
    [Category("Behavior")]
    [DefaultValue(typeof(decimal), "1")]
    public decimal Increment
    {
        get => _increment;
        set => _increment = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Bước tăng phải lớn hơn 0.");
    }

    /// <summary>Lets the user type "25k" for 25.000.</summary>
    [Category("Behavior")]
    [DefaultValue(false)]
    public bool AllowThousandsShortcut
    {
        get => _format.AllowThousandsShortcut;
        set => UpdateFormat(_format with { AllowThousandsShortcut = value });
    }

    /// <summary>When on, an empty box means "no value" (null) instead of the minimum.</summary>
    [Category("Behavior")]
    [DefaultValue(false)]
    public bool AllowEmpty
    {
        get => _allowEmpty;
        set
        {
            _allowEmpty = value;
            if (!value && _value is null)
            {
                Value = NumericText.Clamp(0, _format);
            }
        }
    }

    /// <summary>The rules in effect; assign to configure everything at once (e.g. <see cref="NumericFormat.Quantity"/>).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NumericFormat Format
    {
        get => _format;
        set => UpdateFormat(value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Validates the typed text without changing it; sets an error message when it is not a number in range.</summary>
    public bool ValidateValue()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            if (_allowEmpty)
            {
                ClearValidation();
                return true;
            }

            SetError("Vui lòng nhập giá trị.");
            return false;
        }

        if (!NumericText.TryParse(Text, _format, out var parsed))
        {
            SetError("Giá trị không hợp lệ.");
            return false;
        }

        if (!NumericText.IsInRange(parsed, _format))
        {
            SetError($"Giá trị phải từ {NumericText.Format(_format.Minimum, _format)} đến {NumericText.Format(_format.Maximum, _format)}.");
            return false;
        }

        ClearValidation();
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!ReadOnly && e.KeyCode is Keys.Up or Keys.Down && !e.Alt && !e.Control)
        {
            var increment = e.Shift ? _increment * 10 : _increment;
            Value = NumericText.Step(ParsedOrValue(), increment, e.KeyCode == Keys.Up ? +1 : -1, _format);
            InnerTextBox.SelectAll();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || ReadOnly)
        {
            return;
        }

        // The keypad decimal key sends the OS separator; in this box it always means the decimal comma.
        if (_lastKeyWasDecimal && _format.DecimalPlaces > 0 && e.KeyChar is '.' or ',')
        {
            e.KeyChar = NumericText.DecimalSeparator;
        }

        if (e.KeyChar is 'k' or 'K' && _format.AllowThousandsShortcut)
        {
            e.Handled = true;
            if (NumericText.TryParse(Text + "k", _format, out var thousands))
            {
                Value = NumericText.Clamp(thousands, _format);
                InnerTextBox.SelectionStart = InnerTextBox.TextLength;
            }

            return;
        }

        if (!NumericText.IsAllowedChar(e.KeyChar, _format))
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _lastKeyWasDecimal = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        _lastKeyWasDecimal = (keyData & Keys.KeyCode) == Keys.Decimal;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!_syncing && InnerTextBox is { } inner)
        {
            var (text, caret) = NumericText.Regroup(inner.Text, inner.SelectionStart, _format);
            if (text != inner.Text)
            {
                _syncing = true;
                try
                {
                    inner.Text = text;
                    inner.SelectionStart = caret;
                }
                finally
                {
                    _syncing = false;
                }
            }

            // Live value while typing (not clamped yet, so totals follow what the user sees).
            if (string.IsNullOrWhiteSpace(text))
            {
                SetValueCore(_allowEmpty ? null : NumericText.Clamp(0, _format), updateText: false, raise: true);
            }
            else if (NumericText.TryParse(text, _format, out var parsed))
            {
                SetValueCore(parsed, updateText: false, raise: true);
            }
        }

        base.OnTextChanged(e);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        if (!ReadOnly && IsHandleCreated)
        {
            // Select everything so a cashier can overwrite the amount straight away.
            BeginInvoke(InnerTextBox.SelectAll);
        }
    }

    protected override void OnLeave(EventArgs e)
    {
        Commit();
        base.OnLeave(e);
    }

    /// <summary>Clamps and reformats the typed value (called automatically when focus leaves).</summary>
    public void Commit()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            SetValueCore(_allowEmpty ? null : NumericText.Clamp(0, _format), updateText: true, raise: true);
            return;
        }

        var value = NumericText.TryParse(Text, _format, out var parsed) ? parsed : _value ?? 0;
        SetValueCore(NumericText.Clamp(value, _format), updateText: true, raise: true);
    }

    private decimal? ParsedOrValue() => NumericText.TryParse(Text, _format, out var parsed) ? parsed : _value;

    private void UpdateFormat(NumericFormat format)
    {
        _format = format;
        if (_value is not null)
        {
            SetValueCore(NumericText.Clamp(_value.Value, format), updateText: true, raise: true);
        }
    }

    private void SetValueCore(decimal? value, bool updateText, bool raise)
    {
        var changed = _value != value;
        _value = value;
        if (updateText)
        {
            var text = value is null ? string.Empty : NumericText.Format(value.Value, _format);
            if (text != Text)
            {
                _syncing = true;
                try
                {
                    Text = text;
                }
                finally
                {
                    _syncing = false;
                }
            }
        }

        if (changed && raise)
        {
            OnValueChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);
}
