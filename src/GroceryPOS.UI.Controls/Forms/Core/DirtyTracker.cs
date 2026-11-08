namespace GroceryPOS.UI.Controls.Forms.Core;

/// <summary>
/// Remembers the values an edit dialog opened with and tells whether the user has changed any of them, so
/// closing with unsaved changes can ask "Bỏ các thay đổi chưa lưu?". Values are read through delegates and
/// compared as text; <c>null</c>, empty and whitespace-only are the same, and surrounding spaces are ignored
/// (the save trims them anyway).
/// </summary>
public sealed class DirtyTracker
{
    private readonly Dictionary<string, Func<string?>> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>Keys in the order they were tracked.</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <summary>
    /// Starts tracking a value. Its current value becomes the baseline; tracking the same key again replaces
    /// the reader and re-baselines it.
    /// </summary>
    public void Track(string key, Func<string?> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(reader);
        if (!_readers.ContainsKey(key))
        {
            _order.Add(key);
        }

        _readers[key] = reader;
        _baseline[key] = Normalize(reader());
    }

    /// <summary>Stops tracking a value (e.g. a field that became read-only).</summary>
    public bool Untrack(string key)
    {
        if (!_readers.Remove(key))
        {
            return false;
        }

        _baseline.Remove(key);
        _order.Remove(key);
        return true;
    }

    /// <summary>Takes the current values as the new baseline (after the dialog finished loading, or after saving).</summary>
    public void AcceptChanges()
    {
        foreach (var (key, reader) in _readers)
        {
            _baseline[key] = Normalize(reader());
        }
    }

    /// <summary>Takes the current value of one key as its baseline (a value filled in by the program, such as a suggested code).</summary>
    public void AcceptChange(string key)
    {
        if (_readers.TryGetValue(key, out var reader))
        {
            _baseline[key] = Normalize(reader());
        }
    }

    public bool IsChanged(string key) =>
        _readers.TryGetValue(key, out var reader) && !string.Equals(_baseline[key], Normalize(reader()), StringComparison.Ordinal);

    public bool IsDirty => _order.Any(IsChanged);

    /// <summary>Changed keys in tracking order.</summary>
    public IReadOnlyList<string> ChangedKeys => _order.Where(IsChanged).ToList();

    internal static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
