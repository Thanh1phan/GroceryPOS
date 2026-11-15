using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Users;

namespace GroceryPOS.Presentation.Tests;

public sealed class RolePermissionPresenterTests
{
    internal sealed class FakeMatrixView : RecordingView, IRolePermissionView
    {
        public int? SelectedRoleId { get; set; }
        public PermissionMatrix? Matrix { get; private set; }
        public int? SelectedOnShow { get; private set; }
        public int Shows { get; private set; }
        public int Refreshes { get; private set; }
        public string? Status { get; private set; }
        public bool StatusEmphasis { get; private set; }
        public List<string> Editors { get; } = new();

        /// <summary>What the role dialog returns (saved or cancelled).</summary>
        public bool EditorSaves { get; set; } = true;

        public void ShowMatrix(PermissionMatrix matrix, int? selectRoleId)
        {
            Matrix = matrix;
            SelectedOnShow = selectRoleId;
            Shows++;
        }

        public void RefreshMatrix() => Refreshes++;

        public void ShowStatus(string text, bool emphasis)
        {
            Status = text;
            StatusEmphasis = emphasis;
        }

        public bool OpenRoleEditor(RoleDto? role, IReadOnlyList<RoleDto> roles)
        {
            Editors.Add(role?.Name ?? "<new>");
            return EditorSaves;
        }
    }

    private static readonly PermissionGroupDto Sales = new("Bán hàng", new[]
    {
        new PermissionItem(Permissions.SalesCreate, "Bán hàng"),
        new PermissionItem(Permissions.ReturnsCreate, "Trả hàng"),
    });

    private static readonly PermissionGroupDto System = new("Hệ thống", new[]
    {
        new PermissionItem(Permissions.RolesManage, "Quản lý vai trò & phân quyền"),
    });

    internal static readonly RoleDto Admin = new(1, "Admin", null, true, true, Permissions.All.ToHashSet(), 1);
    internal static readonly RoleDto Manager = new(2, "Quản lý", null, true, false, new HashSet<string> { Permissions.SalesCreate, Permissions.RolesManage }, 2);
    internal static readonly RoleDto Cashier = new(3, "Thu ngân", "Bán hàng tại quầy", true, false, new HashSet<string> { Permissions.SalesCreate }, 3);
    internal static readonly RoleDto Accountant = new(4, "Kế toán", null, false, false, new HashSet<string>(), 0);

    private static (TestHost Host, RolePermissionPresenter Presenter, FakeMatrixView View) Create(params string[] permissions)
    {
        var host = new TestHost(permissions.Length == 0 ? new[] { Permissions.RolesManage } : permissions);
        host.Roles.Catalog.AddRange(new[] { Sales, System });
        host.Roles.Items.AddRange(new[] { Admin, Manager, Cashier, Accountant });
        var presenter = host.Get<RolePermissionPresenter>();
        var view = new FakeMatrixView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static async Task<(TestHost Host, RolePermissionPresenter Presenter, FakeMatrixView View)> Loaded()
    {
        var created = Create();
        Assert.True(await created.Presenter.LoadAsync());
        return created;
    }

    [Fact]
    public async Task Load_shows_the_matrix_and_keeps_the_selected_column()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.SelectedRoleId = 3;

        Assert.True(await presenter.LoadAsync());

        Assert.Equal(1, view.Shows);
        Assert.Equal(3, view.SelectedOnShow);
        Assert.Equal(4, view.Matrix!.Roles.Count);
        Assert.Equal(2, view.Matrix.Groups.Count);
        Assert.Equal(RolePermissionPresenter.IdleHint, view.Status);
        Assert.False(presenter.HasChanges);
        Assert.False(presenter.CanSave);
    }

    [Fact]
    public async Task Load_failure_warns_and_keeps_what_was_shown()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Roles.ListFailsWith = Error.Forbidden();

        Assert.False(await presenter.LoadAsync());

        Assert.Equal(0, view.Shows);
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Warnings);
    }

    [Fact]
    public async Task Without_permission_nothing_loads_or_changes()
    {
        var (host, presenter, view) = Create(Permissions.UsersManage);
        using var _h = host;
        Assert.False(presenter.CanManage);

        Assert.False(await presenter.LoadAsync());
        await presenter.AddRoleAsync();
        await presenter.SaveAsync();

        Assert.Equal(new[] { RolePermissionPresenter.NoPermission, RolePermissionPresenter.NoPermission, RolePermissionPresenter.NoPermission }, view.Warnings);
        Assert.Empty(host.Roles.Calls);
        Assert.Empty(view.Editors);
    }

    [Fact]
    public async Task Toggling_marks_changes_and_undo_discards_them()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        presenter.Toggle(Cashier.Id, Permissions.ReturnsCreate);

        Assert.True(presenter.HasChanges);
        Assert.True(presenter.CanSave);
        Assert.True(view.Matrix!.Has(Cashier.Id, Permissions.ReturnsCreate));
        Assert.Equal(1, view.Refreshes);
        Assert.True(view.StatusEmphasis);
        Assert.Contains("1 vai trò", view.Status);

        presenter.Undo();

        Assert.False(presenter.HasChanges);
        Assert.False(view.Matrix.Has(Cashier.Id, Permissions.ReturnsCreate));
        Assert.Equal(RolePermissionPresenter.IdleHint, view.Status);
        Assert.False(view.StatusEmphasis);
    }

    [Fact]
    public async Task Group_toggle_grants_then_revokes_the_whole_group()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        presenter.ToggleGroup(Cashier.Id, Sales);
        Assert.Equal(GroupCheck.All, view.Matrix!.StateOf(Cashier.Id, Sales));

        presenter.ToggleGroup(Cashier.Id, Sales);
        Assert.Equal(GroupCheck.None, view.Matrix.StateOf(Cashier.Id, Sales));
        Assert.True(presenter.HasChanges); // Thu ngân lost "Bán hàng"
    }

    [Fact]
    public async Task Admin_column_is_read_only_with_a_hint()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        presenter.Toggle(Admin.Id, Permissions.SalesCreate);

        Assert.False(presenter.HasChanges);
        Assert.Equal(0, view.Refreshes);
        Assert.Equal(RolePermissionPresenter.AdminLocked, view.Status);
    }

    [Fact]
    public async Task Save_sends_only_changed_roles_then_reloads()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        presenter.Toggle(Cashier.Id, Permissions.ReturnsCreate);

        await presenter.SaveAsync();

        Assert.Contains("save:3", host.Roles.Calls);
        Assert.Equal(new[] { Permissions.SalesCreate, Permissions.ReturnsCreate }.OrderBy(p => p, StringComparer.Ordinal), host.Roles.LastMatrix!.Single().Permissions);
        Assert.Equal(2, view.Shows);
        Assert.False(presenter.HasChanges);
        Assert.Equal(RolePermissionPresenter.Saved, view.Status);
        Assert.Single(view.Successes);
        Assert.Empty(view.Questions);
    }

    [Fact]
    public async Task Save_failure_warns_and_keeps_the_changes()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        presenter.Toggle(Manager.Id, Permissions.ReturnsCreate);
        host.Roles.FailWith = Error.Conflict("Phải còn ít nhất một tài khoản quản lý được phân quyền.");

        await presenter.SaveAsync();

        Assert.Equal(new[] { "Phải còn ít nhất một tài khoản quản lý được phân quyền." }, view.Warnings);
        Assert.True(presenter.HasChanges);
        Assert.True(presenter.CanSave);
        Assert.Equal(1, view.Shows);
    }

    [Fact]
    public async Task Removing_role_management_from_your_own_role_asks_first()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        host.CurrentUser.Session = new UserSession(5, "quanly", "Chị Hoa", "quản lý", new HashSet<string> { Permissions.RolesManage }, DateTimeOffset.Now);
        presenter.Toggle(Manager.Id, Permissions.RolesManage);
        view.ConfirmAnswer = false;

        await presenter.SaveAsync();

        Assert.Equal(new[] { RolePermissionPresenter.LoseOwnAccessQuestion }, view.DangerQuestions);
        Assert.DoesNotContain(host.Roles.Calls, c => c.StartsWith("save", StringComparison.Ordinal));
        Assert.True(presenter.HasChanges);

        view.ConfirmAnswer = true;
        await presenter.SaveAsync();
        Assert.Contains("save:2", host.Roles.Calls);
    }

    [Fact]
    public async Task Changes_to_other_roles_do_not_ask()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        host.CurrentUser.Session = new UserSession(5, "quanly", "Chị Hoa", "Quản lý", new HashSet<string> { Permissions.RolesManage }, DateTimeOffset.Now);
        presenter.Toggle(Manager.Id, Permissions.ReturnsCreate);
        presenter.Toggle(Cashier.Id, Permissions.RolesManage);

        await presenter.SaveAsync();

        Assert.Empty(view.Questions);
        Assert.Contains("save:2,3", host.Roles.Calls);
    }

    [Fact]
    public async Task Role_actions_wait_for_matrix_changes_to_be_saved()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        presenter.Toggle(Cashier.Id, Permissions.ReturnsCreate);

        await presenter.AddRoleAsync();
        await presenter.EditRoleAsync(Cashier);
        await presenter.DeleteRoleAsync(Accountant);

        Assert.Equal(3, view.Infos.Count);
        Assert.All(view.Infos, m => Assert.Equal(RolePermissionPresenter.SaveFirst, m));
        Assert.Empty(view.Editors);
        Assert.DoesNotContain("delete:4", host.Roles.Calls);
    }

    [Fact]
    public async Task Saved_role_dialog_toasts_and_reloads()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        await presenter.AddRoleAsync();
        await presenter.EditRoleAsync(Cashier);

        Assert.Equal(new[] { "<new>", "Thu ngân" }, view.Editors);
        Assert.Equal(new[] { "Đã thêm vai trò mới.", "Đã lưu vai trò Thu ngân." }, view.Successes);
        Assert.Equal(3, view.Shows);
    }

    [Fact]
    public async Task Cancelled_role_dialog_does_not_reload()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.EditorSaves = false;

        await presenter.AddRoleAsync();

        Assert.Single(view.Editors);
        Assert.Empty(view.Successes);
        Assert.Equal(1, view.Shows);
    }

    [Fact]
    public async Task Delete_confirms_in_red_and_only_for_unused_custom_roles()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        Assert.False(RolePermissionPresenter.CanDelete(Cashier));
        Assert.False(RolePermissionPresenter.CanDelete(Accountant with { UserCount = 1 }));
        Assert.True(RolePermissionPresenter.CanDelete(Accountant));
        Assert.False(RolePermissionPresenter.CanDelete(null));

        await presenter.DeleteRoleAsync(Cashier);
        Assert.Empty(view.Questions);

        view.ConfirmAnswer = false;
        await presenter.DeleteRoleAsync(Accountant);
        Assert.DoesNotContain("delete:4", host.Roles.Calls);

        view.ConfirmAnswer = true;
        await presenter.DeleteRoleAsync(Accountant);
        Assert.Contains("delete:4", host.Roles.Calls);
        Assert.Equal(new[] { RolePermissionPresenter.DeleteQuestion(Accountant), RolePermissionPresenter.DeleteQuestion(Accountant) }, view.DangerQuestions);
        Assert.Equal(new[] { "Đã xóa vai trò Kế toán." }, view.Successes);
        Assert.Equal(2, view.Shows);
    }

    [Fact]
    public async Task Delete_failure_warns_without_reloading()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        host.Roles.FailWith = Error.Conflict("Vai trò đang được sử dụng.");

        await presenter.DeleteRoleAsync(Accountant);

        Assert.Equal(new[] { "Vai trò đang được sử dụng." }, view.Warnings);
        Assert.Empty(view.Successes);
        Assert.Equal(1, view.Shows);
    }

    [Fact]
    public async Task Leaving_asks_only_when_there_are_unsaved_changes()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        Assert.True(presenter.ConfirmLeave());
        Assert.Empty(view.Questions);

        presenter.Toggle(Cashier.Id, Permissions.ReturnsCreate);
        presenter.Toggle(Manager.Id, Permissions.ReturnsCreate);
        view.ConfirmAnswer = false;
        Assert.False(presenter.ConfirmLeave());
        view.ConfirmAnswer = true;
        Assert.True(presenter.ConfirmLeave());

        Assert.Equal(2, view.DangerQuestions.Count);
        Assert.Equal(RolePermissionPresenter.LeaveQuestion(2), view.DangerQuestions[0]);
    }

    [Fact]
    public async Task Nothing_is_shown_once_the_screen_closed()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.IsAlive = false;

        Assert.False(await presenter.LoadAsync());

        Assert.Equal(0, view.Shows);
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void Column_header_shows_name_users_and_tooltip()
    {
        Assert.Equal("Thu ngân\n3 người dùng", RolePermissionPresenter.HeaderText(Cashier));
        Assert.Equal("Bán hàng tại quầy", RolePermissionPresenter.HeaderToolTip(Cashier));
        Assert.Equal("Kế toán", RolePermissionPresenter.HeaderToolTip(Accountant));
        Assert.Equal("Admin luôn có toàn bộ quyền.", RolePermissionPresenter.HeaderToolTip(Admin));
    }
}
