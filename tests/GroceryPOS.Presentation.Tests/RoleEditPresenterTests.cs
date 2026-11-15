using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Users;

namespace GroceryPOS.Presentation.Tests;

public sealed class RoleEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, IRoleEditView
    {
        public string RoleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int? CopyFromRoleId { get; set; }
        public List<string> Focused { get; } = new();

        public void FocusField(string field) => Focused.Add(field);
    }

    private static readonly IReadOnlyList<RoleDto> Roles = new[]
    {
        RolePermissionPresenterTests.Admin,
        RolePermissionPresenterTests.Manager,
        RolePermissionPresenterTests.Cashier,
        RolePermissionPresenterTests.Accountant,
    };

    private static (TestHost Host, RoleEditPresenter Presenter, FakeEditView View) Create(RoleDto? role = null)
    {
        var host = new TestHost(Permissions.RolesManage);
        var presenter = host.Get<RoleEditPresenter>().For(role, Roles);
        var view = new FakeEditView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Add_offers_every_role_to_copy_from()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        Assert.True(presenter.IsNew);
        Assert.Equal("Thêm vai trò", presenter.Title);
        Assert.False(presenter.NameLocked);
        Assert.Equal(
            new[] { RoleEditPresenter.StartEmpty, "Admin", "Quản lý", "Thu ngân", "Kế toán" },
            presenter.CopySources.Select(s => s.Name));
        Assert.Equal(0, presenter.CopySources[0].Id);
    }

    [Fact]
    public void Edit_locks_system_names_and_offers_no_copy()
    {
        var (host, system, _) = Create(RolePermissionPresenterTests.Cashier);
        using var _h = host;
        Assert.Equal("Sửa vai trò – Thu ngân", system.Title);
        Assert.True(system.NameLocked);
        Assert.NotNull(system.NameHelperText);
        Assert.Empty(system.CopySources);

        var custom = host.Get<RoleEditPresenter>().For(RolePermissionPresenterTests.Accountant, Roles);
        Assert.False(custom.NameLocked);
        Assert.Null(custom.NameHelperText);
    }

    [Fact]
    public async Task New_role_copies_the_chosen_roles_permissions()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.RoleName = "  Kế toán 2 ";
        view.Description = "   ";
        view.CopyFromRoleId = RolePermissionPresenterTests.Manager.Id;

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new[] { "create:Kế toán 2" }, host.Roles.Calls);
        Assert.Null(host.Roles.LastRequest!.Description);
        Assert.Equal(RolePermissionPresenterTests.Manager.Permissions.OrderBy(p => p, StringComparer.Ordinal), host.Roles.LastRequest.Permissions);
    }

    [Fact]
    public async Task New_role_starts_empty_by_default()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.RoleName = "Bảo vệ";
        view.CopyFromRoleId = 0;

        Assert.Null(await presenter.SaveAsync());
        Assert.Empty(host.Roles.LastRequest!.Permissions);
    }

    [Fact]
    public async Task Edit_keeps_the_roles_permissions_and_system_name()
    {
        var (host, presenter, view) = Create(RolePermissionPresenterTests.Cashier);
        using var _h = host;
        view.RoleName = "Đổi tên"; // read-only in the dialog; ignored anyway
        view.Description = " Bán hàng ca sáng ";

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new[] { "update:3:Thu ngân" }, host.Roles.Calls);
        Assert.Equal("Bán hàng ca sáng", host.Roles.LastRequest!.Description);
        Assert.Equal(new[] { Permissions.SalesCreate }, host.Roles.LastRequest.Permissions);
    }

    [Fact]
    public async Task Blank_name_focuses_the_name_field()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.RoleName = "   ";

        Assert.Equal("Tên vai trò không được để trống.", await presenter.SaveAsync());
        Assert.Equal(new[] { nameof(SaveRoleRequest.Name) }, view.Focused);
        Assert.Empty(host.Roles.Calls);
    }

    [Fact]
    public async Task Too_long_description_focuses_it()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.RoleName = "Kế toán";
        view.Description = new string('x', 201);

        Assert.NotNull(await presenter.SaveAsync());
        Assert.Equal(new[] { nameof(SaveRoleRequest.Description) }, view.Focused);
    }

    [Fact]
    public async Task Service_errors_go_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.RoleName = "Thu ngân";
        host.Roles.FailWith = Error.Conflict("Tên vai trò đã tồn tại.");

        Assert.Equal("Tên vai trò đã tồn tại.", await presenter.SaveAsync());
        Assert.Empty(view.Focused);
    }
}
