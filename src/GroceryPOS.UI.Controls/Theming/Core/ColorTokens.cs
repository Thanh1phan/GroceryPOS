using System.Drawing;

namespace GroceryPOS.UI.Controls.Theming;

/// <summary>
/// Semantic color tokens. Controls never hard-code colors; they ask the current theme for a token.
/// </summary>
public sealed record ColorTokens
{
    /// <summary>Brand / primary action (thanh toán, lưu).</summary>
    public required Color Primary { get; init; }

    public required Color OnPrimary { get; init; }

    /// <summary>Destructive action (xóa, hủy hóa đơn).</summary>
    public required Color Danger { get; init; }

    public required Color OnDanger { get; init; }

    public required Color Success { get; init; }

    public required Color Warning { get; init; }

    public required Color Info { get; init; }

    /// <summary>Window / page background.</summary>
    public required Color Background { get; init; }

    /// <summary>Cards, dialogs, inputs.</summary>
    public required Color Surface { get; init; }

    /// <summary>Alternate rows, hovered list items, secondary buttons on hover.</summary>
    public required Color SurfaceAlt { get; init; }

    public required Color Border { get; init; }

    public required Color TextPrimary { get; init; }

    public required Color TextSecondary { get; init; }

    /// <summary>Placeholders and icons inside inputs.</summary>
    public required Color TextMuted { get; init; }

    public required Color DisabledBackground { get; init; }

    public required Color DisabledText { get; init; }

    /// <summary>Keyboard focus ring and focused input border.</summary>
    public required Color Focus { get; init; }
}
