using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Users;

namespace GroceryPOS.Presentation.Tests;

public sealed class UserEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, IUserEditView
    {
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int? RoleId { get; set; }
        public string Password { get; set; } = string.Empty;
        public List<RoleOptionDto>? Roles { get; private set; }
        public int? SelectedRole { get; private set; }
        public List<string> Focused { get; } = new();

        public void ShowRoles(IReadOnlyList<RoleOptionDto> roles, int? selectedRoleId)
        {
            Roles = roles.ToList();
            SelectedRole = selectedRoleId;
        }

        public void FocusField(string field) => Focused.Add(field);
    }

    private static readonly RoleOptionDto Admin = new(1, "Admin", CanAssign: false);
    private static readonly RoleOptionDto Manager = new(2, "Quản lý", CanAssign: false);
    private static readonly RoleOptionDto Cashier = new(3, "Thu ngân", CanAssign: true);
    private static readonly RoleOptionDto Stock = new(4, "Thủ kho", CanAssign: true);

    private static (TestHost Host, UserEditPresenter Presenter, FakeEditView View) Create(UserDto? user = null)
    {
        var host = new TestHost(Permissions.UsersManage);
        host.Users.Roles.AddRange(new[] { Admin, Manager, Cashier, Stock });
        if (user is not null)
        {
            host.Users.Items.Add(user);
        }

        var presenter = host.Get<UserEditPresenter>().For(user);
        var view = new FakeEditView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Title_and_locks_follow_what_is_edited()
    {
        var (host, add, _) = Create();
        using var _h = host;
        Assert.True(add.IsNew);
        Assert.Equal("Thêm người dùng", add.Title);
        Assert.NotNull(add.UsernameHelperText);
        Assert.False(add.RoleLocked);

        var edit = host.Get<UserEditPresenter>().For(UserListPresenterTests.Lan);
        Assert.Equal("Sửa người dùng – thungan1", edit.Title);
        Assert.Null(edit.UsernameHelperText);
        Assert.False(edit.RoleLocked);

        var self = host.Get<UserEditPresenter>().For(UserListPresenterTests.Me);
        Assert.True(self.RoleLocked);
        Assert.NotNull(self.RoleHelperText);
    }

    [Fact]
    public void Temporary_passwords_are_random()
    {
        var a = UserEditPresenter.NewTemporaryPassword();
        Assert.False(string.IsNullOrWhiteSpace(a));
        Assert.Contains(Enumerable.Range(0, 5).Select(_ => UserEditPresenter.NewTemporaryPassword()), p => p != a);
    }

    [Fact]
    public async Task Role_list_offers_assignable_roles_plus_the_current_one()
    {
        var (host, add, addView) = Create();
        using var _h = host;
        Assert.Null(await add.LoadRolesAsync());
        Assert.Equal(new[] { 3, 4 }, addView.Roles!.Select(r => r.Id));
        Assert.Null(addView.SelectedRole);

        var manager = UserListPresenterTests.Lan with { Id = 5, RoleId = 2, RoleName = "Quản lý" };
        var (editHost, edit, editView) = Create(manager);
        using var _e = editHost;
        Assert.Null(await edit.LoadRolesAsync());
        Assert.Equal(new[] { 2, 3, 4 }, editView.Roles!.Select(r => r.Id));
        Assert.Equal(2, editView.SelectedRole);
    }

    [Fact]
    public async Task Role_list_failure_goes_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Users.FailWith = Error.Forbidden();

        Assert.Equal(Error.Forbidden().Message, await presenter.LoadRolesAsync());
        Assert.Null(view.Roles);
    }

    [Theory]
    [InlineData("a", "Lan", 3, "Username")]
    [InlineData("thungan2", "  ", 3, "FullName")]
    [InlineData("thungan2", "Lan", null, "RoleId")]
    public async Task Invalid_new_account_focuses_the_first_bad_field(string username, string fullName, int? roleId, string field)
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.Username = username;
        view.FullName = fullName;
        view.RoleId = roleId;
        view.Password = UserEditPresenter.NewTemporaryPassword();

        Assert.False(string.IsNullOrEmpty(await presenter.SaveAsync()));
        Assert.Equal(new[] { field }, view.Focused);
        Assert.Empty(host.Users.Calls);
    }

    [Fact]
    public async Task Weak_password_is_refused()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.Username = "thungan2";
        view.FullName = "Lan";
        view.RoleId = 3;
        view.Password = "123";

        Assert.NotNull(await presenter.SaveAsync());
        Assert.Equal(new[] { "Password" }, view.Focused);
    }

    [Fact]
    public async Task Creating_shows_the_temporary_password_once()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var password = UserEditPresenter.NewTemporaryPassword();
        view.Username = " thungan2 ";
        view.FullName = " Trần Văn Hùng ";
        view.RoleId = 3;
        view.Password = password;

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new[] { "create:thungan2" }, host.Users.Calls);
        Assert.Equal("Trần Văn Hùng", host.Users.LastCreate!.FullName);
        Assert.Equal(password, host.Users.LastCreate.ConfirmPassword);
        var info = Assert.Single(view.Infos);
        Assert.Contains(password, info);
        Assert.Contains("thungan2", info);
    }

    [Fact]
    public async Task Editing_updates_name_and_role_only()
    {
        var (host, presenter, view) = Create(UserListPresenterTests.Lan);
        using var _h = host;
        view.FullName = "Nguyễn Thị Lan Anh";
        view.RoleId = 4;

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new[] { "update:2:Nguyễn Thị Lan Anh:4" }, host.Users.Calls);
        Assert.Empty(view.Infos);
    }

    [Fact]
    public async Task Service_error_goes_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.Username = "thungan1";
        view.FullName = "Lan";
        view.RoleId = 3;
        view.Password = UserEditPresenter.NewTemporaryPassword();
        host.Users.FailWith = Error.Conflict("Tên đăng nhập đã tồn tại.");

        Assert.Equal("Tên đăng nhập đã tồn tại.", await presenter.SaveAsync());
        Assert.Empty(view.Infos);
    }
}
