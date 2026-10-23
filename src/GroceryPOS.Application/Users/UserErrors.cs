using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Users;

public static class UserErrors
{
    public static readonly Error DuplicateUsername = Error.Conflict("Tên đăng nhập đã tồn tại.");

    public static readonly Error InvalidUsername = Error.Validation(
        "Tên đăng nhập dài 3–50 ký tự, chỉ gồm chữ không dấu, số và dấu . _ -");

    public static readonly Error SelfDeactivate = Error.Conflict("Bạn không thể tự ngừng hoạt động tài khoản đang đăng nhập.");

    public static readonly Error SelfRoleChange = Error.Conflict("Bạn không thể tự đổi vai trò của mình.");

    public static readonly Error SelfReset = Error.Conflict("Hãy dùng chức năng \"Đổi mật khẩu\" để đổi mật khẩu của chính bạn.");

    public static readonly Error LastAdministrator = Error.Conflict(
        "Phải còn ít nhất một tài khoản đang hoạt động có quyền quản lý vai trò & phân quyền.");

    public static readonly Error RoleOutOfReach = Error.Forbidden(
        "Vai trò này có quyền mà bạn không có, nên bạn không thể gán hoặc quản lý tài khoản thuộc vai trò đó.");

    public static readonly Error DuplicateRoleName = Error.Conflict("Tên vai trò đã tồn tại.");

    public static readonly Error SystemRoleRename = Error.Conflict("Không thể đổi tên vai trò hệ thống.");

    public static readonly Error SystemRoleDelete = Error.Conflict("Không thể xóa vai trò hệ thống.");

    public static readonly Error AdminPermissionsLocked = Error.Conflict("Vai trò Admin luôn có toàn bộ quyền, không thể thay đổi.");

    public static Error UserNotFound(int id) => Error.NotFound($"Không tìm thấy người dùng (mã #{id}).");

    public static Error RoleNotFound(int id) => Error.NotFound($"Không tìm thấy vai trò (mã #{id}).");

    public static Error UnknownPermission(string key) => Error.Validation($"Quyền không hợp lệ: {key}");

    public static Error RoleInUse(int userCount) =>
        Error.Conflict($"Vai trò đang được gán cho {userCount} người dùng. Hãy chuyển họ sang vai trò khác trước khi xóa.");
}
