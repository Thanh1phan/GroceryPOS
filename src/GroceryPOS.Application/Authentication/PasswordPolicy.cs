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
}
