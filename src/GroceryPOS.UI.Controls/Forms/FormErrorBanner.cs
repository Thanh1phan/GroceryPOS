using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>
/// Inline error message of a dialog: a soft red card with an error icon and wrapped text. Hidden while
/// <see cref="Control.Text"/> is empty; its height follows the text so long messages are never cut off.
/// </summary>
public sealed class FormErrorBanner : Control, IThemeable
{
    private const int PaddingX = 12;
    private const int PaddingY = 8;
    private const int IconWidth = 24;

    public FormErrorBanner()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        Font = ThemeFonts.Get(FontRole.Body);
        Visible = false;
        TabStop = false;
    }

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value ?? string.Empty;
            Visible = !string.IsNullOrWhiteSpace(base.Text);
            FitHeight();
            Invalidate();
        }
    }

    public void ApplyTheme(Theme theme) => Invalidate();

    /// <summary>Height needed to show the whole message at the current width.</summary>
    public int MeasureHeight()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return 0;
        }

        var textWidth = Math.Max(40, Width - (PaddingX * 2) - IconWidth);
        var size = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return Math.Max(size.Height, Font.Height) + (PaddingY * 2);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = ThemeManager.Current;
        var colors = FeedbackPalette.Resolve(theme, FeedbackKind.Error);
        var g = e.Graphics;
        g.UseHighQuality();
        g.Clear(Parent?.BackColor ?? theme.Colors.Background);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        g.FillRoundedRectangle(colors.IconBackground, bounds, theme.Spacing.RadiusMd);
        g.DrawRoundedRectangle(colors.Accent, bounds, theme.Spacing.RadiusMd);

        var iconBounds = new Rectangle(PaddingX - 4, PaddingY, IconWidth, Font.Height);
        TextRenderer.DrawText(g, Glyphs.Error, ThemeFonts.Icon(11f), iconBounds, colors.IconForeground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        var textBounds = new Rectangle(PaddingX + IconWidth, PaddingY, Width - (PaddingX * 2) - IconWidth, Height - (PaddingY * 2));
        TextRenderer.DrawText(g, Text, Font, textBounds, colors.IconForeground, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private void FitHeight()
    {
        var height = MeasureHeight();
        if (height > 0 && Height != height)
        {
            Height = height;
        }
    }
}
