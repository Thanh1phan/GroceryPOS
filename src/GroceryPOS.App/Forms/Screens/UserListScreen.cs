using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// User accounts (người dùng): add, edit name/role, reset password, unlock, stop/resume. Accounts are never deleted.
/// Buttons follow <c>users.manage</c>; the service also refuses accounts whose role is above the signed-in user's.
/// </summary>
internal sealed class UserListScreen : BaseListView<UserListScreen.UserRow>
{
    private const string Caption = "Người dùng";

    private readonly IServiceScopeFactory _scopeFactory;

    public UserListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        var canManage = currentUser.HasPermission(Permissions.UsersManage);

        Grid.EmptyText = "Chưa có người dùng nào.";
        Grid.AddTextColumn(nameof(UserRow.Username), "Tên đăng nhập", width: 150);
        Grid.AddTextColumn(nameof(UserRow.FullName), "Họ tên");
        Grid.AddTextColumn(nameof(UserRow.RoleName), "Vai trò", width: 140);
        Grid.AddTextColumn(nameof(UserRow.Status), "Trạng thái", width: 150);
        Grid.AddDateColumn(nameof(UserRow.LastLogin), "Đăng nhập gần nhất", width: 160, withTime: true, searchable: false);

        AddCommand(new ListCommand<UserRow>("Thêm người dùng") { Allowed = canManage }, () => Open(null), ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<UserRow>("Sửa") { Allowed = canManage }, row => Open(row.User), iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<UserRow>("Đặt lại mật khẩu") { Allowed = canManage, EnabledWhen = r => !r.User.IsCurrentUser },
            row => ResetPassword(row.User),
            iconGlyph: Glyphs.Lock);
        AddCommand(
            new ListCommand<UserRow>("Mở khóa") { Allowed = canManage, EnabledWhen = r => r.User.IsLockedOut },
            row => RunAsync(s => s.UnlockAsync(row.User.Id), $"Đã mở khóa tài khoản {row.User.Username}."));
        AddCommand(
            new ListCommand<UserRow>("Ngừng hoạt động")
            {
                Allowed = canManage,
                EnabledWhen = r => !r.User.IsCurrentUser,
                TextFor = r => r.User.IsActive ? "Ngừng hoạt động" : "Kích hoạt lại",
            },
            row => ToggleActiveAsync(row.User));
    }

    protected override object? KeyOf(UserRow item) => item.User.Id;

    protected override async Task<IReadOnlyList<UserRow>?> LoadItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ListAsync(cancellationToken);
        if (result.IsFailure)
        {
            ToastError(result.Error.Message, Caption);
            return null;
        }

        return result.Value.Select(u => new UserRow(u)).ToList();
    }

    private void Open(UserDto? user)
    {
        using var dialog = new UserEditForm(_scopeFactory, user);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (user is not null)
            {
                ToastSuccess($"Đã lưu tài khoản {user.Username}.");
            }

            RequestRefresh();
        }
    }

    private void ResetPassword(UserDto user)
    {
        using var dialog = new ResetPasswordForm(_scopeFactory, user);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RequestRefresh();
        }
    }

    private async Task ToggleActiveAsync(UserDto user)
    {
        if (user.IsActive
            && !Confirm($"Ngừng hoạt động tài khoản \"{user.Username}\" ({user.FullName})?\nNgười này sẽ không đăng nhập được nữa.",
                Caption, "Ngừng hoạt động", "Hủy", danger: true))
        {
            return;
        }

        await RunAsync(
            s => user.IsActive ? s.DeactivateAsync(user.Id) : s.ActivateAsync(user.Id),
            user.IsActive ? $"Đã ngừng hoạt động tài khoản {user.Username}." : $"Đã kích hoạt lại tài khoản {user.Username}.");
    }

    private async Task RunAsync(Func<IUserManagementService, Task<Result>> action, string successMessage)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await action(scope.ServiceProvider.GetRequiredService<IUserManagementService>());
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, Caption);
            return;
        }

        ToastSuccess(successMessage);
        await RefreshDataAsync();
    }

    /// <summary>Grid row: the DTO plus the last sign-in in local (store) time.</summary>
    internal sealed record UserRow(UserDto User)
    {
        public string Username => User.Username;
        public string FullName => User.IsCurrentUser ? $"{User.FullName} (bạn)" : User.FullName;
        public string RoleName => User.RoleName;
        public string Status => User.Status;
        public DateTime? LastLogin => User.LastLoginAt?.LocalDateTime;
    }
}
