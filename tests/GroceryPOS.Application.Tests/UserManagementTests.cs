using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Application.Users;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

/// <summary>Shared store: the four system roles and three accounts (admin, quanly, thungan).</summary>
public abstract class IdentityTestBase
{
    protected const string Password = "matkhau123";

    private protected readonly InMemoryUserRepository Users = new();
    private protected readonly InMemoryRoleRepository Roles = new();
    private protected readonly FakeUnitOfWork Uow = new();
    private protected readonly FakePasswordHasher Hasher = new();
    private protected readonly UserSessionStore Session = new();
    private protected readonly RecordingAuditService Audit = new();
    private protected readonly FakeClock Clock = new();

    protected IdentityTestBase()
    {
        foreach (var role in SystemRoles.CreateDefaults())
        {
            Roles.Add(role);
        }

        Admin = AddUser("admin", "Quản trị viên", SystemRoles.Admin);
        Manager = AddUser("quanly", "Lê Văn Cường", SystemRoles.Manager);
        Cashier = AddUser("thungan", "Trần Thị Bình", SystemRoles.Cashier);
        SignInAs(Admin);
    }

    protected User Admin { get; }
    protected User Manager { get; }
    protected User Cashier { get; }

    protected Role Role(string name) => Roles.ListAsync().Result.Single(r => r.Name == name);

    protected User AddUser(string username, string fullName, string roleName)
    {
        var user = new User(username, fullName, Hasher.Hash(Password), Role(roleName));
        Users.Add(user);
        return user;
    }

    protected void SignInAs(User user) => Session.SignIn(new UserSession(
        user.Id, user.Username, user.FullName, user.Role.Name, user.Role.PermissionKeys, Clock.Now));
}

public class UserManagementServiceTests : IdentityTestBase
{
    private UserManagementService CreateSut() => new(Users, Roles, Uow, Hasher, Session, Audit, Clock);

    [Fact]
    public async Task List_needs_users_view_and_flags_current_user_and_lockouts()
    {
        for (var i = 0; i < LockoutPolicy.Default.MaxFailedAttempts; i++)
        {
            Cashier.RegisterFailedLogin(Clock.Now, LockoutPolicy.Default);
        }

        Manager.Deactivate();
        var result = await CreateSut().ListAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("quanly", result.Value[^1].Username); // inactive accounts last
        Assert.True(result.Value.Single(u => u.Username == "admin").IsCurrentUser);
        var cashier = result.Value.Single(u => u.Username == "thungan");
        Assert.True(cashier.IsLockedOut);
        Assert.Equal("Tạm khóa", cashier.Status);

        SignInAs(Cashier);
        Assert.Equal("forbidden", (await CreateSut().ListAsync()).Error.Code);
    }

    [Fact]
    public async Task Create_normalizes_username_forces_password_change_and_audits()
    {
        var result = await CreateSut().CreateAsync(new CreateUserRequest(" ThuNgan2 ", "Phạm Thị Dung", Role(SystemRoles.Cashier).Id, "tamthoi123", "tamthoi123"));

        Assert.True(result.IsSuccess);
        Assert.Equal("thungan2", result.Value.Username);
        Assert.True(result.Value.MustChangePassword);
        Assert.Equal("Chờ đổi mật khẩu", result.Value.Status);
        var saved = await Users.GetByUsernameAsync("thungan2");
        Assert.True(Hasher.Verify("tamthoi123", saved!.PasswordHash));
        Assert.Contains(AuditActions.UserCreated, Audit.Actions);
        Assert.Equal(1, Uow.SaveCount);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("thu ngan")]
    [InlineData("thungân")]
    public async Task Create_rejects_bad_usernames(string username)
    {
        var result = await CreateSut().CreateAsync(new CreateUserRequest(username, "A", Role(SystemRoles.Cashier).Id, Password, Password));

        Assert.Equal(UserErrors.InvalidUsername, result.Error);
    }

    [Fact]
    public async Task Create_rejects_duplicates_and_weak_passwords()
    {
        var sut = CreateSut();
        var cashierRole = Role(SystemRoles.Cashier).Id;

        Assert.Equal(UserErrors.DuplicateUsername, (await sut.CreateAsync(new CreateUserRequest("THUNGAN", "B", cashierRole, Password, Password))).Error);
        Assert.Equal("validation", (await sut.CreateAsync(new CreateUserRequest("moi", "B", cashierRole, "12345678", "12345678"))).Error.Code);
        Assert.Equal(0, Uow.SaveCount);
    }

    [Fact]
    public async Task Manager_cannot_create_admins_or_touch_admin_accounts()
    {
        SignInAs(Manager);
        var sut = CreateSut();

        var create = await sut.CreateAsync(new CreateUserRequest("admin2", "X", Role(SystemRoles.Admin).Id, Password, Password));
        var reset = await sut.ResetPasswordAsync(Admin.Id, "matkhaumoi1");
        var deactivate = await sut.DeactivateAsync(Admin.Id);
        var options = await sut.ListRoleOptionsAsync();

        Assert.Equal(UserErrors.RoleOutOfReach, create.Error);
        Assert.Equal(UserErrors.RoleOutOfReach, reset.Error);
        Assert.Equal(UserErrors.RoleOutOfReach, deactivate.Error);
        Assert.False(options.Value.Single(o => o.Name == SystemRoles.Admin).CanAssign);
        Assert.True(options.Value.Single(o => o.Name == SystemRoles.Cashier).CanAssign);
        Assert.Equal(SystemRoles.Admin, options.Value[0].Name);
    }

    [Fact]
    public async Task Manager_can_reset_cashier_password_which_unlocks_and_forces_change()
    {
        for (var i = 0; i < LockoutPolicy.Default.MaxFailedAttempts; i++)
        {
            Cashier.RegisterFailedLogin(Clock.Now, LockoutPolicy.Default);
        }

        SignInAs(Manager);
        var result = await CreateSut().ResetPasswordAsync(Cashier.Id, "matkhaumoi1");

        Assert.True(result.IsSuccess);
        Assert.False(Cashier.IsLockedOut(Clock.Now));
        Assert.True(Cashier.MustChangePassword);
        Assert.True(Hasher.Verify("matkhaumoi1", Cashier.PasswordHash));
        Assert.Contains(AuditActions.PasswordReset, Audit.Actions);
    }

    [Fact]
    public async Task Nobody_resets_own_password_changes_own_role_or_deactivates_self()
    {
        var sut = CreateSut();

        Assert.Equal(UserErrors.SelfReset, (await sut.ResetPasswordAsync(Admin.Id, "matkhaumoi1")).Error);
        Assert.Equal(UserErrors.SelfRoleChange, (await sut.UpdateAsync(Admin.Id, new UpdateUserRequest("Admin", Role(SystemRoles.Manager).Id))).Error);
        Assert.Equal(UserErrors.SelfDeactivate, (await sut.DeactivateAsync(Admin.Id)).Error);

        var rename = await sut.UpdateAsync(Admin.Id, new UpdateUserRequest("Chủ cửa hàng", Admin.RoleId));
        Assert.True(rename.IsSuccess);
        Assert.Equal("Chủ cửa hàng", Admin.FullName);
    }

    [Fact]
    public async Task Last_administrator_cannot_be_deactivated_or_demoted()
    {
        var second = AddUser("admin2", "Phó quản trị", SystemRoles.Admin);
        var sut = CreateSut();

        // Two administrators: one may be stopped.
        Assert.True((await sut.DeactivateAsync(second.Id)).IsSuccess);
        Assert.True((await sut.ActivateAsync(second.Id)).IsSuccess);

        // The signed-in admin's own account was stopped meanwhile (stale session): "second" is the last administrator.
        Admin.Deactivate();
        Assert.Equal(UserErrors.LastAdministrator, (await sut.DeactivateAsync(second.Id)).Error);
        Assert.Equal(UserErrors.LastAdministrator, (await sut.UpdateAsync(second.Id, new UpdateUserRequest("X", Role(SystemRoles.Manager).Id))).Error);
        Assert.True(second.IsActive);
        Assert.Equal(SystemRoles.Admin, second.Role.Name);
    }

    [Fact]
    public async Task Update_changes_role_and_audits_the_difference()
    {
        var result = await CreateSut().UpdateAsync(Cashier.Id, new UpdateUserRequest("Trần Thị Bình", Role(SystemRoles.Storekeeper).Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(SystemRoles.Storekeeper, result.Value.RoleName);
        var entry = Assert.Single(Audit.Entries, e => e.Action == AuditActions.UserUpdated);
        Assert.Contains("Thu ngân → Trần Thị Bình – Thủ kho", entry.Details);
    }

    [Fact]
    public async Task Unlock_and_reactivate_are_audited_once()
    {
        for (var i = 0; i < LockoutPolicy.Default.MaxFailedAttempts; i++)
        {
            Cashier.RegisterFailedLogin(Clock.Now, LockoutPolicy.Default);
        }

        var sut = CreateSut();
        Assert.True((await sut.UnlockAsync(Cashier.Id)).IsSuccess);
        Assert.True((await sut.UnlockAsync(Cashier.Id)).IsSuccess);
        Assert.True((await sut.DeactivateAsync(Cashier.Id)).IsSuccess);
        Assert.True((await sut.ActivateAsync(Cashier.Id)).IsSuccess);

        Assert.False(Cashier.IsLockedOut(Clock.Now));
        Assert.Single(Audit.Entries, e => e.Action == AuditActions.UserUnlocked);
        Assert.Equal(2, Audit.Entries.Count(e => e.Action == AuditActions.UserStatusChanged));
    }

    [Fact]
    public async Task Unknown_user_and_missing_permission_are_reported()
    {
        Assert.Equal("not_found", (await CreateSut().UnlockAsync(999)).Error.Code);

        SignInAs(Cashier);
        Assert.Equal("forbidden", (await CreateSut().UnlockAsync(Manager.Id)).Error.Code);
    }
}

public class RoleServiceTests : IdentityTestBase
{
    private RoleService CreateSut() => new(Roles, Users, Uow, Session, Audit);

    [Fact]
    public async Task List_shows_system_roles_first_with_user_counts_and_admin_locked()
    {
        AddUser("thungan2", "Thu ngân 2", SystemRoles.Cashier);
        await CreateSut().CreateAsync(new SaveRoleRequest("Kế toán", null, new[] { Permissions.ReportsView }));

        var roles = (await CreateSut().ListAsync()).Value;

        Assert.Equal(new[] { SystemRoles.Admin, SystemRoles.Manager, SystemRoles.Cashier, SystemRoles.Storekeeper, "Kế toán" }, roles.Select(r => r.Name));
        Assert.Equal(2, roles.Single(r => r.Name == SystemRoles.Cashier).UserCount);
        Assert.True(roles[0].PermissionsLocked);
        Assert.False(roles[1].PermissionsLocked);
    }

    [Fact]
    public async Task Only_role_managers_may_use_it()
    {
        SignInAs(Manager);

        Assert.Equal("forbidden", (await CreateSut().ListAsync()).Error.Code);
    }

    [Fact]
    public async Task Create_rejects_duplicate_names_case_insensitively_and_unknown_permissions()
    {
        var sut = CreateSut();

        Assert.Equal(UserErrors.DuplicateRoleName, (await sut.CreateAsync(new SaveRoleRequest("THU NGÂN", null, Array.Empty<string>()))).Error);
        Assert.Equal("validation", (await sut.CreateAsync(new SaveRoleRequest("Kế toán", null, new[] { "x.y" }))).Error.Code);

        var created = await sut.CreateAsync(new SaveRoleRequest(" Kế toán ", "Xem báo cáo cuối ngày", new[] { Permissions.ReportsView, Permissions.DashboardView }));
        Assert.True(created.IsSuccess);
        Assert.Equal("Kế toán", created.Value.Name);
        Assert.Contains(AuditActions.RoleCreated, Audit.Actions);
    }

    [Fact]
    public async Task System_roles_keep_their_name_and_admin_keeps_every_permission()
    {
        var sut = CreateSut();
        var cashier = Role(SystemRoles.Cashier);
        var admin = Role(SystemRoles.Admin);

        Assert.Equal(UserErrors.SystemRoleRename, (await sut.UpdateAsync(cashier.Id, new SaveRoleRequest("Bán hàng", null, cashier.PermissionKeys))).Error);
        Assert.Equal(UserErrors.AdminPermissionsLocked, (await sut.UpdateAsync(admin.Id, new SaveRoleRequest(SystemRoles.Admin, null, new[] { Permissions.DashboardView }))).Error);

        var withVoid = cashier.PermissionKeys.Append(Permissions.SalesVoid).ToList();
        var updated = await sut.UpdateAsync(cashier.Id, new SaveRoleRequest(SystemRoles.Cashier, "Nhân viên quầy", withVoid));
        Assert.True(updated.IsSuccess);
        Assert.True(cashier.HasPermission(Permissions.SalesVoid));
        var entry = Assert.Single(Audit.Entries, e => e.Action == AuditActions.RoleUpdated);
        Assert.Contains("+Hủy hóa đơn", entry.Details);
    }

    [Fact]
    public async Task Matrix_save_touches_only_changed_roles()
    {
        var manager = Role(SystemRoles.Manager);
        var cashier = Role(SystemRoles.Cashier);
        var admin = Role(SystemRoles.Admin);
        var newCashier = cashier.PermissionKeys.Where(p => p != Permissions.CustomersManage).Append(Permissions.SalesDiscount).ToList();

        var result = await CreateSut().SavePermissionMatrixAsync(new[]
        {
            new RolePermissionsUpdate(admin.Id, admin.PermissionKeys.ToList()),
            new RolePermissionsUpdate(manager.Id, manager.PermissionKeys.ToList()),
            new RolePermissionsUpdate(cashier.Id, newCashier),
        });

        Assert.True(result.IsSuccess);
        Assert.True(cashier.HasPermission(Permissions.SalesDiscount));
        Assert.False(cashier.HasPermission(Permissions.CustomersManage));
        var entry = Assert.Single(Audit.Entries);
        Assert.Equal("+Giảm giá khi bán, −Quản lý khách hàng", entry.Details);
        Assert.Equal(1, Uow.SaveCount);
    }

    [Fact]
    public async Task Matrix_save_refuses_to_remove_the_last_administrator()
    {
        var custom = new Role("Chủ cửa hàng");
        Roles.Add(custom);
        custom.SetPermissions(new[] { Permissions.RolesManage, Permissions.UsersManage });
        var owner = AddUser("chucuahang", "Chủ", "Chủ cửa hàng");
        Admin.Deactivate();
        SignInAs(owner);

        var result = await CreateSut().SavePermissionMatrixAsync(new[] { new RolePermissionsUpdate(custom.Id, new[] { Permissions.UsersManage }) });

        Assert.Equal(UserErrors.LastAdministrator, result.Error);
        Assert.True(custom.HasPermission(Permissions.RolesManage));
        Assert.Equal(0, Uow.SaveCount);
    }

    [Fact]
    public async Task Delete_only_unused_custom_roles()
    {
        var sut = CreateSut();
        var custom = (await sut.CreateAsync(new SaveRoleRequest("Kế toán", null, new[] { Permissions.ReportsView }))).Value;
        var accountant = AddUser("ketoan", "Kế toán", "Kế toán");

        Assert.Equal(UserErrors.SystemRoleDelete, (await sut.DeleteAsync(Role(SystemRoles.Storekeeper).Id)).Error);
        Assert.Equal("conflict", (await sut.DeleteAsync(custom.Id)).Error.Code);

        Users.Remove(accountant);
        Assert.True((await sut.DeleteAsync(custom.Id)).IsSuccess);
        Assert.Null(await Roles.GetByIdAsync(custom.Id));
        Assert.Contains(AuditActions.RoleDeleted, Audit.Actions);
    }

    [Fact]
    public void Catalog_lists_every_permission_once_with_vietnamese_labels()
    {
        var items = CreateSut().GetPermissionCatalog().SelectMany(g => g.Items).ToList();

        Assert.Equal(Permissions.All.Count, items.Count);
        Assert.Contains(items, i => i.Key == Permissions.SalesVoid && i.DisplayName == "Hủy hóa đơn");
    }
}

public class UserValidatorTests
{
    [Fact]
    public void Temporary_passwords_meet_the_policy_and_differ()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => Authentication.PasswordPolicy.GenerateTemporary()).ToList();

        Assert.All(passwords, p => Assert.Null(Authentication.PasswordPolicy.Validate(p)));
        Assert.True(passwords.Distinct().Count() > 45);
    }

    [Fact]
    public void Create_user_checks_username_password_policy_and_confirmation()
    {
        var validator = new CreateUserRequestValidator();

        var errors = validator.Validate(new CreateUserRequest("a b", "", 0, "abc", "abd")).ToFieldErrors();

        Assert.Contains(nameof(CreateUserRequest.Username), errors.Keys);
        Assert.Contains(nameof(CreateUserRequest.FullName), errors.Keys);
        Assert.Contains(nameof(CreateUserRequest.RoleId), errors.Keys);
        Assert.Contains(nameof(CreateUserRequest.Password), errors.Keys);
        Assert.Contains(nameof(CreateUserRequest.ConfirmPassword), errors.Keys);
        Assert.True(validator.Validate(new CreateUserRequest("thungan.2", "Bình", 3, "matkhau123", "matkhau123")).IsValid);
    }

    [Fact]
    public void Reset_password_and_role_requests_are_checked()
    {
        Assert.False(new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest("matkhau123", "matkhau124")).IsValid);
        Assert.True(new ResetPasswordRequestValidator().Validate(new ResetPasswordRequest("matkhau123", "matkhau123")).IsValid);
        Assert.False(new SaveRoleRequestValidator().Validate(new SaveRoleRequest(" ", null, new[] { "bad.key" })).IsValid);
        Assert.True(new SaveRoleRequestValidator().Validate(new SaveRoleRequest("Kế toán", null, new[] { Permissions.ReportsView })).IsValid);
    }
}

public class PermissionMatrixTests
{
    private static readonly PermissionGroupDto Sales = new("Bán hàng", new[]
    {
        new PermissionItem(Permissions.SalesCreate, "Bán hàng"),
        new PermissionItem(Permissions.SalesDiscount, "Giảm giá khi bán"),
    });

    private static PermissionMatrix Create() => new(new[] { Sales }, new[]
    {
        new RoleDto(1, SystemRoles.Admin, null, true, true, new HashSet<string> { Permissions.SalesCreate, Permissions.SalesDiscount }, 1),
        new RoleDto(3, SystemRoles.Cashier, null, true, false, new HashSet<string> { Permissions.SalesCreate }, 2),
    });

    [Fact]
    public void Toggle_tracks_changes_and_reset_discards_them()
    {
        var matrix = Create();

        Assert.True(matrix.Toggle(3, Permissions.SalesDiscount));
        Assert.True(matrix.IsChanged(3, Permissions.SalesDiscount));
        Assert.False(matrix.IsChanged(3, Permissions.SalesCreate));
        var change = Assert.Single(matrix.Changes);
        Assert.Equal(new[] { Permissions.SalesCreate, Permissions.SalesDiscount }.OrderBy(k => k, StringComparer.Ordinal), change.Permissions);

        Assert.True(matrix.Toggle(3, Permissions.SalesDiscount));
        Assert.False(matrix.IsDirty);

        matrix.Toggle(3, Permissions.SalesCreate);
        matrix.Reset();
        Assert.True(matrix.Has(3, Permissions.SalesCreate));
        Assert.Equal(0, matrix.ChangedRoleCount);
    }

    [Fact]
    public void Group_toggle_fills_then_clears_and_reports_tri_state()
    {
        var matrix = Create();
        Assert.Equal(GroupCheck.Some, matrix.StateOf(3, Sales));

        matrix.ToggleGroup(3, Sales);
        Assert.Equal(GroupCheck.All, matrix.StateOf(3, Sales));

        matrix.ToggleGroup(3, Sales);
        Assert.Equal(GroupCheck.None, matrix.StateOf(3, Sales));
    }

    [Fact]
    public void Locked_roles_cannot_change()
    {
        var matrix = Create();

        Assert.False(matrix.Toggle(1, Permissions.SalesCreate));
        Assert.False(matrix.ToggleGroup(1, Sales));
        Assert.True(matrix.Has(1, Permissions.SalesCreate));
        Assert.False(matrix.CanEdit(1));
    }
}
