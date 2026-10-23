namespace GroceryPOS.Application.Users;

/// <summary>A row of the user list (người dùng).</summary>
public sealed record UserDto(
    int Id,
    string Username,
    string FullName,
    int RoleId,
    string RoleName,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    bool IsCurrentUser)
{
    /// <summary>Vietnamese status shown in the list: stopped accounts first, then temporary lockouts.</summary>
    public string Status => !IsActive ? "Ngừng hoạt động" : IsLockedOut ? "Tạm khóa" : MustChangePassword ? "Chờ đổi mật khẩu" : "Đang hoạt động";
}

/// <summary>A role that can be picked in the user dialog. <see cref="CanAssign"/> is false for roles with permissions the signed-in user lacks.</summary>
public sealed record RoleOptionDto(int Id, string Name, bool CanAssign);

/// <summary>
/// What the add-user dialog submits. <see cref="Password"/> is a temporary password the new user must change at the
/// first sign-in. <see cref="ConfirmPassword"/> is checked by the validator only.
/// </summary>
public sealed record CreateUserRequest(string? Username, string? FullName, int RoleId, string? Password, string? ConfirmPassword);

/// <summary>What the edit-user dialog submits (the username never changes).</summary>
public sealed record UpdateUserRequest(string? FullName, int RoleId);

/// <summary>What the reset-password dialog submits. <see cref="ConfirmPassword"/> is checked by the validator only.</summary>
public sealed record ResetPasswordRequest(string? NewPassword, string? ConfirmPassword);

/// <summary>A role with its permission set, for the role list and the permission matrix.</summary>
public sealed record RoleDto(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    bool PermissionsLocked,
    IReadOnlySet<string> Permissions,
    int UserCount);

/// <summary>What the add/edit-role dialog submits. On system roles the name cannot change.</summary>
public sealed record SaveRoleRequest(string? Name, string? Description, IReadOnlyCollection<string> Permissions);

/// <summary>One column of the permission matrix: the full desired permission set of a role.</summary>
public sealed record RolePermissionsUpdate(int RoleId, IReadOnlyCollection<string> Permissions);

/// <summary>A permission in the matrix: stable key and Vietnamese label.</summary>
public sealed record PermissionItem(string Key, string DisplayName);

/// <summary>A titled group of permissions (Hệ thống, Bán hàng…).</summary>
public sealed record PermissionGroupDto(string Title, IReadOnlyList<PermissionItem> Items);
