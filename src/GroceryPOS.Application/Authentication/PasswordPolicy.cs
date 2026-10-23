namespace GroceryPOS.Application.Authentication;

/// <summary>Minimum password requirements: at least 8 characters with both letters and digits.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Mật khẩu phải có ít nhất {MinLength} ký tự.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "Mật khẩu phải chứa cả chữ và số.";
        }

        return null;
    }

    // No look-alike characters (0/O, 1/l/I) so the password can be read out or written on paper.
    private const string Letters = "abcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>
    /// A random temporary password for a new account or a reset (6 letters + 4 digits, e.g. <c>hkmpqa4827</c>).
    /// It satisfies <see cref="Validate"/>; the user must replace it at the next sign-in.
    /// </summary>
    public static string GenerateTemporary()
    {
        Span<char> chars = stackalloc char[10];
        for (var i = 0; i < 6; i++)
        {
            chars[i] = Letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Letters.Length)];
        }

        for (var i = 6; i < 10; i++)
        {
            chars[i] = Digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Digits.Length)];
        }

        return new string(chars);
    }
}
