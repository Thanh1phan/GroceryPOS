using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Users;

/// <summary>Rules shared by user and role management (username format, privilege ceiling, last administrator).</summary>
public static class UserRules
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 50;

    /// <summary>3–50 characters: unaccented letters, digits, '.', '_' and '-' (it is typed at the login screen).</summary>
    public static bool IsValidUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var trimmed = username.Trim();
        return trimmed.Length is >= MinUsernameLength and <= MaxUsernameLength
               && trimmed.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-');
    }

    /// <summary>
    /// Privilege ceiling: a user may only assign a role (or manage accounts in it) when they themselves hold every
    /// permission of that role. A manager can therefore never create an Admin or promote themselves.
    /// </summary>
    public static bool CanReach(ICurrentUser actor, Role role) => role.PermissionKeys.All(actor.HasPermission);

    /// <summary>An administrator is an active account whose role may manage roles and permissions.</summary>
    public static bool IsAdministrator(User user) => user.IsActive && user.Role.HasPermission(Permissions.RolesManage);
}
