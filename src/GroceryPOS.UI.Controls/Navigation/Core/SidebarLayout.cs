using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Navigation.Core;

/// <summary>Vertical position of one sidebar row (relative to the top of the scrollable list).</summary>
public readonly record struct NavRowBounds(int Index, int Top, int Height)
{
    public int Bottom => Top + Height;
}

/// <summary>Row metrics of the sidebar in pixels (already DPI-scaled by the caller).</summary>
public readonly record struct SidebarMetrics(int ItemHeight, int HeaderHeight, int SectionGap, int CollapsedSeparator);

/// <summary>Stacks sidebar rows and answers scrolling/hit-test questions. Pure.</summary>
public sealed class SidebarLayout
{
    private readonly NavRowBounds[] _bounds;

    private SidebarLayout(NavRowBounds[] bounds, int contentHeight)
    {
        _bounds = bounds;
        ContentHeight = contentHeight;
    }

    public IReadOnlyList<NavRowBounds> Rows => _bounds;

    public int ContentHeight { get; }

    /// <param name="rows">Rows from <see cref="NavMenu.Flatten"/>.</param>
    /// <param name="collapsed">Collapsed sidebars have no headers; a thin separator marks where a new section starts.</param>
    public static SidebarLayout Arrange(IReadOnlyList<NavRow> rows, SidebarMetrics metrics, bool collapsed)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var bounds = new NavRowBounds[rows.Count];
        var y = 0;
        NavSection? previousSection = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var newSection = previousSection is not null && !ReferenceEquals(row.Section, previousSection);
            if (row.Kind == NavRowKind.Header)
            {
                if (i > 0)
                {
                    y += metrics.SectionGap;
                }

                bounds[i] = new NavRowBounds(i, y, metrics.HeaderHeight);
                y += metrics.HeaderHeight;
            }
            else
            {
                if (collapsed && newSection)
                {
                    y += metrics.CollapsedSeparator;
                }

                bounds[i] = new NavRowBounds(i, y, metrics.ItemHeight);
                y += metrics.ItemHeight;
            }

            previousSection = row.Section;
        }

        return new SidebarLayout(bounds, y);
    }

    /// <summary>Row under the point (content coordinates), or -1.</summary>
    public int HitTest(int y)
    {
        foreach (var row in _bounds)
        {
            if (y >= row.Top && y < row.Bottom)
            {
                return row.Index;
            }
        }

        return -1;
    }

    public static int MaxScroll(int contentHeight, int viewportHeight) => Math.Max(0, contentHeight - Math.Max(0, viewportHeight));

    /// <summary>Smallest scroll change that brings row <paramref name="index"/> fully into view.</summary>
    public int ScrollIntoView(int index, int scroll, int viewportHeight)
    {
        if (index < 0 || index >= _bounds.Length)
        {
            return Math.Clamp(scroll, 0, MaxScroll(ContentHeight, viewportHeight));
        }

        var row = _bounds[index];
        if (row.Top < scroll)
        {
            scroll = row.Top;
        }
        else if (row.Bottom > scroll + viewportHeight)
        {
            scroll = row.Bottom - viewportHeight;
        }

        return Math.Clamp(scroll, 0, MaxScroll(ContentHeight, viewportHeight));
    }
}

public readonly record struct SidebarColors(
    Color Background,
    Color Border,
    Color BrandText,
    Color HeaderText,
    Color ItemText,
    Color ItemIcon,
    Color HoverBackground,
    Color SelectedBackground,
    Color SelectedText,
    Color Indicator,
    Color ShortcutText,
    Color Separator);

/// <summary>Sidebar colors: selection is a brand tint with an accent bar, so the page in use stands out in both themes.</summary>
public static class SidebarPalette
{
    public static SidebarColors Resolve(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var c = theme.Colors;
        var background = c.Surface;
        var hover = c.SurfaceAlt;
        var selected = ColorMath.Blend(background, c.Primary, theme.IsDark ? 0.22 : 0.12);
        return new SidebarColors(
            Background: background,
            Border: ColorMath.Blend(background, c.Border, 0.7),
            BrandText: ColorMath.EnsureContrast(c.Primary, background),
            HeaderText: ColorMath.EnsureContrast(c.TextMuted, background),
            ItemText: ColorMath.EnsureContrast(c.TextSecondary, hover),
            ItemIcon: ColorMath.EnsureContrast(c.TextSecondary, hover, 3.0),
            HoverBackground: hover,
            SelectedBackground: selected,
            SelectedText: ColorMath.EnsureContrast(theme.IsDark ? c.TextPrimary : ColorMath.Darken(c.Primary, 0.25), selected),
            Indicator: c.Primary,
            ShortcutText: ColorMath.EnsureContrast(c.TextMuted, hover, 3.0),
            Separator: ColorMath.Blend(background, c.Border, 0.6));
    }
}
