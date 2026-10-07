using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Authentication;

public static class AuthErrors
{
    /// <summary>Deliberately vague so it does not reveal whether the username exists.</summary>
    public static readonly Error InvalidCredentials = new("auth.invalid", "Tên đăng nhập hoặc mật khẩu không đúng.");

    public static readonly Error Inactive = new("auth.inactive", "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");

    public static readonly Error NotAuthenticated = new("auth.required", "Bạn chưa đăng nhập.");

    public static readonly Error WrongCurrentPassword = new("auth.wrong_password", "Mật khẩu hiện tại không đúng.");

    public static readonly Error SamePassword = new("auth.same_password", "Mật khẩu mới phải khác mật khẩu hiện tại.");

    public static Error LockedOut(DateTimeOffset until, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return new("auth.locked", $"Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau {minutes} phút.");
    }
}
