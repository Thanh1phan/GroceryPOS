namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>A value of a row that the grid can sort on and/or search in.</summary>
/// <param name="Key">Column key (data property name).</param>
/// <param name="Getter">Raw value used for sorting.</param>
/// <param name="Searchable">Whether the quick search looks at this value.</param>
/// <param name="SearchText">Text matched by the search; defaults to the value's string form (a formatted price, a code…).</param>
public sealed record GridField<T>(string Key, Func<T, object?> Getter, bool Searchable = true, Func<T, string?>? SearchText = null)
{
    internal string? TextOf(T item) => SearchText is not null ? SearchText(item) : Getter(item)?.ToString();
}
