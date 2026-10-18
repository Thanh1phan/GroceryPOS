using System.Drawing;

namespace GroceryPOS.UI.Controls.Theming;

public readonly record struct ButtonColors(Color Background, Color Foreground, Color Border);

public readonly record struct InputColors(Color Background, Color Foreground, Color Border, Color Icon, Color Message);

/// <summary>Maps a button variant and state to colors. Pure, so it is unit-tested without WinForms.</summary>
public static class ButtonPalette
{
    public static ButtonColors Resolve(Theme theme, ButtonVariant variant, InteractionState state)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;

        if (state == InteractionState.Disabled)
        {
            return variant == ButtonVariant.Ghost
                ? new ButtonColors(Color.Transparent, c.DisabledText, Color.Transparent)
                : new ButtonColors(c.DisabledBackground, c.DisabledText, c.DisabledBackground);
        }

        switch (variant)
        {
            case ButtonVariant.Primary:
            case ButtonVariant.Danger:
                var (fill, on) = variant == ButtonVariant.Primary ? (c.Primary, c.OnPrimary) : (c.Danger, c.OnDanger);
                var back = Shade(theme, fill, state);
                return new ButtonColors(back, on, back);

            case ButtonVariant.Secondary:
                var surface = state switch
                {
                    InteractionState.Hover => c.SurfaceAlt,
                    InteractionState.Pressed => theme.Pressed(c.SurfaceAlt),
                    _ => c.Surface,
                };
                return new ButtonColors(surface, c.TextPrimary, c.Border);

            case ButtonVariant.Ghost:
                var ghost = state switch
                {
                    InteractionState.Hover => c.SurfaceAlt,
                    InteractionState.Pressed => theme.Pressed(c.SurfaceAlt),
                    _ => Color.Transparent,
                };
                return new ButtonColors(ghost, c.Primary, Color.Transparent);

            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
        }
    }

    private static Color Shade(Theme theme, Color fill, InteractionState state) => state switch
    {
        InteractionState.Hover => theme.Hover(fill),
        InteractionState.Pressed => theme.Pressed(fill),
        _ => fill,
    };
}

/// <summary>Maps an input's validation/focus/enabled state to colors.</summary>
public static class InputPalette
{
    public static InputColors Resolve(Theme theme, ValidationState validation, bool focused, bool enabled, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;

        if (!enabled)
        {
            return new InputColors(c.DisabledBackground, c.DisabledText, c.Border, c.DisabledText, c.DisabledText);
        }

        var accent = AccentOf(theme, validation);
        var border = accent ?? (focused && !readOnly ? c.Focus : c.Border);
        var background = readOnly ? c.SurfaceAlt : c.Surface;
        var icon = accent ?? (focused ? c.Focus : c.TextMuted);
        var message = accent ?? c.TextSecondary;
        return new InputColors(background, c.TextPrimary, border, icon, message);
    }

    /// <summary>Color that signals a validation state, or null when the state carries no color.</summary>
    public static Color? AccentOf(Theme theme, ValidationState validation) => validation switch
    {
        ValidationState.Error => theme.Colors.Danger,
        ValidationState.Warning => theme.Colors.Warning,
        ValidationState.Valid => theme.Colors.Success,
        _ => null,
    };
}
