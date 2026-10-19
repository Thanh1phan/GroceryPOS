using System.Globalization;
using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>
/// In-memory search, sort and paging for list screens whose data fits in memory (categories, products,
/// suppliers…). Search is accent-insensitive and every word must occur in some searchable field;
/// sorting is stable, puts empty values first in ascending order and compares text the Vietnamese way.
/// </summary>
public sealed class GridDataView<T>
{
    private static readonly StringComparer VietnameseText =
        StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), CompareOptions.IgnoreCase);

    private readonly Dictionary<string, GridField<T>> _fields = new(StringComparer.Ordinal);
    private readonly List<GridField<T>> _searchFields = [];
    private IReadOnlyList<T> _items = [];
    private string[] _searchIndex = [];

    public GridDataView(IEnumerable<GridField<T>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (var field in fields)
        {
            if (!_fields.TryAdd(field.Key, field))
            {
                throw new ArgumentException($"Trùng khóa cột '{field.Key}'.", nameof(fields));
            }

            if (field.Searchable)
            {
                _searchFields.Add(field);
            }
        }
    }

    public IReadOnlyCollection<string> Keys => _fields.Keys;

    public IReadOnlyList<T> Items => _items;

    public bool CanSort(string key) => _fields.ContainsKey(key);

    /// <summary>Replaces the rows and pre-computes their folded search text.</summary>
    public void SetItems(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToList();
        _searchIndex = _items
            .Select(item => TextMatch.Fold(string.Join(' ', _searchFields.Select(f => f.TextOf(item)))))
            .ToArray();
    }

    public GridPage<T> Execute(GridQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var words = TextMatch.Fold(query.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<T> rows = words.Length == 0
            ? _items
            : _items.Where((_, i) => words.All(w => _searchIndex[i].Contains(w, StringComparison.Ordinal)));

        if (query.IsSorted && _fields.TryGetValue(query.SortKey!, out var field))
        {
            var comparer = Comparer<object?>.Create(CompareValues);
            rows = query.SortDirection == SortDirection.Descending
                ? rows.OrderByDescending(field.Getter, comparer)
                : rows.OrderBy(field.Getter, comparer);
        }

        var matched = rows as IReadOnlyList<T> ?? rows.ToList();
        var pageCount = Math.Max(1, (matched.Count + query.PageSize - 1) / query.PageSize);
        var pageIndex = Math.Clamp(query.PageIndex, 0, pageCount - 1);
        var pageItems = matched.Skip(pageIndex * query.PageSize).Take(query.PageSize).ToList();
        return new GridPage<T>(pageItems, matched.Count, pageIndex, query.PageSize);
    }

    /// <summary>Nulls first; text with Vietnamese collation; numbers of different types by value; otherwise <see cref="IComparable"/>.</summary>
    internal static int CompareValues(object? x, object? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        if (x is string sx && y is string sy)
        {
            return VietnameseText.Compare(sx, sy);
        }

        if (IsNumber(x) && IsNumber(y) && x.GetType() != y.GetType())
        {
            return x is float or double || y is float or double
                ? Convert.ToDouble(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(y, CultureInfo.InvariantCulture))
                : Convert.ToDecimal(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(y, CultureInfo.InvariantCulture));
        }

        if (x is IComparable cx && x.GetType() == y.GetType())
        {
            return cx.CompareTo(y);
        }

        return VietnameseText.Compare(x.ToString(), y.ToString());
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double;
}
