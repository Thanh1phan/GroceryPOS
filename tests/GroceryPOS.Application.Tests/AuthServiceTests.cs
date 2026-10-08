using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public class AuthServiceTests
{
    private const string Password = "matkhau123";

    private readonly InMemoryUserRepository _users = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly UserSessionStore _sessions = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();
    private readonly AuthService _sut;
    private readonly User _cashier;

    public AuthServiceTests()
    {
        var role = new Role(SystemRoles.Cashier);
        role.Grant(Permissions.SalesCreate);
        _cashier = new User("thungan", "Trần Thị B", _hasher.Hash(Password), role);
        _users.Add(_cashier);
        _sut = new AuthService(_users, _uow, _hasher, _sessions, _audit, _clock, new LockoutPolicy(3, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task Login_succeeds_and_opens_session_with_permissions()
    {
        var result = await _sut.LoginAsync("  ThuNgan ", Password);

        Assert.True(result.IsSuccess);
        Assert.Equal("thungan", result.Value.Username);
        Assert.True(_sessions.Session!.Permissions.Contains(Permissions.SalesCreate));
        Assert.Contains(AuditActions.LoginSucceeded, _audit.Actions);
        Assert.Equal(_clock.Now, _cashier.LastLoginAt);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("thungan", "")]
    public async Task Login_requires_both_fields(string username, string password)
    {
        var result = await _sut.LoginAsync(username, password);

        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Unknown_user_gets_generic_error_and_is_audited()
    {
        var result = await _sut.LoginAsync("khongton", Password);

        Assert.Equal(AuthErrors.InvalidCredentials, result.Error);
        Assert.Contains(AuditActions.LoginFailed, _audit.Actions);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Third_wrong_password_locks_account()
    {
        await _sut.LoginAsync("thungan", "sai1");
        await _sut.LoginAsync("thungan", "sai2");
        var third = await _sut.LoginAsync("thungan", "sai3");

        Assert.Equal("auth.locked", third.Error.Code);
        Assert.Contains(AuditActions.AccountLocked, _audit.Actions);

        // Even the correct password is refused while locked.
        var correct = await _sut.LoginAsync("thungan", Password);
        Assert.Equal("auth.locked", correct.Error.Code);
        Assert.Null(_sessions.Session);
    }

    [Fact]
    public async Task Lockout_expires_after_duration()
    {
        for (var i = 0; i < 3; i++)
        {
            await _sut.LoginAsync("thungan", "sai");
        }

        _clock.Advance(TimeSpan.FromMinutes(16));
        var result = await _sut.LoginAsync("thungan", Password);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Inactive_user_cannot_login()
    {
        _cashier.Deactivate();

        var result = await _sut.LoginAsync("thungan", Password);

        Assert.Equal(AuthErrors.Inactive, result.Error);
    }

    [Fact]
    public async Task ChangePassword_requires_login()
    {
        var result = await _sut.ChangePasswordAsync(Password, "matkhaumoi1");

        Assert.Equal(AuthErrors.NotAuthenticated, result.Error);
    }

    [Fact]
    public async Task ChangePassword_validates_policy_current_password_and_difference()
    {
        await _sut.LoginAsync("thungan", Password);

        Assert.Equal("validation", (await _sut.ChangePasswordAsync(Password, "short1")).Error.Code);
        Assert.Equal(AuthErrors.SamePassword, (await _sut.ChangePasswordAsync(Password, Password)).Error);
        Assert.Equal(AuthErrors.WrongCurrentPassword, (await _sut.ChangePasswordAsync("saimatkhau1", "matkhaumoi1")).Error);
    }

    [Fact]
    public async Task ChangePassword_updates_hash_and_clears_must_change_flag()
    {
        _cashier.SetPasswordHash(_hasher.Hash(Password), mustChange: true);
        var login = await _sut.LoginAsync("thungan", Password);
        Assert.True(login.Value.MustChangePassword);

        var result = await _sut.ChangePasswordAsync(Password, "matkhaumoi1");

        Assert.True(result.IsSuccess);
        Assert.True(_hasher.Verify("matkhaumoi1", _cashier.PasswordHash));
        Assert.False(_cashier.MustChangePassword);
        Assert.False(_sessions.Session!.MustChangePassword);
        Assert.Contains(AuditActions.PasswordChanged, _audit.Actions);
    }

    [Fact]
    public async Task Logout_clears_session_and_audits()
    {
        await _sut.LoginAsync("thungan", Password);
        var changes = 0;
        _sessions.SessionChanged += (_, _) => changes++;

        await _sut.LogoutAsync();

        Assert.Null(_sessions.Session);
        Assert.Equal(1, changes);
        Assert.Contains(AuditActions.Logout, _audit.Actions);
    }
}
