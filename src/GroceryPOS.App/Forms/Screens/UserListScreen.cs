using GroceryPOS.Application.Users;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Users;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// User accounts (người dùng) – passive MVP view; the logic lives in <see cref="UserListPresenter"/>.
/// Accounts are never deleted, only stopped.
/// </summary>
internal sealed class UserListScreen : BaseListView<UserListScreen.UserRow>, IUserListView
{
    private readonly IServiceProvider _services;
    private readonly UserListPresenter _presenter;

    public UserListScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<UserListPresenter>();
        _presenter.Attach(this);
        var canManage = _presenter.CanManage;

        Grid.EmptyText = "Chưa có người dùng nào.";
        Grid.AddTextColumn(nameof(UserRow.Username), "Tên đăng nhập", width: 150);
        Grid.AddTextColumn(nameof(UserRow.FullName), "Họ tên");
        Grid.AddTextColumn(nameof(UserRow.RoleName), "Vai trò", width: 140);
        Grid.AddTextColumn(nameof(UserRow.Status), "Trạng thái", width: 150);
        Grid.AddDateColumn(nameof(UserRow.LastLogin), "Đăng nhập gần nhất", width: 160, withTime: true, searchable: false);

        AddCommand(new ListCommand<UserRow>("Thêm người dùng") { Allowed = canManage }, _presenter.AddAsync, ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<UserRow>("Sửa") { Allowed = canManage }, row => _presenter.EditAsync(row.User), iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<UserRow>("Đặt lại mật khẩu") { Allowed = canManage, EnabledWhen = r => UserListPresenter.CanActOn(r.User) },
            row => _presenter.ResetPasswordAsync(row.User),
            iconGlyph: Glyphs.Lock);
        AddCommand(
            new ListCommand<UserRow>("Mở khóa") { Allowed = canManage, EnabledWhen = r => UserListPresenter.CanUnlock(r.User) },
            row => _presenter.UnlockAsync(row.User));
        AddCommand(
            new ListCommand<UserRow>("Ngừng hoạt động")
            {
                Allowed = canManage,
                EnabledWhen = r => UserListPresenter.CanActOn(r.User),
                TextFor = r => UserListPresenter.ToggleText(r.User),
            },
            row => _presenter.ToggleActiveAsync(row.User));
    }

    bool IView.IsAlive => IsAlive;

    protected override object? KeyOf(UserRow item) => item.User.Id;

    protected override async Task<IReadOnlyList<UserRow>?> LoadItemsAsync(CancellationToken cancellationToken) =>
        (await _presenter.LoadAsync(cancellationToken))?.Select(u => new UserRow(u)).ToList();

    bool IUserListView.OpenEditor(UserDto? user)
    {
        using var dialog = new UserEditForm(_services.GetRequiredService<UserEditPresenter>().For(user));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    bool IUserListView.OpenResetPassword(UserDto user)
    {
        using var dialog = new ResetPasswordForm(_services.GetRequiredService<ResetPasswordPresenter>().For(user));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    Task IUserListView.ReloadAsync() => RefreshDataAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    /// <summary>Grid row: the DTO plus the last sign-in in local (store) time.</summary>
    internal sealed record UserRow(UserDto User)
    {
        public string Username => User.Username;
        public string FullName => UserListPresenter.DisplayName(User);
        public string RoleName => User.RoleName;
        public string Status => User.Status;
        public DateTime? LastLogin => User.LastLoginAt?.LocalDateTime;
    }
}
