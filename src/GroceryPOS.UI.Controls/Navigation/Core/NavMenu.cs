namespace GroceryPOS.UI.Controls.Navigation.Core;

/// <summary>
/// One destination in the sidebar.
/// </summary>
/// <param name="Key">Stable identifier the shell uses to open the screen ("pos", "products"…).</param>
/// <param name="Text">Vietnamese caption ("Bán hàng").</param>
/// <param name="Glyph">Icon from <c>Icons.Glyphs</c>.</param>
/// <param name="Permissions">The user needs at least one of these; empty means every signed-in user.</param>
/// <param name="Shortcut">Optional key hint shown on the right ("F2").</param>
public sealed record NavItem(string Key, string Text, string? Glyph = null, IReadOnlyList<string>? Permissions = null, string? Shortcut = null)
{
    public IReadOnlyList<string> Permissions { get; init; } = Permissions ?? Array.Empty<string>();

    public bool IsAllowed(Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(hasPermission);
        return Permissions.Count == 0 || Permissions.Any(hasPermission);
    }
}

/// <summary>A titled group of items ("Bán hàng", "Kho hàng", "Báo cáo"…). A null title draws no header.</summary>
public sealed record NavSection(string? Title, IReadOnlyList<NavItem> Items);

public enum NavRowKind
{
    Header,
    Item,
}

/// <summary>A row of the flattened menu: a section header or an item.</summary>
public readonly record struct NavRow(NavRowKind Kind, NavSection Section, NavItem? Item)
{
    public bool IsItem => Kind == NavRowKind.Item;
}

/// <summary>The sidebar menu: sections of items, filtered by the signed-in user's permissions.</summary>
public sealed class NavMenu
{
    public NavMenu(IEnumerable<NavSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Sections = sections.ToList();

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Sections.SelectMany(s => s.Items))
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                throw new ArgumentException("Mục menu phải có mã.", nameof(sections));
            }

            if (!keys.Add(item.Key))
            {
                throw new ArgumentException($"Mã menu bị trùng: {item.Key}.", nameof(sections));
            }
        }
    }

    public static NavMenu Empty { get; } = new(Array.Empty<NavSection>());

    public IReadOnlyList<NavSection> Sections { get; }

    public IEnumerable<NavItem> Items => Sections.SelectMany(s => s.Items);

    public NavItem? Find(string? key) =>
        key is null ? null : Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Keeps only the items the user may open; sections left empty disappear.</summary>
    public NavMenu Filter(Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(hasPermission);
        return new NavMenu(Sections
            .Select(s => s with { Items = s.Items.Where(i => i.IsAllowed(hasPermission)).ToList() })
            .Where(s => s.Items.Count > 0));
    }

    /// <summary>Rows in display order; headers are left out when <paramref name="includeHeaders"/> is false (collapsed sidebar).</summary>
    public IReadOnlyList<NavRow> Flatten(bool includeHeaders = true)
    {
        var rows = new List<NavRow>();
        foreach (var section in Sections)
        {
            if (includeHeaders && !string.IsNullOrWhiteSpace(section.Title))
            {
                rows.Add(new NavRow(NavRowKind.Header, section, null));
            }

            rows.AddRange(section.Items.Select(i => new NavRow(NavRowKind.Item, section, i)));
        }

        return rows;
    }
}

/// <summary>Keyboard movement over the item rows of a flattened menu (headers are skipped).</summary>
public static class NavCursor
{
    /// <summary>Next item row in <paramref name="direction"/> (+1 down, -1 up), wrapping around; -1 when there is no item.</summary>
    public static int Move(IReadOnlyList<NavRow> rows, int current, int direction)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || !rows.Any(r => r.IsItem))
        {
            return -1;
        }

        direction = direction >= 0 ? 1 : -1;
        var index = current < 0 || current >= rows.Count ? (direction > 0 ? -1 : rows.Count) : current;
        for (var step = 0; step < rows.Count; step++)
        {
            index = ((index + direction) % rows.Count + rows.Count) % rows.Count;
            if (rows[index].IsItem)
            {
                return index;
            }
        }

        return -1;
    }

    public static int First(IReadOnlyList<NavRow> rows) => Move(rows, -1, +1);

    public static int Last(IReadOnlyList<NavRow> rows) => Move(rows, -1, -1);

    public static int IndexOf(IReadOnlyList<NavRow> rows, string? key)
    {
        ArgumentNullException.ThrowIfNull(rows);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Item is { } item && string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
