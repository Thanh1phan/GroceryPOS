using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Partners;

/// <summary>Vietnamese phone number normalization: strips spaces/dots, converts +84 to 0.</summary>
public static class PhoneNumber
{
    public static string? NormalizeOptional(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        if (digits.StartsWith("+84", StringComparison.Ordinal))
        {
            digits = "0" + digits[3..];
        }
        else if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
        {
            digits = "0" + digits[2..];
        }

        if (digits.Length is < 9 or > 11 || !digits.All(char.IsAsciiDigit) || digits[0] != '0')
        {
            throw new DomainException("phone.invalid", "Số điện thoại không hợp lệ.");
        }

        return digits;
    }
}
