using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Single-line text box with a rounded border, optional leading icon, placeholder and validation state.
/// The validation (or helper) message is drawn under the box when <see cref="ReserveMessageSpace"/> is on,
/// so showing an error never shifts the layout.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(TextChanged))]
[DefaultProperty(nameof(Text))]
public class ModernTextBox : UserControl, IThemeable
{
    private readonly TextBox _inner;
    private string? _iconGlyph;
    private ValidationState _validationState;
    private string? _validationMessage;
    private string? _helperText;
    private bool _reserveMessageSpace = true;
    private int _cornerRadius = -1;
    private int _boxHeight = -1;
    private string? _suffixText;
    private string? _trailingGlyph;
    private bool _trailingHot;

    public ModernTextBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = ThemeFonts.Get(FontRole.Body),
        };
        _inner.TextChanged += (_, e) => OnTextChanged(e);
        _inner.KeyDown += (_, e) => OnKeyDown(e);
        _inner.KeyPress += (_, e) => OnKeyPress(e);
        _inner.KeyUp += (_, e) => OnKeyUp(e);
        _inner.GotFocus += (_, _) => Invalidate();
        _inner.LostFocus += (_, _) => Invalidate();
        Controls.Add(_inner);

        Font = ThemeFonts.Get(FontRole.Body);
        Width = 240;
        Height = PreferredControlHeight;
        Cursor = Cursors.IBeam;
        ApplyColors(ThemeManager.Current);
    }

    /// <summary>Raised when the user presses Enter (barcode scanners send Enter after the code).</summary>
    public event EventHandler? EnterPressed;

    /// <summary>Raised when the trailing icon (<see cref="TrailingGlyph"/>) is clicked.</summary>
    public event EventHandler? TrailingIconClick;

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [AllowNull]
    public override string Text
    {
        // Null-safe: the base constructor may touch Text before the inner text box exists.
        get => _inner?.Text ?? string.Empty;
        set
        {
            if (_inner is not null)
            {
                _inner.Text = value ?? string.Empty;
            }
        }
    }

    [Category("Appearance")]
    [DefaultValue("")]
    [Description("Chữ gợi ý hiển thị khi ô trống.")]
    [AllowNull]
    public string PlaceholderText
    {
        get => _inner.PlaceholderText;
        set
        {
            _inner.PlaceholderText = value ?? string.Empty;
            if (string.IsNullOrEmpty(AccessibleName))
            {
                _inner.AccessibleName = value;
            }
        }
    }

    /// <summary>Leading icon from <see cref="Icons.Glyphs"/>.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? IconGlyph
    {
        get => _iconGlyph;
        set
        {
            if (_iconGlyph != value)
            {
                _iconGlyph = string.IsNullOrEmpty(value) ? null : value;
                PerformLayout();
                Invalidate();
            }
        }
    }

    /// <summary>Muted unit shown at the right end of the box, e.g. "₫", "kg", "%".</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? SuffixText
    {
        get => _suffixText;
        set
        {
            if (_suffixText != value)
            {
                _suffixText = string.IsNullOrEmpty(value) ? null : value;
                PerformLayout();
                Invalidate();
            }
        }
    }

    /// <summary>Clickable icon at the right end of the box (calendar, clear, show password); raises <see cref="TrailingIconClick"/>.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? TrailingGlyph
    {
        get => _trailingGlyph;
        set
        {
            if (_trailingGlyph != value)
            {
                _trailingGlyph = string.IsNullOrEmpty(value) ? null : value;
                PerformLayout();
                Invalidate();
            }
        }
    }

    [Category("Behavior")]
    [DefaultValue(false)]
    public bool UseSystemPasswordChar
    {
        get => _inner.UseSystemPasswordChar;
        set => _inner.UseSystemPasswordChar = value;
    }

    [Category("Behavior")]
    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set
        {
            _inner.ReadOnly = value;
            ApplyColors(ThemeManager.Current);
            Invalidate();
        }
    }

    [Category("Behavior")]
    [DefaultValue(32767)]
    public int MaxLength
    {
        get => _inner.MaxLength;
        set => _inner.MaxLength = value;
    }

    [Category("Appearance")]
    [DefaultValue(HorizontalAlignment.Left)]
    public HorizontalAlignment TextAlign
    {
        get => _inner.TextAlign;
        set => _inner.TextAlign = value;
    }

    [Category("Behavior")]
    [DefaultValue(CharacterCasing.Normal)]
    public CharacterCasing CharacterCasing
    {
        get => _inner.CharacterCasing;
        set => _inner.CharacterCasing = value;
    }

    [Category("Validation")]
    [DefaultValue(ValidationState.None)]
    public ValidationState ValidationState
    {
        get => _validationState;
        set
        {
            if (_validationState != value)
            {
                _validationState = value;
                Invalidate();
            }
        }
    }

    /// <summary>Message shown under the box for the current validation state (e.g. "Vui lòng nhập tên đăng nhập.").</summary>
    [Category("Validation")]
    [DefaultValue(null)]
    public string? ValidationMessage
    {
        get => _validationMessage;
        set
        {
            if (_validationMessage != value)
            {
                _validationMessage = value;
                Invalidate();
            }
        }
    }

    /// <summary>Hint shown under the box when there is no validation message.</summary>
    [Category("Appearance")]
    [DefaultValue(null)]
    public string? HelperText
    {
        get => _helperText;
        set
        {
            if (_helperText != value)
            {
                _helperText = value;
                Invalidate();
            }
        }
    }

    /// <summary>Keeps a line under the box for messages so the layout does not jump.</summary>
    [Category("Layout")]
    [DefaultValue(true)]
    public bool ReserveMessageSpace
    {
        get => _reserveMessageSpace;
        set
        {
            if (_reserveMessageSpace != value)
            {
                _reserveMessageSpace = value;
                Height = PreferredControlHeight;
                Invalidate();
            }
        }
    }

    /// <summary>Corner radius in pixels at 96 DPI; -1 uses the theme's medium radius.</summary>
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

    /// <summary>Height of the box itself in pixels at 96 DPI; -1 uses the theme's control height.</summary>
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

    /// <summary>The hosted text box, for selection and other advanced scenarios.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox InnerTextBox => _inner;

    bool IThemeable.ThemesChildren => true;

    /// <summary>Height of the box itself at the current DPI (without the message line).</summary>
    protected int ScaledBoxHeight => InputChrome.BoxHeight(this, _boxHeight);

    /// <summary>Hit area of the trailing icon, or empty when there is none.</summary>
    protected Rectangle TrailingIconBounds =>
        _trailingGlyph is null ? Rectangle.Empty : InputChrome.TrailingIconBounds(this, ScaledBoxHeight);

    private int PreferredControlHeight => ScaledBoxHeight + InputChrome.MessageLineHeight(this, _reserveMessageSpace);

    private string? CurrentMessage => InputChrome.MessageFor(_validationState, _validationMessage, _helperText);

    private int SuffixWidth => _suffixText is null
        ? 0
        : TextRenderer.MeasureText(_suffixText, Font, Size.Empty, TextFormatFlags.NoPadding).Width;

    /// <summary>Shows an error state and message.</summary>
    public void SetError(string message)
    {
        _validationMessage = message;
        ValidationState = ValidationState.Error;
        Invalidate();
    }

    /// <summary>Shows a warning state and message.</summary>
    public void SetWarning(string message)
    {
        _validationMessage = message;
        ValidationState = ValidationState.Warning;
        Invalidate();
    }

    public void ClearValidation()
    {
        _validationMessage = null;
        ValidationState = ValidationState.None;
        Invalidate();
    }

    public void SelectAll() => _inner.SelectAll();

    public void Clear() => _inner.Clear();

    void IThemeable.ApplyTheme(Theme theme)
    {
        ApplyColors(theme);
        Invalidate();
    }

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
            EnterPressed?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _inner.Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _inner.Focus();
        if (e.Button == MouseButtons.Left && Enabled && TrailingIconBounds.Contains(e.Location))
        {
            OnTrailingIconClick(EventArgs.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetTrailingHot(TrailingIconBounds.Contains(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetTrailingHot(false);
    }

    protected virtual void OnTrailingIconClick(EventArgs e) => TrailingIconClick?.Invoke(this, e);

    private void SetTrailingHot(bool hot)
    {
        if (_trailingHot != hot)
        {
            _trailingHot = hot;
            Cursor = hot ? Cursors.Hand : Cursors.IBeam;
            Invalidate(TrailingIconBounds);
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
            return; // Called from the base constructor, before the inner text box exists.
        }

        _inner.Font = Font;
        PerformLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = PreferredControlHeight;
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        // The height follows the box height and message line; only the width is free.
        base.SetBoundsCore(x, y, width, PreferredControlHeight, specified);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_inner is null)
        {
            return; // Called from the base constructor, before the inner text box exists.
        }

        var spacing = ThemeManager.Current.Spacing;
        var padding = this.ScaleDpi(spacing.Md);
        var left = padding + (_iconGlyph is null ? 0 : this.ScaleDpi(spacing.IconSize + spacing.Sm));
        var right = _trailingGlyph is null ? padding : Width - TrailingIconBounds.Left;
        if (_suffixText is not null)
        {
            right += SuffixWidth + this.ScaleDpi(spacing.Xs);
        }

        var top = Math.Max(0, (ScaledBoxHeight - _inner.PreferredHeight) / 2);
        _inner.SetBounds(left, top, Math.Max(0, Width - left - right), _inner.PreferredHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_inner is null)
        {
            return; // Not fully constructed yet.
        }

        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = ResolveColors(theme);
        g.Clear(BackColor);
        g.UseHighQuality();

        var boxHeight = ScaledBoxHeight;
        InputChrome.PaintBox(g, this, theme, colors, boxHeight, _cornerRadius,
            emphasized: _inner.Focused || _validationState != ValidationState.None);

        if (_iconGlyph is not null)
        {
            InputChrome.PaintGlyph(g, _iconGlyph, Font.SizeInPoints, InputChrome.LeadingIconBounds(this, boxHeight), colors.Icon);
        }

        var trailing = TrailingIconBounds;
        if (_trailingGlyph is not null)
        {
            if (_trailingHot && Enabled)
            {
                var hover = Rectangle.Inflate(trailing, -this.ScaleDpi(theme.Spacing.Xxs), -this.ScaleDpi(theme.Spacing.Xs));
                g.FillRoundedRectangle(theme.Colors.SurfaceAlt, hover, this.ScaleDpi(theme.Spacing.RadiusSm));
            }

            InputChrome.PaintGlyph(g, _trailingGlyph, Font.SizeInPoints, trailing, _trailingHot ? theme.Colors.Focus : colors.Icon);
        }

        if (_suffixText is not null)
        {
            var padding = this.ScaleDpi(theme.Spacing.Md);
            var suffixRight = _trailingGlyph is null ? Width - padding : trailing.Left;
            var width = SuffixWidth;
            var suffixBounds = new Rectangle(suffixRight - width, 0, width, boxHeight);
            TextRenderer.DrawText(g, _suffixText, Font, suffixBounds, Enabled ? theme.Colors.TextMuted : theme.Colors.DisabledText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        if (_reserveMessageSpace)
        {
            InputChrome.PaintMessage(g, this, theme, CurrentMessage, boxHeight, colors.Message);
        }
    }

    private InputColors ResolveColors(Theme theme) =>
        InputPalette.Resolve(theme, _validationState, _inner.Focused, Enabled, _inner.ReadOnly);

    private void ApplyColors(Theme theme)
    {
        if (_inner is null)
        {
            return; // Called from the base constructor, before the inner text box exists.
        }

        var colors = ResolveColors(theme);
        BackColor = InputChrome.OuterBackColor(this, theme);
        _inner.BackColor = colors.Background;
        _inner.ForeColor = colors.Foreground;
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

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ApplyColors(e.Current);
        PerformLayout();
        Invalidate();
    }
}
