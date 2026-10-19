namespace GroceryPOS.UI.Controls.Grids.Core;

public enum SortDirection
{
    None,
    Ascending,
    Descending,
}

/// <summary>What a grid asks its data source for: search text, sort column and the page to show.</summary>
public sealed record GridQuery
{
    public const int DefaultPageSize = 20;

    /// <summary>Page sizes offered in the grid footer.</summary>
    public static IReadOnlyList<int> PageSizeOptions { get; } = [20, 50, 100, 200];

    public string Search { get; init; } = string.Empty;

    /// <summary>Key of the sorted column (its data property name); null keeps the source order.</summary>
    public string? SortKey { get; init; }

    public SortDirection SortDirection { get; init; } = SortDirection.None;

    /// <summary>Zero-based page index. Sources clamp it to the last page.</summary>
    public int PageIndex { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    public bool IsSorted => SortKey is not null && SortDirection != SortDirection.None;

    /// <summary>Same query with a new search text; goes back to the first page.</summary>
    public GridQuery WithSearch(string? search) => this with { Search = search?.Trim() ?? string.Empty, PageIndex = 0 };

    /// <summary>
    /// Header click: a new column sorts ascending, the same column cycles ascending → descending → unsorted.
    /// Goes back to the first page.
    /// </summary>
    public GridQuery ToggleSort(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var next = SortKey != key
            ? SortDirection.Ascending
            : SortDirection switch
            {
                SortDirection.None => SortDirection.Ascending,
                SortDirection.Ascending => SortDirection.Descending,
                _ => SortDirection.None,
            };
        return this with
        {
            SortKey = next == SortDirection.None ? null : key,
            SortDirection = next,
            PageIndex = 0,
        };
    }

    public GridQuery WithPage(int pageIndex) => this with { PageIndex = Math.Max(0, pageIndex) };

    /// <summary>New page size, keeping the first visible row on screen.</summary>
    public GridQuery WithPageSize(int pageSize)
    {
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Số dòng mỗi trang phải lớn hơn 0.");
        }

        var firstRow = PageIndex * PageSize;
        return this with { PageSize = pageSize, PageIndex = firstRow / pageSize };
    }
}
