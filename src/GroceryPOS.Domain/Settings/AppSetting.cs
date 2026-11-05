using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Settings;

/// <summary>
/// One store-wide setting stored in the database as a key/value pair (e.g. <c>store.name</c>, <c>loyalty.vnd_per_point</c>),
/// so every counter sharing the database uses the same store profile, receipt and loyalty rules.
/// Values are invariant-culture text; the application layer knows how to read each key.
/// </summary>
public sealed class AppSetting : AuditableEntity
{
    public const int KeyMaxLength = 100;
    public const int ValueMaxLength = 1000;

    private AppSetting()
    {
    }

    public AppSetting(string key, string? value)
    {
        Key = NormalizeKey(key);
        SetValue(value);
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    /// <summary>Replaces the value; returns <c>false</c> when it did not change (nothing to save).</summary>
    public bool SetValue(string? value)
    {
        var next = value ?? string.Empty;
        if (next.Length > ValueMaxLength)
        {
            throw new DomainException("setting.value", $"Giá trị cấu hình tối đa {ValueMaxLength} ký tự.");
        }

        if (string.Equals(next, Value, StringComparison.Ordinal))
        {
            return false;
        }

        Value = next;
        return true;
    }

    /// <summary>Keys are lower-case <c>area.name</c> identifiers: letters, digits, '.', '_' only.</summary>
    public static string NormalizeKey(string? key)
    {
        var value = Guard.NotEmpty(key, "setting.key", "Thiếu khóa cấu hình", KeyMaxLength).ToLowerInvariant();
        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_'))
        {
            throw new DomainException("setting.key", $"Khóa cấu hình không hợp lệ: {value}");
        }

        return value;
    }
}
