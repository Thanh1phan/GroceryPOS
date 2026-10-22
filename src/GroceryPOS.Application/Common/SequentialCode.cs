using System.Globalization;

namespace GroceryPOS.Application.Common;

/// <summary>
/// Running master-data codes such as <c>KH000001</c> or <c>NCC0001</c>: prefix + zero-padded number.
/// Codes typed by hand that merely start with the prefix (e.g. "NCC-VINAMILK") are ignored when picking the next number.
/// </summary>
public static class SequentialCode
{
    public static string Next(string prefix, int width, IEnumerable<string> existingCodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var normalizedPrefix = prefix.Trim().ToUpperInvariant();

        var max = 0L;
        foreach (var code in existingCodes)
        {
            if (code is null || !code.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = code.AsSpan(normalizedPrefix.Length);
            if (suffix.Length > 0
                && long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > max)
            {
                max = number;
            }
        }

        return normalizedPrefix + (max + 1).ToString(new string('0', width), CultureInfo.InvariantCulture);
    }
}
