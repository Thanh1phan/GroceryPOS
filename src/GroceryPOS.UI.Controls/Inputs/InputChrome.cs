using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Shared painting and sizing for the rounded input "box" used by <see cref="ModernTextBox"/>,
/// <see cref="ModernComboBox"/> and the controls built on them, so every input looks the same.
/// </summary>
internal static class InputChrome
{
    private const TextFormatFlags CenteredGlyph =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

    /// <summary>Height of the box at the control's DPI (<paramref name="designHeight"/> ≤ 0 uses the theme's control height).</summary>
    public static int BoxHeight(Control control, int designHeight) =>
        control.ScaleDpi(designHeight > 0 ? designHeight : ThemeManager.Current.Spacing.ControlHeight);

    /// <summary>Height of the validation/helper line under the box, or 0 when no space is reserved.</summary>
    public static int MessageLineHeight(Control control, bool reserve) => reserve
        ? TextRenderer.MeasureText("Ág", ThemeFonts.Get(FontRole.Caption)).Height + control.ScaleDpi(ThemeManager.Current.Spacing.Xs)
        : 0;

    /// <summary>The message to show: the validation message when a state is set, otherwise the helper text.</summary>
    public static string? MessageFor(ValidationState state, string? validationMessage, string? helperText) =>
        !string.IsNullOrWhiteSpace(validationMessage) && state != ValidationState.None ? validationMessage : helperText;

    /// <summary>Fills and strokes the rounded box; the border is thicker when focused or carrying a validation state.</summary>
    public static void PaintBox(Graphics g, Control control, Theme theme, InputColors colors, int boxHeight, int designRadius, bool emphasized)
    {
        var radius = control.ScaleDpi(designRadius >= 0 ? designRadius : theme.Spacing.RadiusMd);
        var box = new RectangleF(0, 0, control.Width, boxHeight);
        var borderWidth = control.ScaleDpi(emphasized ? theme.Spacing.FocusRingWidth : theme.Spacing.BorderWidth);
        g.FillRoundedRectangle(colors.Background, box, radius);
        g.DrawRoundedRectangle(colors.Border, box, radius, borderWidth);
    }

    /// <summary>Bounds of a leading icon slot inside the box.</summary>
    public static Rectangle LeadingIconBounds(Control control, int boxHeight)
    {
        var spacing = ThemeManager.Current.Spacing;
        return new Rectangle(control.ScaleDpi(spacing.Md), 0, control.ScaleDpi(spacing.IconSize), boxHeight);
    }

    /// <summary>Bounds of a trailing (clickable) icon slot inside the box, flush with the right padding.</summary>
    public static Rectangle TrailingIconBounds(Control control, int boxHeight)
    {
        var spacing = ThemeManager.Current.Spacing;
        var size = control.ScaleDpi(spacing.IconSize + spacing.Sm);
        return new Rectangle(control.Width - control.ScaleDpi(spacing.Sm) - size, 0, size, boxHeight);
    }

    public static void PaintGlyph(Graphics g, string glyph, float sizeInPoints, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(g, glyph, ThemeFonts.Icon(sizeInPoints), bounds, color, CenteredGlyph);

    /// <summary>Draws the validation/helper line under the box.</summary>
    public static void PaintMessage(Graphics g, Control control, Theme theme, string? message, int boxHeight, Color color)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var top = boxHeight + control.ScaleDpi(theme.Spacing.Xxs);
        var left = control.ScaleDpi(theme.Spacing.Xs);
        var bounds = new Rectangle(left, top, control.Width - left, control.Height - top);
        TextRenderer.DrawText(g, message, ThemeFonts.Get(FontRole.Caption), bounds, color,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    /// <summary>The control's own back color: its parent's when opaque, so the rounded corners blend in.</summary>
    public static Color OuterBackColor(Control control, Theme theme) =>
        control.Parent?.BackColor is { A: 255 } parentBack ? parentBack : theme.Colors.Background;
}
