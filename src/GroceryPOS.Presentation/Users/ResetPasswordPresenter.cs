using FluentValidation;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Users;

/// <summary>The reset-password dialog as seen by its presenter.</summary>
public interface IResetPasswordView : IView
{
    /// <summary>The new temporary password (pre-filled, ↻ makes a new one).</summary>
    string Password { get; }
}

/// <summary>
/// Sets a temporary password for another account. The account is unlocked and must change the password at the
/// next sign-in; the new password is shown once so it can be handed over.
/// </summary>
public sealed class ResetPasswordPresenter : Presenter<IResetPasswordView>
{
    public const string Title = "Đặt lại mật khẩu";

    private UserDto? _user;

    public ResetPasswordPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    public UserDto User => _user ?? throw new InvalidOperationException("Call For(user) before showing the dialog.");

    /// <summary>The read-only "Tài khoản" line.</summary>
    public string AccountText => $"{User.Username} – {User.FullName}";

    public ResetPasswordPresenter For(UserDto user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        return this;
    }

    public static string DoneMessage(string username, string password) =>
        $"Mật khẩu tạm của \"{username}\": {password}\n\nTài khoản đã được mở khóa (nếu đang bị khóa).";

    /// <summary>Validates and resets; returns the message for the dialog's error banner, or null when done.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var password = View.Password;
        var request = new ResetPasswordRequest(password, password);
        if (FormInput.FirstError(services.GetRequiredService<IValidator<ResetPasswordRequest>>(), request) is { } invalid)
        {
            return invalid.Message;
        }

        Result result = await services.GetRequiredService<IUserManagementService>().ResetPasswordAsync(User.Id, password);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        if (ViewAlive)
        {
            View.ShowInfo(DoneMessage(User.Username, password), Title);
        }

        return null;
    });
}
