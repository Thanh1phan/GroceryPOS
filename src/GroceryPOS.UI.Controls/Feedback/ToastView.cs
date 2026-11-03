using System.ComponentModel;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Feedback;

/// <summary>
/// One toast card: colored side bar, icon, bold title, wrapped message, optional action link, close button
/// and a thin line showing the time left. Created and positioned by <see cref="Toaster"/>.
/// </summary>
[ToolboxItem(false)]
internal sealed class ToastView : Control, IThemeable
{
    private const int DesignWidth = 360;
    private const int PadDesign = 12;
    private const int IconBox = 32;
    private const int CloseBox = 24;

    private Rectangle _closeBounds;
    private Rectangle _actionBounds;
    private bool _closeHot;
    private bool _actionHot;

    public ToastView(Toast toast)
    {
        Toast = toast;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.Alert;
        UpdateAccessibleText();
    }

    public Toast Toast { get; }

    public event EventHandler? CloseClicked;

    public event EventHandler? ActionClicked;

    /// <summary>Preferred width at the control's DPI.</summary>
    public int PreferredWidth => this.ScaleDpi(DesignWidth);

    /// <summary>Height needed to show the whole toast at <paramref name="width"/>.</summary>
    public int MeasureHeight(int width)
    {
        var pad = this.ScaleDpi(PadDesign);
        var textWidth = Math.Max(40, TextWidth(width));
        var height = pad;
        if (Toast.Title is not null)
        {
            height += TextRenderer.MeasureText(Toast.Title, TitleFont, new Size(textWidth, int.MaxValue), WrapFlags).Height + this.ScaleDpi(2);
        }

        height += TextRenderer.MeasureText(Toast.Message, BodyFont, new Size(textWidth, int.MaxValue), WrapFlags).Height;
        if (Toast.ActionText is not null)
        {
            height += this.ScaleDpi(6) + TextRenderer.MeasureText(Toast.ActionText, ActionFont).Height;
        }

        height += pad + this.ScaleDpi(3); // bottom padding + progress line
        return Math.Max(height, this.ScaleDpi(IconBox) + 2 * pad);
    }

    public void UpdateContent(bool contentChanged)
    {
        if (contentChanged)
        {
            UpdateAccessibleText();
        }

        Invalidate();
    }

    void IThemeable.ApplyTheme(Theme theme) => Invalidate();

    private static Font TitleFont => ThemeFonts.Get(FontRole.Body, FontStyle.Bold);

    private static Font BodyFont => ThemeFonts.Get(FontRole.Body);

    private static Font ActionFont => ThemeFonts.Get(FontRole.Body, FontStyle.Bold | FontStyle.Underline);

    private static TextFormatFlags WrapFlags => TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    private int TextLeft => this.ScaleDpi(4 + PadDesign + IconBox + 10);

    private int TextWidth(int width) => width - TextLeft - this.ScaleDpi(PadDesign + CloseBox + 4);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateRegion();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var closeHot = _closeBounds.Contains(e.Location);
        var actionHot = _actionBounds.Contains(e.Location);
        if (closeHot != _closeHot || actionHot != _actionHot)
        {
            _closeHot = closeHot;
            _actionHot = actionHot;
            Cursor = closeHot || actionHot ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_closeHot || _actionHot)
        {
            _closeHot = _actionHot = false;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_actionBounds.Contains(e.Location))
        {
            ActionClicked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Clicking anywhere else on a toast dismisses it, like most notification UIs.
            CloseClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = ThemeManager.Current;
        var colors = FeedbackPalette.Resolve(theme, Toast.Kind);
        var radius = this.ScaleDpi(theme.Spacing.RadiusLg);
        var pad = this.ScaleDpi(PadDesign);

        g.Clear(colors.Background);
        g.UseHighQuality();
        var bounds = new Rectangle(0, 0, Width, Height);
        g.DrawRoundedRectangle(colors.Border, bounds, radius, Math.Max(1, this.ScaleDpi(1)));

        // Side bar in the accent color.
        using (var bar = new SolidBrush(colors.Accent))
        {
            g.FillRectangle(bar, 0, 0, this.ScaleDpi(4), Height);
        }

        // Icon in a soft circle.
        var iconSize = this.ScaleDpi(IconBox);
        var icon = new Rectangle(this.ScaleDpi(4) + pad, pad, iconSize, iconSize);
        g.FillRoundedRectangle(colors.IconBackground, icon, iconSize / 2f);
        TextRenderer.DrawText(g, FeedbackGlyphs.For(Toast.Kind), ThemeFonts.Icon(12f), icon, colors.IconForeground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Texts.
        var left = TextLeft;
        var textWidth = Math.Max(40, TextWidth(Width));
        var y = pad;
        if (Toast.Title is not null)
        {
            var title = Toast.Repeat > 1 ? $"{Toast.Title}  ×{Toast.Repeat}" : Toast.Title;
            var size = TextRenderer.MeasureText(title, TitleFont, new Size(textWidth, int.MaxValue), WrapFlags);
            TextRenderer.DrawText(g, title, TitleFont, new Rectangle(left, y, textWidth, size.Height), colors.Title, WrapFlags);
            y += size.Height + this.ScaleDpi(2);
        }

        var message = Toast.Title is null && Toast.Repeat > 1 ? $"{Toast.Message}  ×{Toast.Repeat}" : Toast.Message;
        var messageSize = TextRenderer.MeasureText(message, BodyFont, new Size(textWidth, int.MaxValue), WrapFlags);
        TextRenderer.DrawText(g, message, BodyFont, new Rectangle(left, y, textWidth, messageSize.Height),
            Toast.Title is null ? colors.Title : colors.Text, WrapFlags);
        y += messageSize.Height;

        _actionBounds = Rectangle.Empty;
        if (Toast.ActionText is not null)
        {
            y += this.ScaleDpi(6);
            var actionSize = TextRenderer.MeasureText(Toast.ActionText, ActionFont);
            _actionBounds = new Rectangle(left, y, actionSize.Width, actionSize.Height);
            var actionColor = _actionHot ? theme.Hover(colors.Accent) : colors.Accent;
            TextRenderer.DrawText(g, Toast.ActionText, ActionFont, _actionBounds, ColorMath.EnsureContrast(actionColor, colors.Background), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // Close button.
        var closeSize = this.ScaleDpi(CloseBox);
        _closeBounds = new Rectangle(Width - pad - closeSize, pad - this.ScaleDpi(4), closeSize, closeSize);
        if (_closeHot)
        {
            g.FillRoundedRectangle(theme.Colors.SurfaceAlt, _closeBounds, this.ScaleDpi(theme.Spacing.RadiusSm));
        }

        TextRenderer.DrawText(g, Glyphs.Close, ThemeFonts.Icon(8f), _closeBounds, _closeHot ? colors.Title : colors.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Time left.
        if (!Toast.IsSticky)
        {
            var lineHeight = this.ScaleDpi(3);
            var full = Width - this.ScaleDpi(4);
            var width = (int)Math.Round(full * Toast.Progress);
            using var brush = new SolidBrush(ColorMath.WithAlpha(colors.Accent, 160));
            g.FillRectangle(brush, this.ScaleDpi(4), Height - lineHeight, width, lineHeight);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ThemeChanged += OnThemeChanged;
        UpdateRegion();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Region?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => Invalidate();

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var radius = this.ScaleDpi(ThemeManager.Current.Spacing.RadiusLg);
        using var path = GraphicsExtensions.CreateRoundedRectangle(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    private void UpdateAccessibleText()
    {
        AccessibleName = Toast.Title ?? FeedbackGlyphs.NameOf(Toast.Kind);
        AccessibleDescription = Toast.Message;
    }
}

/// <summary>Icon glyph and spoken name for each feedback kind.</summary>
internal static class FeedbackGlyphs
{
    public static string For(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => Glyphs.Completed,
        FeedbackKind.Warning => Glyphs.Warning,
        FeedbackKind.Error => Glyphs.Error,
        FeedbackKind.Danger => Glyphs.Delete,
        FeedbackKind.Question => Glyphs.Help,
        _ => Glyphs.Info,
    };

    public static string NameOf(FeedbackKind kind) => kind switch
    {
        FeedbackKind.Success => "Thành công",
        FeedbackKind.Warning => "Cảnh báo",
        FeedbackKind.Error => "Lỗi",
        FeedbackKind.Danger => "Xác nhận",
        FeedbackKind.Question => "Câu hỏi",
        _ => "Thông báo",
    };
}
