using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Display.Core;

/// <summary>Meaning of a badge; picks its color.</summary>
public enum BadgeKind
{
    /// <summary>Gray – counts and neutral labels (Nháp, Đã đóng).</summary>
    Neutral,

    /// <summary>Brand color – highlighted labels (Mới, Thành viên).</summary>
    Primary,

    /// <summary>Green – completed, in stock, active (Hoàn thành, Đang bán).</summary>
    Success,

    /// <summary>Amber – needs attention (Sắp hết hàng, Chờ duyệt).</summary>
    Warning,

    /// <summary>Red – problems and alerts (Hết hàng, Đã hủy, Bị khóa).</summary>
    Danger,

    /// <summary>Blue – information (Chuyển khoản, Đang mở ca).</summary>
    Info,
}

/// <summary>How strongly a badge is filled.</summary>
public enum BadgeStyle
{
    /// <summary>Light tint of the color with colored text – status labels in grids and forms.</summary>
    Soft,

    /// <summary>Solid color with contrasting text – notification counts on the sidebar and tabs.</summary>
    Solid,
}

public readonly record struct BadgeColors(Color Background, Color Foreground);

/// <summary>Badge colors derived from the theme. Text always reaches WCAG AA (4.5:1) on its background.</summary>
public static class BadgePalette
{
    public static BadgeColors Resolve(Theme theme, BadgeKind kind, BadgeStyle style = BadgeStyle.Soft) =>
        Resolve(theme, kind, style, theme?.Colors.Surface ?? default);

    /// <param name="surface">Color the badge sits on (soft tints are blended from it).</param>
    public static BadgeColors Resolve(Theme theme, BadgeKind kind, BadgeStyle style, Color surface)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var accent = AccentOf(theme, kind);

        if (style == BadgeStyle.Solid)
        {
            var on = kind switch
            {
                BadgeKind.Primary => theme.Colors.OnPrimary,
                BadgeKind.Danger => theme.Colors.OnDanger,
                BadgeKind.Neutral => theme.Colors.Surface,
                _ => ColorMath.BestContrast(accent, Color.White, ColorMath.FromHex("#111827")),
            };
            return new BadgeColors(accent, ColorMath.EnsureContrast(on, accent));
        }

        var tint = ColorMath.Blend(surface, accent, theme.IsDark ? 0.24 : 0.14);
        return new BadgeColors(tint, ColorMath.EnsureContrast(accent, tint));
    }

    public static Color AccentOf(Theme theme, BadgeKind kind)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        return kind switch
        {
            BadgeKind.Primary => c.Primary,
            BadgeKind.Success => c.Success,
            BadgeKind.Warning => c.Warning,
            BadgeKind.Danger => c.Danger,
            BadgeKind.Info => c.Info,
            _ => c.TextSecondary,
        };
    }
}

/// <summary>Text helpers for badges.</summary>
public static class BadgeText
{
    /// <summary>Notification count: empty for 0 or less (no badge), "99+" above <paramref name="max"/>.</summary>
    public static string Count(int count, int max = 99)
    {
        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "Giới hạn phải lớn hơn 0.");
        }

        return count <= 0 ? string.Empty : count > max ? $"{max}+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
