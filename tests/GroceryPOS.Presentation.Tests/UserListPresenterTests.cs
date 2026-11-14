using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Users;

namespace GroceryPOS.Presentation.Tests;

public sealed class UserListPresenterTests
{
    private sealed class FakeListView : RecordingView, IUserListView
    {
        public bool EditorResult { get; set; }
        public bool ResetResult { get; set; }
        public List<UserDto?> OpenedFor { get; } = new();
        public List<UserDto> ResetFor { get; } = new();
        public int Reloads { get; private set; }

        public bool OpenEditor(UserDto? user)
        {
            OpenedFor.Add(user);
            return EditorResult;
        }

        public bool OpenResetPassword(UserDto user)
        {
            ResetFor.Add(user);
            return ResetResult;
        }

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    internal static readonly UserDto Me = new(1, "admin", "Quản trị viên", 1, "Admin", true, false, null, false, null, IsCurrentUser: true);
    internal static readonly UserDto Lan = new(2, "thungan1", "Nguyễn Thị Lan", 3, "Thu ngân", true, false, null, false, null, IsCurrentUser: false);

    private static (TestHost Host, UserListPresenter Presenter, FakeListView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.UsersManage) : new TestHost(Permissions.UsersView);
        var presenter = host.Get<UserListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Row_rules_follow_the_account()
    {
        Assert.Equal("Quản trị viên (bạn)", UserListPresenter.DisplayName(Me));
        Assert.Equal("Nguyễn Thị Lan", UserListPresenter.DisplayName(Lan));
        Assert.False(UserListPresenter.CanActOn(Me));
        Assert.True(UserListPresenter.CanActOn(Lan));
        Assert.False(UserListPresenter.CanUnlock(Lan));
        Assert.True(UserListPresenter.CanUnlock(Lan with { IsLockedOut = true }));
        Assert.Equal("Ngừng hoạt động", UserListPresenter.ToggleText(Lan));
        Assert.Equal("Kích hoạt lại", UserListPresenter.ToggleText(Lan with { IsActive = false }));
    }

    [Fact]
    public async Task Load_returns_accounts_or_toasts_the_error()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Users.Items.AddRange(new[] { Me, Lan });
        Assert.Equal(new[] { Me, Lan }, await presenter.LoadAsync());

        host.Users.FailWith = Error.Forbidden();
        Assert.Null(await presenter.LoadAsync());
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
    }

    [Fact]
    public async Task Without_permission_nothing_opens()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;
        Assert.False(presenter.CanManage);

        await presenter.AddAsync();
        await presenter.EditAsync(Lan);
        await presenter.ResetPasswordAsync(Lan);
        await presenter.UnlockAsync(Lan);
        await presenter.ToggleActiveAsync(Lan);

        Assert.Empty(view.OpenedFor);
        Assert.Empty(view.ResetFor);
        Assert.Empty(host.Users.Calls);
        Assert.Equal(5, view.Warnings.Count);
        Assert.All(view.Warnings, w => Assert.Equal(UserListPresenter.NoPermission, w));
    }

    [Fact]
    public async Task Adding_reloads_without_a_toast_because_the_password_box_was_shown()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.AddAsync();

        Assert.Null(Assert.Single(view.OpenedFor));
        Assert.Empty(view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Editing_toasts_and_reloads_only_when_saved()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.EditAsync(Lan);
        Assert.Equal(0, view.Reloads);

        view.EditorResult = true;
        await presenter.EditAsync(Lan);
        Assert.Equal(new[] { "Đã lưu tài khoản thungan1." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Your_own_account_cannot_be_reset_or_stopped()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ResetPasswordAsync(Me);
        await presenter.ToggleActiveAsync(Me);

        Assert.Empty(view.ResetFor);
        Assert.Empty(view.Questions);
        Assert.Empty(host.Users.Calls);
        Assert.Equal(new[] { UserListPresenter.NotOnYourself, UserListPresenter.NotOnYourself }, view.Warnings);
    }

    [Fact]
    public async Task Reset_password_reloads_after_the_dialog_succeeds()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.ResetResult = true;

        await presenter.ResetPasswordAsync(Lan);

        Assert.Same(Lan, Assert.Single(view.ResetFor));
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Unlock_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.UnlockAsync(Lan with { IsLockedOut = true });

        Assert.Equal(new[] { "unlock:2" }, host.Users.Calls);
        Assert.Equal(new[] { "Đã mở khóa tài khoản thungan1." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Stopping_asks_with_a_red_button_and_resuming_does_not()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        view.ConfirmAnswer = false;
        await presenter.ToggleActiveAsync(Lan);
        Assert.Empty(host.Users.Calls);

        view.ConfirmAnswer = true;
        await presenter.ToggleActiveAsync(Lan);
        await presenter.ToggleActiveAsync(Lan with { IsActive = false });

        Assert.Equal(2, view.DangerQuestions.Count);
        Assert.Contains("thungan1", view.DangerQuestions[0]);
        Assert.Equal(new[] { "deactivate:2", "activate:2" }, host.Users.Calls);
        Assert.Equal(
            new[] { "Đã ngừng hoạt động tài khoản thungan1.", "Đã kích hoạt lại tài khoản thungan1." },
            view.Successes);
        Assert.Equal(2, view.Reloads);
    }

    [Fact]
    public async Task Service_refusal_is_shown_and_nothing_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Users.FailWith = Error.Conflict("Không thể ngừng hoạt động quản trị viên cuối cùng.");

        await presenter.ToggleActiveAsync(Lan);

        Assert.Equal(new[] { "Không thể ngừng hoạt động quản trị viên cuối cùng." }, view.Warnings);
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Closed_screen_is_not_touched_after_the_call()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.IsAlive = false;

        await presenter.UnlockAsync(Lan);

        Assert.Equal(new[] { "unlock:2" }, host.Users.Calls);
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }
}
