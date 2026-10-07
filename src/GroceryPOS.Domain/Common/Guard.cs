namespace GroceryPOS.Domain.Common;

/// <summary>Small helpers for enforcing invariants inside entities.</summary>
public static class Guard
{
    public static string NotEmpty(string? value, string code, string message, int maxLength = 200)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainException(code, message);
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainException(code, $"{message} (tối đa {maxLength} ký tự)");
        }

        return trimmed;
    }

    public static string? Optional(string? value, int maxLength = 500)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    public static decimal NotNegative(decimal value, string code, string message)
    {
        if (value < 0)
        {
            throw new DomainException(code, message);
        }

        return value;
    }
}
