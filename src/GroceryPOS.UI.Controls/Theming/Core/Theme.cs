using System.Drawing;

namespace GroceryPOS.UI.Controls.Theming;

/// <summary>A complete set of design tokens.</summary>
public sealed record Theme(string Name, ThemeMode Mode, ColorTokens Colors, TypographyTokens Typography, SpacingTokens Spacing)
{
    public bool IsDark => Mode == ThemeMode.Dark;

    /// <summary>Shade used when the pointer is over a filled element: darker on light themes, lighter on dark ones.</summary>
    public Color Hover(Color color) => IsDark ? ColorMath.Lighten(color, 0.10) : ColorMath.Darken(color, 0.08);

    /// <summary>Shade used while a filled element is pressed.</summary>
    public Color Pressed(Color color) => IsDark ? ColorMath.Lighten(color, 0.20) : ColorMath.Darken(color, 0.16);
}

/// <summary>Built-in themes.</summary>
public static class Themes
{
    public static Theme Light { get; } = new(
        "Sáng",
        ThemeMode.Light,
        new ColorTokens
        {
            Primary = ColorMath.FromHex("#15803D"),
            OnPrimary = Color.White,
            Danger = ColorMath.FromHex("#B91C1C"),
            OnDanger = Color.White,
            Success = ColorMath.FromHex("#15803D"),
            Warning = ColorMath.FromHex("#B45309"),
            Info = ColorMath.FromHex("#1D4ED8"),
            Background = ColorMath.FromHex("#F3F4F6"),
            Surface = ColorMath.FromHex("#FFFFFF"),
            SurfaceAlt = ColorMath.FromHex("#F3F4F6"),
            Border = ColorMath.FromHex("#D1D5DB"),
            TextPrimary = ColorMath.FromHex("#111827"),
            TextSecondary = ColorMath.FromHex("#4B5563"),
            TextMuted = ColorMath.FromHex("#6B7280"),
            DisabledBackground = ColorMath.FromHex("#E5E7EB"),
            DisabledText = ColorMath.FromHex("#9CA3AF"),
            Focus = ColorMath.FromHex("#2563EB"),
        },
        new TypographyTokens(),
        new SpacingTokens());

    public static Theme Dark { get; } = new(
        "Tối",
        ThemeMode.Dark,
        new ColorTokens
        {
            Primary = ColorMath.FromHex("#22C55E"),
            OnPrimary = ColorMath.FromHex("#052E16"),
            Danger = ColorMath.FromHex("#F87171"),
            OnDanger = ColorMath.FromHex("#450A0A"),
            Success = ColorMath.FromHex("#4ADE80"),
            Warning = ColorMath.FromHex("#FBBF24"),
            Info = ColorMath.FromHex("#60A5FA"),
            Background = ColorMath.FromHex("#0F172A"),
            Surface = ColorMath.FromHex("#1E293B"),
            SurfaceAlt = ColorMath.FromHex("#273449"),
            Border = ColorMath.FromHex("#475569"),
            TextPrimary = ColorMath.FromHex("#F1F5F9"),
            TextSecondary = ColorMath.FromHex("#CBD5E1"),
            TextMuted = ColorMath.FromHex("#94A3B8"),
            DisabledBackground = ColorMath.FromHex("#334155"),
            DisabledText = ColorMath.FromHex("#64748B"),
            Focus = ColorMath.FromHex("#60A5FA"),
        },
        new TypographyTokens(),
        new SpacingTokens());

    public static Theme For(ThemeMode mode) => mode == ThemeMode.Dark ? Dark : Light;

    /// <summary>Parses a stored setting value (<c>Light</c>/<c>Dark</c>, case-insensitive); anything else means light.</summary>
    public static ThemeMode ParseMode(string? value) =>
        Enum.TryParse<ThemeMode>(value?.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : ThemeMode.Light;
}
