using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

internal enum LabelTone
{
    Normal,
    Muted,
    Primary,
    Success,
    Warning,
    Danger,
}

/// <summary>Label whose color follows a semantic tone, re-applied on every theme change.</summary>
internal sealed class ToneLabel : Label, IThemeable
{
    private LabelTone _tone;

    public LabelTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            ApplyTheme(ThemeManager.Current);
        }
    }

    public void ApplyTheme(Theme theme)
    {
        var c = theme.Colors;
        ForeColor = _tone switch
        {
            LabelTone.Muted => c.TextSecondary,
            LabelTone.Primary => c.Primary,
            LabelTone.Success => c.Success,
            LabelTone.Warning => c.Warning,
            LabelTone.Danger => c.Danger,
            _ => c.TextPrimary,
        };
    }
}
