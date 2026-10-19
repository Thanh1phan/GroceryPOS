using System.ComponentModel;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Date input in dd/MM/yyyy with a calendar drop-down. Typing is quick: "19/10", "191026", "hôm nay", "-7"
/// (see <see cref="DateText.TryParse"/>). ↑/↓ move one day, PageUp/PageDown one month, F4 or Alt+↓ opens the calendar.
/// </summary>
[DefaultEvent(nameof(ValueChanged))]
[DefaultProperty(nameof(Value))]
public class ModernDatePicker : ModernTextBox
{
    private DateTime? _value;
    private DateTime _minDate = new(2000, 1, 1);
    private DateTime _maxDate = new(2099, 12, 31);
    private bool _allowEmpty;
    private bool _ownError;
    private ToolStripDropDown? _dropDown;
    private MonthCalendar? _calendar;

    public ModernDatePicker()
    {
        TrailingGlyph = Glyphs.Calendar;
        PlaceholderText = "dd/mm/yyyy";
        MaxLength = 20;
        Width = 160;
        SetValueCore(DateTime.Today, raise: false);
    }

    public event EventHandler? ValueChanged;

    /// <summary>Selected date (time part is always 00:00); null only when <see cref="AllowEmpty"/> is on.</summary>
    [Category("Data")]
    [Bindable(true)]
    public DateTime? Value
    {
        get => _value;
        set
        {
            if (value is null && !_allowEmpty)
            {
                throw new ArgumentNullException(nameof(value), "Ngày không được để trống.");
            }

            SetValueCore(value is null ? null : ClampDate(value.Value.Date), raise: true);
        }
    }

    /// <summary>The value as a store day.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateOnly? Date
    {
        get => _value is null ? null : DateOnly.FromDateTime(_value.Value);
        set => Value = value?.ToDateTime(TimeOnly.MinValue);
    }

    [Category("Data")]
    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            _minDate = value.Date;
            if (_maxDate < _minDate)
            {
                _maxDate = _minDate;
            }

            if (_value is not null)
            {
                Value = _value;
            }
        }
    }

    [Category("Data")]
    public DateTime MaxDate
    {
        get => _maxDate;
        set
        {
            _maxDate = value.Date;
            if (_minDate > _maxDate)
            {
                _minDate = _maxDate;
            }

            if (_value is not null)
            {
                Value = _value;
            }
        }
    }

    /// <summary>When on, the box may be left empty (no date filter).</summary>
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
                Value = ClampDate(DateTime.Today);
            }
        }
    }

    private bool ShouldSerializeMinDate() => _minDate != new DateTime(2000, 1, 1);

    private bool ShouldSerializeMaxDate() => _maxDate != new DateTime(2099, 12, 31);

    private bool ShouldSerializeValue() => false;

    /// <summary>Shows the calendar under the box.</summary>
    public void ShowCalendar()
    {
        if (ReadOnly || !Enabled)
        {
            return;
        }

        Commit();
        EnsureDropDown();
        var calendar = _calendar!;
        calendar.MinDate = _minDate;
        calendar.MaxDate = _maxDate;
        var selected = _value ?? ClampDate(DateTime.Today);
        calendar.SetDate(selected);
        _dropDown!.Show(this, new Point(0, ScaledBoxHeight));
        calendar.Focus();
    }

    /// <summary>Parses the typed text; invalid or out-of-range input shows an error and keeps the last valid date.</summary>
    public bool Commit()
    {
        var text = Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (_allowEmpty)
            {
                ClearOwnError();
                SetValueCore(null, raise: true);
                return true;
            }

            ShowOwnError("Vui lòng nhập ngày.");
            return false;
        }

        if (!DateText.TryParse(text, DateOnly.FromDateTime(DateTime.Today), out var parsed))
        {
            ShowOwnError("Ngày không hợp lệ (dd/mm/yyyy).");
            return false;
        }

        var date = parsed.ToDateTime(TimeOnly.MinValue);
        if (date < _minDate || date > _maxDate)
        {
            ShowOwnError($"Ngày phải từ {_minDate:dd/MM/yyyy} đến {_maxDate:dd/MM/yyyy}.");
            return false;
        }

        ClearOwnError();
        SetValueCore(date, raise: true);
        return true;
    }

    protected override void OnTrailingIconClick(EventArgs e)
    {
        base.OnTrailingIconClick(e);
        ShowCalendar();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!ReadOnly && !e.Control)
        {
            if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
            {
                ShowCalendar();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (!e.Alt && e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
            {
                if (Commit() || _value is not null)
                {
                    var current = _value ?? ClampDate(DateTime.Today);
                    var moved = e.KeyCode switch
                    {
                        Keys.Up => current.AddDays(1),
                        Keys.Down => current.AddDays(-1),
                        Keys.PageUp => current.AddMonths(1),
                        _ => current.AddMonths(-1),
                    };
                    ClearOwnError();
                    SetValueCore(ClampDate(moved), raise: true);
                    SelectAll();
                }

                e.Handled = e.SuppressKeyPress = true;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        if (!ReadOnly && IsHandleCreated)
        {
            BeginInvoke(SelectAll);
        }
    }

    protected override void OnLeave(EventArgs e)
    {
        if (_dropDown is not { Visible: true })
        {
            Commit();
        }

        base.OnLeave(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dropDown?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    private DateTime ClampDate(DateTime date) => date < _minDate ? _minDate : date > _maxDate ? _maxDate : date;

    private void SetValueCore(DateTime? value, bool raise)
    {
        var changed = _value != value;
        _value = value;
        var text = value is null ? string.Empty : DateText.Format(DateOnly.FromDateTime(value.Value));
        if (Text != text)
        {
            Text = text;
        }

        if (changed && raise)
        {
            OnValueChanged(EventArgs.Empty);
        }
    }

    private void ShowOwnError(string message)
    {
        _ownError = true;
        SetError(message);
    }

    /// <summary>Clears only errors this control set, so a form-level validation message is not wiped.</summary>
    private void ClearOwnError()
    {
        if (_ownError)
        {
            _ownError = false;
            ClearValidation();
        }
    }

    private void EnsureDropDown()
    {
        if (_dropDown is not null)
        {
            return;
        }

        _calendar = new MonthCalendar
        {
            MaxSelectionCount = 1,
            ShowToday = true,
            ShowTodayCircle = true,
            Margin = Padding.Empty,
        };
        _calendar.DateSelected += (_, e) =>
        {
            ClearOwnError();
            SetValueCore(ClampDate(e.Start.Date), raise: true);
            _dropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
            InnerTextBox.Focus();
            SelectAll();
        };
        _calendar.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ClearOwnError();
                SetValueCore(ClampDate(_calendar.SelectionStart.Date), raise: true);
                _dropDown?.Close(ToolStripDropDownCloseReason.Keyboard);
                InnerTextBox.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _dropDown?.Close(ToolStripDropDownCloseReason.Keyboard);
                InnerTextBox.Focus();
                e.Handled = true;
            }
        };

        var host = new ToolStripControlHost(_calendar)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = true,
        };
        _dropDown = new ToolStripDropDown
        {
            Padding = Padding.Empty,
            AutoClose = true,
            DropShadowEnabled = true,
        };
        _dropDown.Items.Add(host);
    }
}
