using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Users;

/// <summary>The Người dùng screen as seen by its presenter.</summary>
public interface IUserListView : IView
{
    /// <summary>Opens the add (<paramref name="user"/> = null) or edit dialog; true when it was saved.</summary>
    bool OpenEditor(UserDto? user);

    /// <summary>Opens the reset-password dialog; true when a new temporary password was set.</summary>
    bool OpenResetPassword(UserDto user);

    /// <summary>Reloads the list (keeps the selection by key).</summary>
    Task ReloadAsync();
}

/// <summary>
/// Người dùng screen logic: add, edit name/role, reset password, unlock and stop/resume accounts (never deleted).
/// Buttons follow <c>users.manage</c>; the service also refuses accounts whose role is above the signed-in user's
/// and never lets the last administrator go. Your own account cannot be reset or stopped from here.
/// </summary>
public sealed class UserListPresenter : Presenter<IUserListView>
{
    public const string Title = "Người dùng";
    public const string NoPermission = "Bạn không có quyền quản lý người dùng.";
    public const string NotOnYourself = "Bạn không thể thực hiện thao tác này trên tài khoản của chính mình.";

    public UserListPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.UsersManage);
    }

    /// <summary>Every action on this screen needs <c>users.manage</c>.</summary>
    public bool CanManage { get; }

    /// <summary>Name shown in the list; your own account is marked "(bạn)".</summary>
    public static string DisplayName(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsCurrentUser ? $"{user.FullName} (bạn)" : user.FullName;
    }

    /// <summary>Caption of the stop/resume button for the selected row.</summary>
    public static string ToggleText(UserDto? user) => user is { IsActive: false } ? "Kích hoạt lại" : "Ngừng hoạt động";

    /// <summary>Reset password and stop/resume are disabled on your own account (use Đổi mật khẩu instead).</summary>
    public static bool CanActOn(UserDto user) => !user.IsCurrentUser;

    /// <summary>Mở khóa is only useful for an account locked after failed sign-ins.</summary>
    public static bool CanUnlock(UserDto user) => user.IsLockedOut;

    public static string DeactivateQuestion(UserDto user) =>
        $"Ngừng hoạt động tài khoản \"{user.Username}\" ({user.FullName})?\nNgười này sẽ không đăng nhập được nữa.";

    /// <summary>All accounts, or null after showing the error (the view keeps what it had).</summary>
    public async Task<IReadOnlyList<UserDto>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await UseAsync<IUserManagementService, Result<IReadOnlyList<UserDto>>>(
            users => users.ListAsync(cancellationToken));
        if (result.IsSuccess)
        {
            return result.Value;
        }

        if (ViewAlive)
        {
            View.ToastError(result.Error.Message, Title);
        }

        return null;
    }

    public Task AddAsync() => OpenEditorAsync(null);

    public Task EditAsync(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return OpenEditorAsync(user);
    }

    /// <summary>Opens the reset-password dialog (the dialog itself shows the new temporary password), then reloads.</summary>
    public async Task ResetPasswordAsync(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Allowed(user))
        {
            return;
        }

        if (View.OpenResetPassword(user) && ViewAlive)
        {
            await View.ReloadAsync();
        }
    }

    public async Task UnlockAsync(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        await RunAsync(users => users.UnlockAsync(user.Id), $"Đã mở khóa tài khoản {user.Username}.");
    }

    /// <summary>Stops an active account (after a red confirmation) or resumes a stopped one, then reloads.</summary>
    public async Task ToggleActiveAsync(UserDto user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Allowed(user))
        {
            return;
        }

        if (user.IsActive && !View.Confirm(DeactivateQuestion(user), Title, "Ngừng hoạt động", danger: true))
        {
            return;
        }

        await RunAsync(
            users => user.IsActive ? users.DeactivateAsync(user.Id) : users.ActivateAsync(user.Id),
            user.IsActive ? $"Đã ngừng hoạt động tài khoản {user.Username}." : $"Đã kích hoạt lại tài khoản {user.Username}.");
    }

    private bool Allowed(UserDto user)
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return false;
        }

        if (!CanActOn(user))
        {
            View.ShowWarning(NotOnYourself, Title);
            return false;
        }

        return true;
    }

    private async Task RunAsync(Func<IUserManagementService, Task<Result>> action, string successMessage)
    {
        var result = await UseAsync(action);
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess(successMessage);
        await View.ReloadAsync();
    }

    private async Task OpenEditorAsync(UserDto? user)
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (!View.OpenEditor(user) || !ViewAlive)
        {
            return;
        }

        // A new account already got an information box with its temporary password.
        if (user is not null)
        {
            View.ToastSuccess($"Đã lưu tài khoản {user.Username}.");
        }

        await View.ReloadAsync();
    }
}
