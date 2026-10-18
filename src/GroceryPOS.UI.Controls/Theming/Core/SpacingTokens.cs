namespace GroceryPOS.UI.Controls.Theming;

/// <summary>Spacing, corner radius and control size tokens, in pixels at 96 DPI.</summary>
public sealed record SpacingTokens
{
    public int Xxs { get; init; } = 2;

    public int Xs { get; init; } = 4;

    public int Sm { get; init; } = 8;

    public int Md { get; init; } = 12;

    public int Lg { get; init; } = 16;

    public int Xl { get; init; } = 24;

    public int Xxl { get; init; } = 32;

    public int RadiusSm { get; init; } = 4;

    public int RadiusMd { get; init; } = 8;

    public int RadiusLg { get; init; } = 12;

    /// <summary>Default height of buttons and single-line inputs.</summary>
    public int ControlHeight { get; init; } = 36;

    /// <summary>Touch-friendly height used on the POS screen.</summary>
    public int ControlHeightLarge { get; init; } = 48;

    public int BorderWidth { get; init; } = 1;

    public int FocusRingWidth { get; init; } = 2;

    /// <summary>Width reserved for a leading icon inside inputs and buttons.</summary>
    public int IconSize { get; init; } = 16;
}
