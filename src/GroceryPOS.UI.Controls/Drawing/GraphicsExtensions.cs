using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GroceryPOS.UI.Controls.Drawing;

/// <summary>Drawing helpers for rounded, anti-aliased controls.</summary>
public static class GraphicsExtensions
{
    /// <summary>Builds a rounded-rectangle path; a radius of 0 gives square corners.</summary>
    public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
        if (radius <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Turns on anti-aliasing for shapes and ClearType for text.</summary>
    public static void UseHighQuality(this Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void FillRoundedRectangle(this Graphics graphics, Color color, RectangleF bounds, float radius)
    {
        if (color.A == 0)
        {
            return;
        }

        using var path = CreateRoundedRectangle(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    /// <summary>Strokes the border inside <paramref name="bounds"/> so thick borders are not clipped.</summary>
    public static void DrawRoundedRectangle(this Graphics graphics, Color color, RectangleF bounds, float radius, float width = 1f)
    {
        if (color.A == 0 || width <= 0f)
        {
            return;
        }

        var inset = width / 2f;
        var inner = RectangleF.Inflate(bounds, -inset, -inset);
        using var path = CreateRoundedRectangle(inner, Math.Max(0f, radius - inset));
        using var pen = new Pen(color, width);
        graphics.DrawPath(pen, path);
    }

    /// <summary>Scales a 96-DPI design value to the control's current DPI.</summary>
    public static int ScaleDpi(this Control control, int value) =>
        (int)Math.Round(value * control.DeviceDpi / 96d, MidpointRounding.AwayFromZero);
}
