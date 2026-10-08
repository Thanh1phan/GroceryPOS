using System.Collections;

namespace Xunit;

/// <summary>Thrown when an assertion fails.</summary>
public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message)
    {
    }
}

/// <summary>Subset of xUnit's Assert API used by the GroceryPOS tests.</summary>
public static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        // Like xUnit, collections (other than strings) are compared element by element.
        if (expected is IEnumerable e && actual is IEnumerable a && expected is not string)
        {
            Equal(e.Cast<object?>(), a.Cast<object?>());
            return;
        }

        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertException($"Assert.Equal() failure\n  Expected: {Show(expected)}\n  Actual:   {Show(actual)}");
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (!e.SequenceEqual(a))
        {
            throw new AssertException($"Assert.Equal() failure (sequence)\n  Expected: [{string.Join(", ", e.Select(x => Show(x)))}]\n  Actual:   [{string.Join(", ", a.Select(x => Show(x)))}]");
        }
    }

    public static void NotEqual<T>(T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertException($"Assert.NotEqual() failure: both are {Show(actual)}");
        }
    }

    public static void True(bool condition, string? userMessage = null)
    {
        if (!condition)
        {
            throw new AssertException(userMessage ?? "Assert.True() failure");
        }
    }

    public static void False(bool condition, string? userMessage = null)
    {
        if (condition)
        {
            throw new AssertException(userMessage ?? "Assert.False() failure");
        }
    }

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new AssertException($"Assert.Null() failure: {Show(value)}");
        }
    }

    public static T NotNull<T>(T? value) where T : class
    {
        return value ?? throw new AssertException("Assert.NotNull() failure");
    }

    public static void Empty(IEnumerable collection)
    {
        if (collection.GetEnumerator().MoveNext())
        {
            throw new AssertException("Assert.Empty() failure: collection is not empty");
        }
    }

    public static void NotEmpty(IEnumerable collection)
    {
        if (!collection.GetEnumerator().MoveNext())
        {
            throw new AssertException("Assert.NotEmpty() failure: collection is empty");
        }
    }

    public static T Single<T>(IEnumerable<T> collection)
    {
        var list = collection.Take(2).ToList();
        if (list.Count != 1)
        {
            throw new AssertException($"Assert.Single() failure: expected 1 element, found {(list.Count == 0 ? "0" : "more than 1")}");
        }

        return list[0];
    }

    public static void Contains<T>(T expected, IEnumerable<T> collection)
    {
        if (!collection.Contains(expected))
        {
            throw new AssertException($"Assert.Contains() failure: {Show(expected)} not found");
        }
    }

    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection)
    {
        if (collection.Contains(expected))
        {
            throw new AssertException($"Assert.DoesNotContain() failure: {Show(expected)} found");
        }
    }

    public static void Contains(string expectedSubstring, string? actual)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new AssertException($"Assert.Contains() failure: \"{expectedSubstring}\" not found in {Show(actual)}");
        }
    }

    public static T IsType<T>(object? value)
    {
        if (value is null || value.GetType() != typeof(T))
        {
            throw new AssertException($"Assert.IsType() failure\n  Expected: {typeof(T).Name}\n  Actual:   {value?.GetType().Name ?? "null"}");
        }

        return (T)value;
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex.GetType() == typeof(T)
                ? (T)ex
                : throw new AssertException($"Assert.Throws() failure\n  Expected: {typeof(T).Name}\n  Actual:   {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertException($"Assert.Throws() failure: no exception thrown, expected {typeof(T).Name}");
    }

    public static T Throws<T>(Func<object?> func) where T : Exception => Throws<T>(() => { _ = func(); });

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            return ex.GetType() == typeof(T)
                ? (T)ex
                : throw new AssertException($"Assert.ThrowsAsync() failure\n  Expected: {typeof(T).Name}\n  Actual:   {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertException($"Assert.ThrowsAsync() failure: no exception thrown, expected {typeof(T).Name}");
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? string.Empty,
    };
}
