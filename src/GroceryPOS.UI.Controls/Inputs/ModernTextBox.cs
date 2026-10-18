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

    private int ScaledBoxHeight => this.ScaleDpi(_boxHeight > 0 ? _boxHeight : ThemeManager.Current.Spacing.ControlHeight);

    private int MessageLineHeight => _reserveMessageSpace
        ? TextRenderer.MeasureText("Ág", ThemeFonts.Get(FontRole.Caption)).Height + this.ScaleDpi(ThemeManager.Current.Spacing.Xs)
        : 0;

    private int PreferredControlHeight => ScaledBoxHeight + MessageLineHeight;

    private string? CurrentMessage =>
        !string.IsNullOrWhiteSpace(_validationMessage) && _validationState != ValidationState.None
            ? _validationMessage
            : _helperText;

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
        var top = Math.Max(0, (ScaledBoxHeight - _inner.PreferredHeight) / 2);
        _inner.SetBounds(left, top, Math.Max(0, Width - left - padding), _inner.PreferredHeight);
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

        var radius = this.ScaleDpi(_cornerRadius >= 0 ? _cornerRadius : theme.Spacing.RadiusMd);
        var box = new RectangleF(0, 0, Width, ScaledBoxHeight);
        var focused = _inner.Focused;
        var borderWidth = this.ScaleDpi(focused || _validationState != ValidationState.None
            ? theme.Spacing.FocusRingWidth
            : theme.Spacing.BorderWidth);

        g.FillRoundedRectangle(colors.Background, box, radius);
        g.DrawRoundedRectangle(colors.Border, box, radius, borderWidth);

        if (_iconGlyph is not null)
        {
            var iconBounds = new Rectangle(this.ScaleDpi(theme.Spacing.Md), 0, this.ScaleDpi(theme.Spacing.IconSize), ScaledBoxHeight);
            TextRenderer.DrawText(g, _iconGlyph, ThemeFonts.Icon(Font.SizeInPoints), iconBounds, colors.Icon,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var message = CurrentMessage;
        if (_reserveMessageSpace && !string.IsNullOrWhiteSpace(message))
        {
            var top = ScaledBoxHeight + this.ScaleDpi(theme.Spacing.Xxs);
            var messageBounds = new Rectangle(this.ScaleDpi(theme.Spacing.Xs), top, Width - this.ScaleDpi(theme.Spacing.Xs), Height - top);
            TextRenderer.DrawText(g, message, ThemeFonts.Get(FontRole.Caption), messageBounds, colors.Message,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
        BackColor = Parent?.BackColor is { A: 255 } parentBack ? parentBack : theme.Colors.Background;
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
