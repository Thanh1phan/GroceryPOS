using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Display.Core;

namespace GroceryPOS.App.Forms;

/// <summary>Maps the presenters' UI-free <see cref="Tone"/> to the label and badge styles of the views.</summary>
internal static class ToneMapping
{
    public static LabelTone ToLabelTone(this Tone tone) => tone switch
    {
        Tone.Muted => LabelTone.Muted,
        Tone.Info => LabelTone.Primary,
        Tone.Success => LabelTone.Success,
        Tone.Warning => LabelTone.Warning,
        Tone.Danger => LabelTone.Danger,
        _ => LabelTone.Normal,
    };

    public static BadgeKind ToBadgeKind(this Tone tone) => tone switch
    {
        Tone.Info => BadgeKind.Info,
        Tone.Success => BadgeKind.Success,
        Tone.Warning => BadgeKind.Warning,
        Tone.Danger => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };
}
