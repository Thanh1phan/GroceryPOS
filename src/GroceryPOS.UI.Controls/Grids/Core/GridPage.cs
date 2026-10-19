using System.Globalization;

namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>One page of grid rows plus the numbers the footer shows.</summary>
public sealed class GridPage<T>
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public GridPage(IReadOnlyList<T> items, int totalCount, int pageIndex, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Số dòng mỗi trang phải lớn hơn 0.");
        }

        if (totalCount < items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Tổng số dòng nhỏ hơn số dòng của trang.");
        }

        Items = items;
        TotalCount = totalCount;
        PageSize = pageSize;
        PageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
    }

    public static GridPage<T> Empty(int pageSize = GridQuery.DefaultPageSize) => new([], 0, 0, pageSize);

    public IReadOnlyList<T> Items { get; }

    /// <summary>Rows matching the search, over all pages.</summary>
    public int TotalCount { get; }

    public int PageIndex { get; }

    public int PageSize { get; }

    /// <summary>At least 1, so "Trang 1/1" is shown for an empty result.</summary>
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

    public bool HasPrevious => PageIndex > 0;

    public bool HasNext => PageIndex < PageCount - 1;

    /// <summary>1-based number of the first row on this page (0 when empty).</summary>
    public int FirstItemNumber => Items.Count == 0 ? 0 : (PageIndex * PageSize) + 1;

    public int LastItemNumber => Items.Count == 0 ? 0 : (PageIndex * PageSize) + Items.Count;

    /// <summary>Footer text: "Hiển thị 21–40 / 1.235 dòng" or "Không có dữ liệu".</summary>
    public string Summary => TotalCount == 0
        ? "Không có dữ liệu"
        : $"Hiển thị {FirstItemNumber.ToString("N0", Vietnamese)}–{LastItemNumber.ToString("N0", Vietnamese)} / {TotalCount.ToString("N0", Vietnamese)} dòng";

    public GridPage<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new GridPage<TResult>(Items.Select(selector).ToList(), TotalCount, PageIndex, PageSize);
    }
}
