using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Themed combo box with the same rounded box, leading icon, placeholder and validation line as <see cref="ModernTextBox"/>.
/// Items are owner-drawn with theme colors. In <see cref="ComboBoxStyle.DropDown"/> style with <see cref="FilterAsYouType"/>,
/// typing filters the list accent-insensitively ("duong" finds "Đường"), best matches first.
/// </summary>
/// <remarks>
/// Bind with <see cref="DataSource"/> (any <see cref="IEnumerable"/>), <see cref="DisplayMember"/> and <see cref="ValueMember"/>;
/// items are copied into the list, so filtering never touches the caller's collection.
/// </remarks>
[ToolboxItem(true)]
[DefaultEvent(nameof(SelectedIndexChanged))]
[DefaultProperty(nameof(DataSource))]
public class ModernComboBox : UserControl, IThemeable
{
    private const int CbSetCueBanner = 0x1703;

    private readonly ComboBox _inner;
    private List<object>? _unfiltered;
    private object? _dataSource;
    private string _valueMember = string.Empty;
    private string _placeholderText = string.Empty;
    private string? _iconGlyph;
    private ValidationState _validationState;
    private string? _validationMessage;
    private string? _helperText;
    private bool _reserveMessageSpace = true;
    private bool _filterAsYouType = true;
    private bool _limitToList = true;
    private int _cornerRadius = -1;
    private int _boxHeight = -1;
    private bool _filtering;
    private bool _chevronHot;

    public ModernComboBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        _inner = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat,
            IntegralHeight = false,
            MaxDropDownItems = 12,
            Font = ThemeFonts.Get(FontRole.Body),
        };
        _inner.DrawItem += OnDrawItem;
        _inner.SelectedIndexChanged += (_, e) =>
        {
            if (!_filtering)
            {
                OnSelectedIndexChanged(e);
            }
        };
        _inner.SelectionChangeCommitted += (_, e) =>
        {
            if (!_filtering)
            {
                SelectionChangeCommitted?.Invoke(this, e);
                if (_unfiltered is not null && IsHandleCreated)
                {
                    BeginInvoke(EndFilter);
                }
            }
        };
        _inner.TextUpdate += (_, _) => ApplyFilter();
        _inner.DropDown += (_, _) => _inner.DropDownWidth = Math.Max(Width, _inner.Width);
        _inner.KeyDown += (_, e) => OnKeyDown(e);
        _inner.KeyPress += (_, e) => OnKeyPress(e);
        _inner.KeyUp += (_, e) => OnKeyUp(e);
        _inner.GotFocus += (_, _) => Invalidate();
        _inner.LostFocus += (_, _) => Invalidate();
        _inner.HandleCreated += (_, _) => ApplyCueBanner();
        Controls.Add(_inner);

        Font = ThemeFonts.Get(FontRole.Body);
        Width = 240;
        Height = PreferredControlHeight;
        ApplyColors(ThemeManager.Current);
    }

    public event EventHandler? SelectedIndexChanged;

    /// <summary>Raised only when the user picks an item (not when code changes the selection).</summary>
    public event EventHandler? SelectionChangeCommitted;

    /// <summary>Raised when the user presses Enter.</summary>
    public event EventHandler? EnterPressed;

    /// <summary>Items to show; any <see cref="IEnumerable"/> (list of DTOs, enum values, strings). Copied into the list.</summary>
    [Category("Data")]
    [DefaultValue(null)]
    [AttributeProvider(typeof(IListSource))]
    public object? DataSource
    {
        get => _dataSource;
        set
        {
            _dataSource = value;
            var selectedValue = SelectedValue;
            _unfiltered = null;
            _filtering = true;
            try
            {
                _inner.BeginUpdate();
                _inner.Items.Clear();
                if (value is IEnumerable items and not string)
                {
                    _inner.Items.AddRange(items.Cast<object>().ToArray());
                }

                _inner.EndUpdate();
            }
            finally
            {
                _filtering = false;
            }

            // Keep the selection when the same value is still in the new list.
            SelectedValue = selectedValue;
            if (selectedValue is null)
            {
                _inner.SelectedIndex = -1;
                OnSelectedIndexChanged(EventArgs.Empty);
            }
        }
    }

    /// <summary>The items currently in the list (for simple static lists; prefer <see cref="DataSource"/>).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComboBox.ObjectCollection Items => _inner.Items;

    [Category("Data")]
    [DefaultValue("")]
    [AllowNull]
    public string DisplayMember
    {
        get => _inner.DisplayMember;
        set => _inner.DisplayMember = value ?? string.Empty;
    }

    [Category("Data")]
    [DefaultValue("")]
    [AllowNull]
    public string ValueMember
    {
        get => _valueMember;
        set => _valueMember = value ?? string.Empty;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => _inner.SelectedItem;
        set => _inner.SelectedItem = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _inner.SelectedIndex;
        set => _inner.SelectedIndex = value;
    }

    /// <summary>The <see cref="ValueMember"/> of the selected item (the item itself when no member is set).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedValue
    {
        get => ValueOf(_inner.SelectedItem);
        set
        {
            if (value is null)
            {
                _inner.SelectedIndex = -1;
                return;
            }

            foreach (var item in _inner.Items)
            {
                if (Equals(ValueOf(item), value))
                {
                    _inner.SelectedItem = item;
                    return;
                }
            }

            _inner.SelectedIndex = -1;
        }
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [AllowNull]
    public override string Text
    {
        get => _inner?.Text ?? string.Empty;
        set
        {
            if (_inner is not null)
            {
                _inner.Text = value ?? string.Empty;
            }
        }
    }

    /// <summary><see cref="ComboBoxStyle.DropDownList"/> (pick only) or <see cref="ComboBoxStyle.DropDown"/> (type to search).</summary>
    [Category("Behavior")]
    [DefaultValue(ComboBoxStyle.DropDownList)]
    public ComboBoxStyle DropDownStyle
    {
        get => _inner.DropDownStyle;
        set
        {
            if (value == ComboBoxStyle.Simple)
            {
                throw new ArgumentException("Kiểu Simple không được hỗ trợ.", nameof(value));
            }

            _inner.DropDownStyle = value;
            PerformLayout();
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue("")]
    [Description("Chữ gợi ý hiển thị khi chưa chọn.")]
    [AllowNull]
    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value ?? string.Empty;
            if (string.IsNullOrEmpty(AccessibleName))
            {
                _inner.AccessibleName = _placeholderText;
            }

            ApplyCueBanner();
            _inner.Invalidate();
        }
    }

    /// <summary>In DropDown style, typing filters the list (accent-insensitive).</summary>
    [Category("Behavior")]
    [DefaultValue(true)]
    public bool FilterAsYouType
    {
        get => _filterAsYouType;
        set => _filterAsYouType = value;
    }

    /// <summary>In DropDown style, text that matches no item is cleared when focus leaves. Turn off for free text (đơn vị tính).</summary>
    [Category("Behavior")]
    [DefaultValue(true)]
    public bool LimitToList
    {
        get => _limitToList;
        set => _limitToList = value;
    }

    [Category("Behavior")]
    [DefaultValue(12)]
    public int MaxDropDownItems
    {
        get => _inner.MaxDropDownItems;
        set => _inner.MaxDropDownItems = value;
    }

    [Category("Appearance")]
    [DefaultValue(null)]
    public string? IconGlyph
    {
        get => _iconGlyph;
        set
        {
            _iconGlyph = string.IsNullOrEmpty(value) ? null : value;
            PerformLayout();
            Invalidate();
        }
    }

    [Category("Validation")]
    [DefaultValue(ValidationState.None)]
    public ValidationState ValidationState
    {
        get => _validationState;
        set
        {
            _validationState = value;
            Invalidate();
        }
    }

    [Category("Validation")]
    [DefaultValue(null)]
    public string? ValidationMessage
    {
        get => _validationMessage;
        set
        {
            _validationMessage = value;
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue(null)]
    public string? HelperText
    {
        get => _helperText;
        set
        {
            _helperText = value;
            Invalidate();
        }
    }

    [Category("Layout")]
    [DefaultValue(true)]
    public bool ReserveMessageSpace
    {
        get => _reserveMessageSpace;
        set
        {
            _reserveMessageSpace = value;
            Height = PreferredControlHeight;
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue(-1)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(-1, value);
            Invalidate();
        }
    }

    [Category("Layout")]
    [DefaultValue(-1)]
    public int BoxHeight
    {
        get => _boxHeight;
        set
        {
            _boxHeight = Math.Max(-1, value);
            Height = PreferredControlHeight;
            PerformLayout();
        }
    }

    /// <summary>The hosted combo box, for advanced scenarios.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComboBox InnerComboBox => _inner;

    bool IThemeable.ThemesChildren => true;

    private int ScaledBoxHeight => InputChrome.BoxHeight(this, _boxHeight);

    private int PreferredControlHeight => ScaledBoxHeight + InputChrome.MessageLineHeight(this, _reserveMessageSpace);

    private Rectangle ChevronBounds => InputChrome.TrailingIconBounds(this, ScaledBoxHeight);

    public void SetError(string message)
    {
        _validationMessage = message;
        ValidationState = ValidationState.Error;
    }

    public void SetWarning(string message)
    {
        _validationMessage = message;
        ValidationState = ValidationState.Warning;
    }

    public void ClearValidation()
    {
        _validationMessage = null;
        ValidationState = ValidationState.None;
    }

    /// <summary>Selects the item whose display text equals <paramref name="text"/> (accent- and case-insensitive).</summary>
    public bool SelectByText(string? text)
    {
        var folded = TextMatch.Fold(text);
        foreach (var item in _inner.Items)
        {
            if (TextMatch.Fold(_inner.GetItemText(item)) == folded)
            {
                _inner.SelectedItem = item;
                return true;
            }
        }

        return false;
    }

    void IThemeable.ApplyTheme(Theme theme)
    {
        ApplyColors(theme);
        Invalidate();
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e) => SelectedIndexChanged?.Invoke(this, e);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && !e.Handled)
        {
            if (_unfiltered is not null)
            {
                EndFilter();
            }

            EnterPressed?.Invoke(this, EventArgs.Empty);
        }
        else if (e.KeyCode == Keys.Escape && _unfiltered is not null)
        {
            EndFilter();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _inner.Focus();
    }

    protected override void OnLeave(EventArgs e)
    {
        EndFilter();
        base.OnLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _inner.Focus();
        if (e.Button == MouseButtons.Left && Enabled)
        {
            // The chevron and the padding around the hosted combo open the list too.
            _inner.DroppedDown = !_inner.DroppedDown;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = ChevronBounds.Contains(e.Location);
        if (hot != _chevronHot)
        {
            _chevronHot = hot;
            Invalidate(ChevronBounds);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_chevronHot)
        {
            _chevronHot = false;
            Invalidate(ChevronBounds);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ApplyColors(ThemeManager.Current);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_inner is null)
        {
            return; // Called from the base constructor.
        }

        _inner.Font = Font;
        PerformLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = PreferredControlHeight;
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, PreferredControlHeight, specified);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_inner is null)
        {
            return; // Called from the base constructor.
        }

        var spacing = ThemeManager.Current.Spacing;
        var boxHeight = ScaledBoxHeight;
        var padding = this.ScaleDpi(spacing.Md);
        var left = padding + (_iconGlyph is null ? 0 : this.ScaleDpi(spacing.IconSize + spacing.Sm));
        var visibleWidth = Math.Max(0, ChevronBounds.Left - left);

        var textHeight = TextRenderer.MeasureText("Ág", Font).Height;
        _inner.ItemHeight = Math.Max(textHeight, Math.Min(textHeight + this.ScaleDpi(spacing.Sm), boxHeight - this.ScaleDpi(spacing.Md)));

        // The hosted combo is wider than what is shown: its own border and arrow button are clipped away,
        // and this control draws the rounded box and chevron instead.
        var arrowWidth = SystemInformation.VerticalScrollBarWidth + this.ScaleDpi(spacing.Xs);
        var inset = this.ScaleDpi(spacing.Xxs);
        _inner.SetBounds(left - inset, 0, visibleWidth + arrowWidth + inset, 0, BoundsSpecified.X | BoundsSpecified.Width);
        _inner.Top = Math.Max(0, (boxHeight - _inner.Height) / 2);
        var oldRegion = _inner.Region;
        _inner.Region = new Region(new Rectangle(inset, inset, visibleWidth, Math.Max(0, _inner.Height - (2 * inset))));
        oldRegion?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_inner is null)
        {
            return;
        }

        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var focused = _inner.Focused || _inner.ContainsFocus || _inner.DroppedDown;
        var colors = InputPalette.Resolve(theme, _validationState, focused, Enabled);
        g.Clear(BackColor);
        g.UseHighQuality();

        var boxHeight = ScaledBoxHeight;
        InputChrome.PaintBox(g, this, theme, colors, boxHeight, _cornerRadius, emphasized: focused || _validationState != ValidationState.None);

        if (_iconGlyph is not null)
        {
            InputChrome.PaintGlyph(g, _iconGlyph, Font.SizeInPoints, InputChrome.LeadingIconBounds(this, boxHeight), colors.Icon);
        }

        var chevron = ChevronBounds;
        if (_chevronHot && Enabled)
        {
            var hover = Rectangle.Inflate(chevron, -this.ScaleDpi(theme.Spacing.Xxs), -this.ScaleDpi(theme.Spacing.Xs));
            g.FillRoundedRectangle(theme.Colors.SurfaceAlt, hover, this.ScaleDpi(theme.Spacing.RadiusSm));
        }

        InputChrome.PaintGlyph(g, Glyphs.ChevronDown, Font.SizeInPoints * 0.8f, chevron, Enabled ? colors.Icon : theme.Colors.DisabledText);

        if (_reserveMessageSpace)
        {
            InputChrome.PaintMessage(g, this, theme, InputChrome.MessageFor(_validationState, _validationMessage, _helperText), boxHeight, colors.Message);
        }
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        ApplyColors(ThemeManager.Current);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        ApplyColors(ThemeManager.Current);
    }

    private object? ValueOf(object? item)
    {
        if (item is null || string.IsNullOrEmpty(_valueMember))
        {
            return item;
        }

        return TypeDescriptor.GetProperties(item).Find(_valueMember, ignoreCase: true)?.GetValue(item);
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        var theme = ThemeManager.Current;
        var c = theme.Colors;
        var isEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var highlighted = !isEdit && (e.State & DrawItemState.Selected) != 0;
        var inputColors = InputPalette.Resolve(theme, _validationState, focused: false, Enabled);

        var back = highlighted ? c.Primary : isEdit ? inputColors.Background : c.Surface;
        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        string text;
        Color fore;
        if (e.Index < 0)
        {
            if (!isEdit || string.IsNullOrEmpty(_placeholderText))
            {
                return;
            }

            text = _placeholderText;
            fore = Enabled ? c.TextMuted : c.DisabledText;
        }
        else
        {
            text = _inner.GetItemText(_inner.Items[e.Index]) ?? string.Empty;
            fore = highlighted ? c.OnPrimary : Enabled ? c.TextPrimary : c.DisabledText;
        }

        var indent = isEdit ? 0 : this.ScaleDpi(theme.Spacing.Sm);
        var bounds = new Rectangle(e.Bounds.X + indent, e.Bounds.Y, Math.Max(0, e.Bounds.Width - indent), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, _inner.Font, bounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    /// <summary>Narrows the list to items matching the typed text, keeping the text and caret as typed.</summary>
    private void ApplyFilter()
    {
        if (!_filterAsYouType || _inner.DropDownStyle != ComboBoxStyle.DropDown || _filtering)
        {
            return;
        }

        _unfiltered ??= _inner.Items.Cast<object>().ToList();
        var text = _inner.Text;
        var caret = _inner.SelectionStart;
        var matches = _unfiltered
            .Select(item => (Item: item, Text: _inner.GetItemText(item)))
            .Where(x => TextMatch.Matches(x.Text, text))
            .OrderBy(x => TextMatch.Rank(x.Text, text))
            .Select(x => x.Item)
            .ToArray();

        _filtering = true;
        try
        {
            _inner.BeginUpdate();
            _inner.Items.Clear();
            _inner.Items.AddRange(matches);
            _inner.EndUpdate();

            var show = matches.Length > 0 && text.Length > 0;
            if (_inner.DroppedDown != show)
            {
                _inner.DroppedDown = show;
            }

            // Dropping down selects the text and may auto-complete it; put back exactly what the user typed.
            _inner.Text = text;
            _inner.SelectionStart = Math.Min(caret, text.Length);
            _inner.SelectionLength = 0;
            Cursor.Current = Cursors.Default;
        }
        finally
        {
            _filtering = false;
        }
    }

    /// <summary>Restores the full list and resolves the typed text to an item (or clears it when <see cref="LimitToList"/>).</summary>
    private void EndFilter()
    {
        if (_unfiltered is null)
        {
            return;
        }

        var text = _inner.Text;
        var selected = _inner.SelectedItem;
        if (selected is null && text.Length > 0)
        {
            var folded = TextMatch.Fold(text);
            selected = _unfiltered.FirstOrDefault(item => TextMatch.Fold(_inner.GetItemText(item)) == folded)
                ?? (_inner.Items.Count == 1 ? _inner.Items[0] : null);
        }

        var all = _unfiltered;
        _unfiltered = null;
        var previousIndex = _inner.SelectedIndex;
        _filtering = true;
        try
        {
            _inner.BeginUpdate();
            _inner.Items.Clear();
            _inner.Items.AddRange(all.ToArray());
            _inner.EndUpdate();
            _inner.SelectedItem = selected;
            if (selected is null)
            {
                _inner.Text = _limitToList ? string.Empty : text;
            }
        }
        finally
        {
            _filtering = false;
        }

        if (_inner.SelectedIndex != previousIndex || selected is not null)
        {
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    private void ApplyCueBanner()
    {
        if (_inner.IsHandleCreated && OperatingSystem.IsWindows())
        {
            _ = NativeMethods.SendMessage(_inner.Handle, CbSetCueBanner, IntPtr.Zero, _placeholderText);
        }
    }

    private void ApplyColors(Theme theme)
    {
        if (_inner is null)
        {
            return;
        }

        var colors = InputPalette.Resolve(theme, _validationState, focused: false, Enabled);
        BackColor = InputChrome.OuterBackColor(this, theme);
        _inner.BackColor = colors.Background;
        _inner.ForeColor = colors.Foreground;
        _inner.Invalidate();
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ApplyColors(e.Current);
        PerformLayout();
        Invalidate();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
