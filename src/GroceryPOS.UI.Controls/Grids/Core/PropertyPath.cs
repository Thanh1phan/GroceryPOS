using System.Collections.Concurrent;
using System.ComponentModel;

namespace GroceryPOS.UI.Controls.Grids.Core;

/// <summary>
/// Reads a (possibly dotted) property path such as <c>"Category.Name"</c> from any object through
/// <see cref="TypeDescriptor"/>, caching the descriptors per runtime type. A missing property or a null
/// in the middle of the path gives null instead of throwing, so a column never breaks a whole page.
/// </summary>
public static class PropertyPath
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyDescriptor?> Cache = new();

    public static object? GetValue(object? item, string? path)
    {
        if (item is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var current = item;
        foreach (var name in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            var descriptor = Cache.GetOrAdd(
                (current.GetType(), name),
                key => TypeDescriptor.GetProperties(key.Type).Find(key.Name, ignoreCase: false));
            if (descriptor is null)
            {
                return null;
            }

            current = descriptor.GetValue(current);
        }

        return current;
    }
}
