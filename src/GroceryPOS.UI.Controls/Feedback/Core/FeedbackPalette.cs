using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <param name="Accent">Strong color: the toast's side bar, the progress line, a dialog's icon.</param>
/// <param name="IconBackground">Soft tint of the accent behind the icon.</param>
/// <param name="IconForeground">Icon color on <paramref name="IconBackground"/> (WCAG AA 4.5:1 or better).</param>
/// <param name="Background">Toast / dialog surface.</param>
/// <param name="Title">Title text on <paramref name="Background"/>.</param>
/// <param name="Text">Message text on <paramref name="Background"/>.</param>
/// <param name="Border">Outline of a toast card.</param>
public readonly record struct FeedbackColors(
    Color Accent,
    Color IconBackground,
    Color IconForeground,
    Color Background,
    Color Title,
    Color Text,
    Color Border);

/// <summary>Toast and dialog colors derived from the theme tokens, contrast-checked against their surface.</summary>
public static class FeedbackPalette
{
    public static FeedbackColors Resolve(Theme theme, FeedbackKind kind)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        var accent = AccentOf(theme, kind);

        // Toasts float above the page, so on dark themes they are lifted slightly off the surface.
        var background = theme.IsDark ? ColorMath.Lighten(c.Surface, 0.06) : c.Surface;
        var iconBackground = ColorMath.Blend(background, accent, theme.IsDark ? 0.24 : 0.14);
        return new FeedbackColors(
            Accent: ColorMath.EnsureContrast(accent, background, 3.0),
            IconBackground: iconBackground,
            IconForeground: ColorMath.EnsureContrast(accent, iconBackground),
            Background: background,
            Title: ColorMath.EnsureContrast(c.TextPrimary, background),
            Text: ColorMath.EnsureContrast(c.TextSecondary, background),
            Border: theme.IsDark ? ColorMath.Lighten(c.Border, 0.10) : c.Border);
    }

    public static Color AccentOf(Theme theme, FeedbackKind kind)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        return kind switch
        {
            FeedbackKind.Success => c.Success,
            FeedbackKind.Warning => c.Warning,
            FeedbackKind.Error or FeedbackKind.Danger => c.Danger,
            FeedbackKind.Question => c.Primary,
            _ => c.Info,
        };
    }

    /// <summary>Color of the page dimmed behind a modal dialog or under a loading overlay.</summary>
    public static Color Scrim(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.IsDark ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(110, 17, 24, 39);
    }
}
