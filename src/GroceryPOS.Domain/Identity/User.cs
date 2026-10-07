using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Identity;

public sealed class User : AuditableEntity
{
    private User()
    {
    }

    public User(string username, string fullName, string passwordHash, Role role)
    {
        Username = NormalizeUsername(username);
        Rename(fullName);
        SetPasswordHash(passwordHash, mustChange: false);
        AssignRole(role);
        IsActive = true;
    }

    public string Username { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool MustChangePassword { get; private set; }
    public bool IsActive { get; private set; }
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public int RoleId { get; private set; }
    public Role Role { get; private set; } = null!;

    public static string NormalizeUsername(string? username) =>
        Guard.NotEmpty(username, "user.username", "Tên đăng nhập không được để trống", 50).ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void Rename(string fullName) =>
        FullName = Guard.NotEmpty(fullName, "user.fullname", "Họ tên không được để trống", 100);

    public void AssignRole(Role role)
    {
        Role = role ?? throw new ArgumentNullException(nameof(role));
        RoleId = role.Id;
    }

    public void SetPasswordHash(string passwordHash, bool mustChange)
    {
        PasswordHash = Guard.NotEmpty(passwordHash, "user.password", "Mật khẩu không hợp lệ", 500);
        MustChangePassword = mustChange;
    }

    /// <summary>Records a failed login; returns true when this attempt caused a lockout.</summary>
    public bool RegisterFailedLogin(DateTimeOffset now, LockoutPolicy policy)
    {
        if (IsLockedOut(now))
        {
            return false;
        }

        AccessFailedCount++;
        if (AccessFailedCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        LockoutEnd = now.Add(policy.LockoutDuration);
        AccessFailedCount = 0;
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void Unlock()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
