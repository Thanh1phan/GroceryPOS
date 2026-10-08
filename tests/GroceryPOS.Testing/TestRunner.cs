using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace GroceryPOS.Testing;

/// <summary>
/// Discovers and runs [Fact]/[Theory] methods in an assembly. A new class instance is created per test
/// (like xUnit); <see cref="IDisposable"/> is honored. Returns a process exit code (0 = all passed).
/// Usage: <c>dotnet run --project tests/GroceryPOS.Domain.Tests [-- filter]</c>.
/// </summary>
public static class TestRunner
{
    public static int Run(Assembly assembly, string[] args)
    {
        var filter = args.FirstOrDefault();
        var passed = 0;
        var skipped = 0;
        var failures = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        var testClasses = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in testClasses)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
                .OrderBy(m => m.MetadataToken);

            foreach (var method in methods)
            {
                var fact = method.GetCustomAttribute<FactAttribute>()!;
                var name = $"{type.Name}.{method.Name}";
                if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fact.Skip is not null)
                {
                    skipped++;
                    continue;
                }

                var cases = fact is TheoryAttribute
                    ? method.GetCustomAttributes<InlineDataAttribute>().Select(d => d.Data).ToList()
                    : new List<object?[]> { Array.Empty<object?>() };

                foreach (var data in cases)
                {
                    var display = data.Length == 0 ? name : $"{name}({string.Join(", ", data.Select(d => d ?? "null"))})";
                    var error = RunOne(type, method, data);
                    if (error is null)
                    {
                        passed++;
                    }
                    else
                    {
                        failures.Add($"[FAIL] {display}\n{Indent(error)}");
                    }
                }
            }
        }

        foreach (var failure in failures)
        {
            Console.WriteLine(failure);
        }

        Console.WriteLine($"{assembly.GetName().Name}: {passed} passed, {failures.Count} failed, {skipped} skipped ({stopwatch.ElapsedMilliseconds} ms)");
        return failures.Count == 0 && passed > 0 ? 0 : 1;
    }

    private static string? RunOne(Type type, MethodInfo method, object?[] data)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(type);
            var parameters = method.GetParameters();
            var arguments = parameters.Select((p, i) => Convert(i < data.Length ? data[i] : null, p.ParameterType)).ToArray();
            var result = method.Invoke(instance, arguments);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return Describe(ex.InnerException);
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
        finally
        {
            (instance as IDisposable)?.Dispose();
        }
    }

    /// <summary>Converts attribute data to the parameter type (attributes cannot hold decimals).</summary>
    private static object? Convert(object? value, Type target)
    {
        if (value is null || target.IsInstanceOfType(value))
        {
            return value;
        }

        var underlying = Nullable.GetUnderlyingType(target) ?? target;
        if (underlying.IsEnum)
        {
            return Enum.ToObject(underlying, value);
        }

        return System.Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Describe(Exception ex) =>
        ex is AssertException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(l => "    " + l));
}
