using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// User accounts (người dùng): add, edit name/role, reset password, unlock, stop/resume. Accounts are never deleted.
/// Buttons follow <c>users.manage</c>; the service also refuses accounts whose role is above the signed-in user's.
/// </summary>
internal sealed class UserListScreen : UserControl
{
    private const string Caption = "Người dùng";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canManage;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _edit;
    private readonly ModernButton _resetPassword;
    private readonly ModernButton _unlock;
    private readonly ModernButton _toggleActive;

    public UserListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canManage = currentUser.HasPermission(Permissions.UsersManage);
        Dock = DockStyle.Fill;

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có người dùng nào." };
        _grid.AddTextColumn(nameof(UserRow.Username), "Tên đăng nhập", width: 150);
        _grid.AddTextColumn(nameof(UserRow.FullName), "Họ tên");
        _grid.AddTextColumn(nameof(UserRow.RoleName), "Vai trò", width: 140);
        _grid.AddTextColumn(nameof(UserRow.Status), "Trạng thái", width: 150);
        _grid.AddDateColumn(nameof(UserRow.LastLogin), "Đăng nhập gần nhất", width: 160, withTime: true, searchable: false);
        _grid.RowActivated += (_, e) => Open(((UserRow)e.Item).User);
        _grid.SelectionChanged += (_, _) => UpdateToolbarState();

        var add = new ModernButton { Text = "Thêm người dùng", Variant = ButtonVariant.Primary, Width = 150, Enabled = _canManage };
        add.Click += (_, _) => Open(null);

        _edit = new ModernButton { Text = "Sửa", Variant = ButtonVariant.Secondary, Enabled = false };
        _edit.Click += (_, _) =>
        {
            if (Selected is { } user)
            {
                Open(user);
            }
        };

        _resetPassword = new ModernButton { Text = "Đặt lại mật khẩu", Variant = ButtonVariant.Secondary, Width = 150, Enabled = false };
        _resetPassword.Click += (_, _) => ResetPassword();

        _unlock = new ModernButton { Text = "Mở khóa", Variant = ButtonVariant.Secondary, Enabled = false };
        _unlock.Click += async (_, _) => await RunAsync(s => s.UnlockAsync(Selected!.Id));

        _toggleActive = new ModernButton { Text = "Ngừng hoạt động", Variant = ButtonVariant.Secondary, Width = 150, Enabled = false };
        _toggleActive.Click += async (_, _) => await ToggleActiveAsync();

        _grid.ToolbarItems.Controls.AddRange([add, _edit, _resetPassword, _unlock, _toggleActive]);
        Controls.Add(_grid);

        Load += async (_, _) => await ReloadAsync();
    }

    private UserDto? Selected => _grid.GetSelected<UserRow>()?.User;

    private void UpdateToolbarState()
    {
        var selected = Selected;
        var canAct = _canManage && selected is not null;
        _edit.Enabled = canAct;
        _resetPassword.Enabled = canAct && !selected!.IsCurrentUser;
        _unlock.Enabled = canAct && selected!.IsLockedOut;
        _toggleActive.Enabled = canAct && !selected!.IsCurrentUser;
        _toggleActive.Text = selected is { IsActive: false } ? "Kích hoạt lại" : "Ngừng hoạt động";
    }

    private void Open(UserDto? user)
    {
        if (!_canManage)
        {
            return;
        }

        using var dialog = new UserEditForm(_scopeFactory, user);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private void ResetPassword()
    {
        if (Selected is not { } user)
        {
            return;
        }

        using var dialog = new ResetPasswordForm(_scopeFactory, user);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } user)
        {
            return;
        }

        if (user.IsActive
            && MessageBox.Show(this, $"Ngừng hoạt động tài khoản \"{user.Username}\" ({user.FullName})?\nNgười này sẽ không đăng nhập được nữa.",
                Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await RunAsync(s => user.IsActive ? s.DeactivateAsync(user.Id) : s.ActivateAsync(user.Id));
    }

    private async Task RunAsync(Func<IUserManagementService, Task<Result>> action)
    {
        if (Selected is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await action(scope.ServiceProvider.GetRequiredService<IUserManagementService>());
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUserManagementService>().ListAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value.Select(u => new UserRow(u)).ToList());
        }
        else
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        UpdateToolbarState();
    }

    /// <summary>Grid row: the DTO plus the last sign-in in local (store) time.</summary>
    private sealed record UserRow(UserDto User)
    {
        public string Username => User.Username;
        public string FullName => User.IsCurrentUser ? $"{User.FullName} (bạn)" : User.FullName;
        public string RoleName => User.RoleName;
        public string Status => User.Status;
        public DateTime? LastLogin => User.LastLoginAt?.LocalDateTime;
    }
}
