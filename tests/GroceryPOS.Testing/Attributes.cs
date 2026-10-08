namespace Xunit;

/// <summary>Marks a parameterless test method.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class FactAttribute : Attribute
{
    public string? Skip { get; set; }

    public string? DisplayName { get; set; }
}

/// <summary>Marks a data-driven test method; data comes from <see cref="InlineDataAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : FactAttribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute : Attribute
{
    public InlineDataAttribute(params object?[] data)
    {
        Data = data;
    }

    public object?[] Data { get; }
}
