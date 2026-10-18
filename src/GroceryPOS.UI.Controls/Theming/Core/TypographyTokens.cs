namespace GroceryPOS.UI.Controls.Theming;

/// <summary>Font families and sizes (points). Fonts are created and cached by <c>ThemeFonts</c>.</summary>
public sealed record TypographyTokens
{
    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Preferred icon font (Windows 11); <see cref="IconFontFallback"/> is used when it is not installed.</summary>
    public string IconFontFamily { get; init; } = "Segoe Fluent Icons";

    /// <summary>Icon font shipped with Windows 10.</summary>
    public string IconFontFallback { get; init; } = "Segoe MDL2 Assets";

    public float Caption { get; init; } = 8.25f;

    public float Body { get; init; } = 9.75f;

    public float Subtitle { get; init; } = 11.25f;

    public float Title { get; init; } = 14f;

    public float Headline { get; init; } = 20f;

    /// <summary>Big totals on the POS screen (tổng tiền, tiền thừa).</summary>
    public float Display { get; init; } = 28f;

    public float SizeOf(FontRole role) => role switch
    {
        FontRole.Caption => Caption,
        FontRole.Body => Body,
        FontRole.Subtitle => Subtitle,
        FontRole.Title => Title,
        FontRole.Headline => Headline,
        FontRole.Display => Display,
        _ => Body,
    };
}

public enum FontRole
{
    Caption,
    Body,
    Subtitle,
    Title,
    Headline,
    Display,
}
