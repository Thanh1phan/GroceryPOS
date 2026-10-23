using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Users;

public interface IUserManagementService
{
    /// <summary>All accounts, active first, by full name. Requires <c>users.view</c> or <c>users.manage</c>.</summary>
    Task<Result<IReadOnlyList<UserDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Roles for the user dialog, flagging those the signed-in user may assign. Requires <c>users.manage</c>.</summary>
    Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account with a temporary password that must be changed at the first sign-in.
    /// Requires <c>users.manage</c> and every permission of the chosen role.
    /// </summary>
    Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes the full name and/or role. Nobody can change their own role. Requires <c>users.manage</c>.</summary>
    Task<Result<UserDto>> UpdateAsync(int userId, UpdateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets a temporary password (must be changed at next sign-in) and lifts a lockout. Requires <c>users.manage</c>.</summary>
    Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Lifts a lockout caused by failed sign-ins. Requires <c>users.manage</c>.</summary>
    Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>users.manage</c>. Refused for the signed-in user and for the last administrator.</summary>
    Task<Result> DeactivateAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>users.manage</c>.</summary>
    Task<Result> ActivateAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// User accounts (người dùng): create with a temporary password, edit name/role, reset password, unlock,
/// activate/deactivate. Accounts are never deleted – the audit trail and documents refer to them.
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private const string DeniedMessage = "Bạn không có quyền quản lý người dùng.";

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;

    public UserManagementService(
        IUserRepository users,
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        IPasswordHasher hasher,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<UserDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (!_currentUser.HasPermission(Permissions.UsersView) && !_currentUser.HasPermission(Permissions.UsersManage))
        {
            return Error.Forbidden("Bạn không có quyền xem người dùng.");
        }

        var users = await _users.ListAsync(cancellationToken);
        IReadOnlyList<UserDto> items = users
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.FullName, Names)
            .Select(ToDto)
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<IReadOnlyList<RoleOptionDto>>> ListRoleOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var roles = await _roles.ListAsync(cancellationToken);
        IReadOnlyList<RoleOptionDto> options = roles
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.IsSystem ? r.Id.ToString("D10") : r.Name, Names)
            .Select(r => new RoleOptionDto(r.Id, r.Name, UserRules.CanReach(_currentUser, r)))
            .ToList();
        return Result.Success(options);
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (!UserRules.IsValidUsername(request.Username))
        {
            return UserErrors.InvalidUsername;
        }

        if (PasswordPolicy.Validate(request.Password) is { } passwordError)
        {
            return Error.Validation(passwordError);
        }

        var role = await _roles.GetByIdAsync(request.RoleId, cancellationToken);
        if (role is null)
        {
            return UserErrors.RoleNotFound(request.RoleId);
        }

        if (!UserRules.CanReach(_currentUser, role))
        {
            return UserErrors.RoleOutOfReach;
        }

        var username = User.NormalizeUsername(request.Username);
        if (await _users.UsernameExistsAsync(username, cancellationToken))
        {
            return UserErrors.DuplicateUsername;
        }

        User user;
        try
        {
            user = new User(username, request.FullName ?? string.Empty, _hasher.Hash(request.Password!), role);
            user.ResetPassword(user.PasswordHash);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _users.Add(user);
        _audit.Record(AuditActions.UserCreated, nameof(User), user.Username, $"{user.FullName} – {role.Name}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<Result<UserDto>> UpdateAsync(int userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadManageableAsync(userId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var user = loaded.Value;
        var oldRole = user.Role;
        var before = $"{user.FullName} – {oldRole.Name}";

        if (request.RoleId != user.RoleId)
        {
            if (IsSelf(user))
            {
                return UserErrors.SelfRoleChange;
            }

            var newRole = await _roles.GetByIdAsync(request.RoleId, cancellationToken);
            if (newRole is null)
            {
                return UserErrors.RoleNotFound(request.RoleId);
            }

            if (!UserRules.CanReach(_currentUser, newRole))
            {
                return UserErrors.RoleOutOfReach;
            }

            if (!newRole.HasPermission(Permissions.RolesManage) && await IsLastAdministratorAsync(user, cancellationToken))
            {
                return UserErrors.LastAdministrator;
            }

            user.AssignRole(newRole);
        }

        try
        {
            user.Rename(request.FullName ?? string.Empty);
        }
        catch (DomainException ex)
        {
            user.AssignRole(oldRole);
            return SaleErrors.From(ex);
        }

        var after = $"{user.FullName} – {user.Role.Name}";
        if (before != after)
        {
            _audit.Record(AuditActions.UserUpdated, nameof(User), user.Username, $"{before} → {after}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<Result> ResetPasswordAsync(int userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadManageableAsync(userId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var user = loaded.Value;
        if (IsSelf(user))
        {
            return UserErrors.SelfReset;
        }

        if (PasswordPolicy.Validate(newPassword) is { } passwordError)
        {
            return Error.Validation(passwordError);
        }

        user.ResetPassword(_hasher.Hash(newPassword));
        _audit.Record(AuditActions.PasswordReset, nameof(User), user.Username);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadManageableAsync(userId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var user = loaded.Value;
        if (!user.IsLockedOut(_clock.GetUtcNow()) && user.AccessFailedCount == 0)
        {
            return Result.Success();
        }

        user.Unlock();
        _audit.Record(AuditActions.UserUnlocked, nameof(User), user.Username);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> DeactivateAsync(int userId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: false, cancellationToken);

    public Task<Result> ActivateAsync(int userId, CancellationToken cancellationToken = default) =>
        SetActiveAsync(userId, isActive: true, cancellationToken);

    private async Task<Result> SetActiveAsync(int userId, bool isActive, CancellationToken cancellationToken)
    {
        var loaded = await LoadManageableAsync(userId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var user = loaded.Value;
        if (user.IsActive == isActive)
        {
            return Result.Success();
        }

        if (!isActive)
        {
            if (IsSelf(user))
            {
                return UserErrors.SelfDeactivate;
            }

            if (await IsLastAdministratorAsync(user, cancellationToken))
            {
                return UserErrors.LastAdministrator;
            }

            user.Deactivate();
        }
        else
        {
            user.Activate();
        }

        _audit.Record(AuditActions.UserStatusChanged, nameof(User), user.Username, isActive ? "Kích hoạt" : "Ngừng hoạt động");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Loads a user the signed-in user may manage (permission + privilege ceiling on the user's current role).</summary>
    private async Task<Result<User>> LoadManageableAsync(int userId, CancellationToken cancellationToken)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.UserNotFound(userId);
        }

        return UserRules.CanReach(_currentUser, user.Role) ? user : UserErrors.RoleOutOfReach;
    }

    private async Task<bool> IsLastAdministratorAsync(User user, CancellationToken cancellationToken)
    {
        if (!UserRules.IsAdministrator(user))
        {
            return false;
        }

        var all = await _users.ListAsync(cancellationToken);
        return !all.Any(u => u.Id != user.Id && UserRules.IsAdministrator(u));
    }

    private bool IsSelf(User user) => _currentUser.Session?.UserId == user.Id;

    private UserDto ToDto(User u) => new(
        u.Id,
        u.Username,
        u.FullName,
        u.RoleId,
        u.Role.Name,
        u.IsActive,
        u.IsLockedOut(_clock.GetUtcNow()),
        u.LockoutEnd,
        u.MustChangePassword,
        u.LastLoginAt,
        IsSelf(u));

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.UsersManage) ? null : Error.Forbidden(DeniedMessage);
    }

    internal static readonly StringComparer Names =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);
}
