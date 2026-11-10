using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>Semantic color of a <see cref="FormNote"/>.</summary>
public enum NoteTone
{
    Normal,
    Muted,
    Primary,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// Wrapped text inside a dialog (context line, live preview, hint). Its height follows the text at the current width,
/// so a <see cref="BaseEditDialog"/> re-lays out its fields when a preview grows or shrinks. Colors follow
/// <see cref="Tone"/> and are re-applied on every theme change.
/// </summary>
public class FormNote : Label, IThemeable
{
    private NoteTone _tone;
    private FontRole _role = FontRole.Body;
    private FontStyle _style = FontStyle.Regular;
    private int _minLines = 1;

    public FormNote()
    {
        AutoSize = false;
        AutoEllipsis = false;
        UseMnemonic = false;
        TabStop = false;
        Font = ThemeFonts.Get(_role, _style);
        ApplyTheme(ThemeManager.Current);
        FitHeight();
    }

    [Category("Appearance")]
    [DefaultValue(NoteTone.Normal)]
    public NoteTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            ApplyTheme(ThemeManager.Current);
        }
    }

    /// <summary>Text size from the theme's typography scale.</summary>
    [Category("Appearance")]
    [DefaultValue(FontRole.Body)]
    public FontRole FontRole
    {
        get => _role;
        set
        {
            _role = value;
            Font = ThemeFonts.Get(_role, _style);
        }
    }

    [Category("Appearance")]
    [DefaultValue(FontStyle.Regular)]
    public FontStyle TextStyle
    {
        get => _style;
        set
        {
            _style = value;
            Font = ThemeFonts.Get(_role, _style);
        }
    }

    /// <summary>Height kept even when the text is shorter (avoids the dialog jumping while a preview changes).</summary>
    [Category("Layout")]
    [DefaultValue(1)]
    public int MinLines
    {
        get => _minLines;
        set
        {
            _minLines = Math.Max(0, value);
            FitHeight();
        }
    }

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value ?? string.Empty;
            FitHeight();
        }
    }

    /// <summary>Sets text and tone together.</summary>
    public void Set(string? text, NoteTone tone)
    {
        _tone = tone;
        Text = text;
        ApplyTheme(ThemeManager.Current);
    }

    public void ApplyTheme(Theme theme)
    {
        var c = theme.Colors;
        ForeColor = _tone switch
        {
            NoteTone.Muted => c.TextSecondary,
            NoteTone.Primary => c.Primary,
            NoteTone.Success => c.Success,
            NoteTone.Warning => c.Warning,
            NoteTone.Danger => c.Danger,
            _ => c.TextPrimary,
        };
        Font = ThemeFonts.Get(_role, _style);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FitHeight();
    }

    private void FitHeight()
    {
        if (Font is null)
        {
            return;
        }

        var lineHeight = Font.Height;
        var measured = string.IsNullOrEmpty(Text)
            ? 0
            : TextRenderer.MeasureText(Text, Font, new Size(Math.Max(20, Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        var height = Math.Max(measured, _minLines * lineHeight) + 2;
        if (Height != height)
        {
            Height = height;
        }
    }
}
