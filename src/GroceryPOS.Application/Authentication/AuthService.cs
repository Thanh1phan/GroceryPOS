using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroceryPOS.Application.Authentication;

public interface IAuthService
{
    Task<Result<UserSession>> LoginAsync(string username, string password, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Login with lockout, change password and logout.</summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _hasher;
    private readonly IUserSessionStore _sessions;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly LockoutPolicy _lockout;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher hasher,
        IUserSessionStore sessions,
        IAuditService audit,
        TimeProvider clock,
        LockoutPolicy? lockout = null,
        ILogger<AuthService>? logger = null)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _sessions = sessions;
        _audit = audit;
        _clock = clock;
        _lockout = lockout ?? LockoutPolicy.Default;
        _logger = logger ?? NullLogger<AuthService>.Instance;
    }

    public async Task<Result<UserSession>> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return Error.Validation("Vui lòng nhập tên đăng nhập và mật khẩu.");
        }

        var normalized = username.Trim().ToLowerInvariant();
        var now = _clock.GetUtcNow();
        var user = await _users.GetByUsernameAsync(normalized, cancellationToken);

        if (user is null)
        {
            _audit.RecordFor(null, normalized, AuditActions.LoginFailed, "Tài khoản không tồn tại");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Login failed for unknown user {Username}", normalized);
            return AuthErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            _logger.LogWarning("Login rejected for {Username}: locked out until {LockoutEnd}", user.Username, user.LockoutEnd);
            return AuthErrors.LockedOut(user.LockoutEnd!.Value, now);
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            var lockedNow = user.RegisterFailedLogin(now, _lockout);
            _audit.RecordFor(user.Id, user.Username, lockedNow ? AuditActions.AccountLocked : AuditActions.LoginFailed);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (lockedNow)
            {
                _logger.LogWarning("Account {Username} locked after {Attempts} failed logins", user.Username, _lockout.MaxFailedAttempts);
            }
            else
            {
                _logger.LogWarning("Login failed for {Username}: wrong password", user.Username);
            }

            return lockedNow ? AuthErrors.LockedOut(user.LockoutEnd!.Value, now) : AuthErrors.InvalidCredentials;
        }

        // Inactive check comes after the password check so it does not leak account status to guessers.
        if (!user.IsActive)
        {
            _logger.LogWarning("Login rejected for {Username}: account inactive", user.Username);
            return AuthErrors.Inactive;
        }

        user.RegisterSuccessfulLogin(now);
        var session = new UserSession(
            user.Id,
            user.Username,
            user.FullName,
            user.Role.Name,
            user.Role.PermissionKeys,
            now,
            user.MustChangePassword);

        _sessions.SignIn(session);
        _audit.Record(AuditActions.LoginSucceeded, nameof(User), user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {Username} signed in as {Role}", user.Username, session.RoleName);
        return session;
    }

    public async Task<Result> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Session;
        if (session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (PasswordPolicy.Validate(newPassword) is { } policyError)
        {
            return Error.Validation(policyError);
        }

        if (currentPassword == newPassword)
        {
            return AuthErrors.SamePassword;
        }

        var user = await _users.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (!_hasher.Verify(currentPassword, user.PasswordHash))
        {
            return AuthErrors.WrongCurrentPassword;
        }

        user.SetPasswordHash(_hasher.Hash(newPassword), mustChange: false);
        _audit.Record(AuditActions.PasswordChanged, nameof(User), user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _sessions.SignIn(session with { MustChangePassword = false });
        _logger.LogInformation("User {Username} changed password", session.Username);
        return Result.Success();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_sessions.Session is not { } session)
        {
            return;
        }

        _audit.Record(AuditActions.Logout, nameof(User), session.UserId.ToString());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _sessions.SignOut();
        _logger.LogInformation("User {Username} signed out", session.Username);
    }
}
