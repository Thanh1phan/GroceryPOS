using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Users;

/// <summary>The Vai trò &amp; phân quyền screen (permission matrix) as seen by its presenter.</summary>
public interface IRolePermissionView : IView
{
    /// <summary>The role whose column is selected, or null.</summary>
    int? SelectedRoleId { get; }

    /// <summary>Rebuilds the grid from <paramref name="matrix"/> and selects <paramref name="selectRoleId"/>'s column when still there.</summary>
    void ShowMatrix(PermissionMatrix matrix, int? selectRoleId);

    /// <summary>Repaints check marks and highlights after an edit (same rows and columns).</summary>
    void RefreshMatrix();

    /// <summary>Shows the hint line next to the toolbar; <paramref name="emphasis"/> when there is something to act on.</summary>
    void ShowStatus(string text, bool emphasis);

    /// <summary>Opens the add (<paramref name="role"/> = null) or edit role dialog; true when it was saved.</summary>
    bool OpenRoleEditor(RoleDto? role, IReadOnlyList<RoleDto> roles);
}

/// <summary>
/// Vai trò &amp; phân quyền logic: the permission matrix (toggle one permission or a whole group, undo, save every changed
/// role at once) and role add/edit/delete. Everything needs <c>roles.manage</c>. Admin's column is read-only; role
/// actions wait until matrix changes are saved or undone; saving a change that takes "Quản lý vai trò" away from your
/// own role asks first, because you could no longer open this screen after signing in again.
/// </summary>
public sealed class RolePermissionPresenter : Presenter<IRolePermissionView>
{
    public const string Title = "Vai trò & phân quyền";
    public const string NoPermission = "Bạn không có quyền quản lý vai trò & phân quyền.";
    public const string IdleHint = "Chọn một cột để sửa vai trò. Bấm vào dòng nhóm để bật/tắt cả nhóm.";
    public const string AdminLocked = "Vai trò Admin luôn có toàn bộ quyền, không thể thay đổi.";
    public const string SaveFirst = "Hãy lưu hoặc hoàn tác thay đổi phân quyền trước.";
    public const string Saved = "Đã lưu phân quyền. Người dùng nhận quyền mới khi đăng nhập lại.";

    private readonly ICurrentUser _currentUser;
    private bool _saving;

    public RolePermissionPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        CanManage = currentUser.HasPermission(Permissions.RolesManage);
    }

    /// <summary>Every action on this screen needs <c>roles.manage</c>.</summary>
    public bool CanManage { get; }

    /// <summary>The matrix being edited; null until loaded.</summary>
    public PermissionMatrix? Matrix { get; private set; }

    public bool HasChanges => Matrix is { IsDirty: true };

    public bool CanSave => CanManage && HasChanges && !_saving;

    /// <summary>The hint line: what to do, or how many roles have unsaved changes.</summary>
    public string StatusText => Matrix is { ChangedRoleCount: > 0 and var changed }
        ? $"Có thay đổi chưa lưu ở {changed} vai trò. Quyền mới áp dụng từ lần đăng nhập tiếp theo."
        : IdleHint;

    /// <summary>Only custom roles nobody uses can be deleted (the service checks again).</summary>
    public static bool CanDelete(RoleDto? role) => role is { IsSystem: false, UserCount: 0 };

    /// <summary>Column header: name and how many accounts use the role.</summary>
    public static string HeaderText(RoleDto role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return $"{role.Name}\n{role.UserCount} người dùng";
    }

    public static string HeaderToolTip(RoleDto role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.PermissionsLocked ? "Admin luôn có toàn bộ quyền." : role.Description ?? role.Name;
    }

    public static string DeleteQuestion(RoleDto role) => $"Xóa vai trò \"{role.Name}\"? Thao tác này không hoàn tác được.";

    public static string LeaveQuestion(int changedRoles) =>
        $"Có thay đổi phân quyền chưa lưu ở {changedRoles} vai trò. Bỏ các thay đổi này?";

    public const string LoseOwnAccessQuestion =
        "Thay đổi này bỏ quyền \"Quản lý vai trò & phân quyền\" khỏi vai trò của chính bạn.\n" +
        "Sau khi đăng nhập lại, bạn sẽ không mở được màn hình này nữa. Vẫn lưu?";

    /// <summary>Loads the catalog and the roles into the view, keeping the selected column. False after showing the error.</summary>
    public async Task<bool> LoadAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return false;
        }

        var selected = View.SelectedRoleId;
        var loaded = await UseAsync<IRoleService, Result<(IReadOnlyList<PermissionGroupDto>, IReadOnlyList<RoleDto>)>>(async roles =>
        {
            var list = await roles.ListAsync();
            return list.IsSuccess
                ? Result.Success((roles.GetPermissionCatalog(), list.Value))
                : Result.Failure<(IReadOnlyList<PermissionGroupDto>, IReadOnlyList<RoleDto>)>(list.Error);
        });
        if (!ViewAlive)
        {
            return false;
        }

        if (loaded.IsFailure)
        {
            View.ShowWarning(loaded.Error.Message, Title);
            return false;
        }

        var (catalog, roleList) = loaded.Value;
        Matrix = new PermissionMatrix(catalog, roleList);
        View.ShowMatrix(Matrix, selected);
        View.ShowStatus(StatusText, emphasis: false);
        return true;
    }

    /// <summary>Flips one permission of a role (ignored for Admin, with a hint).</summary>
    public void Toggle(int roleId, string permission)
    {
        ArgumentException.ThrowIfNullOrEmpty(permission);
        Edit(roleId, m => m.Toggle(roleId, permission));
    }

    /// <summary>Grants the whole group, or revokes it when the role already holds all of it.</summary>
    public void ToggleGroup(int roleId, PermissionGroupDto group)
    {
        ArgumentNullException.ThrowIfNull(group);
        Edit(roleId, m => m.ToggleGroup(roleId, group));
    }

    /// <summary>Discards every unsaved change.</summary>
    public void Undo()
    {
        if (Matrix is not { IsDirty: true } matrix)
        {
            return;
        }

        matrix.Reset();
        Changed();
    }

    /// <summary>Saves every changed role in one go, then reloads.</summary>
    public async Task SaveAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (!CanSave || Matrix is not { } matrix)
        {
            return;
        }

        if (LosesOwnAccess(matrix) && !View.Confirm(LoseOwnAccessQuestion, Title, "Vẫn lưu", danger: true))
        {
            return;
        }

        _saving = true;
        Result result;
        try
        {
            var changes = matrix.Changes;
            result = await UseAsync<IRoleService, Result>(roles => roles.SavePermissionMatrixAsync(changes));
        }
        finally
        {
            _saving = false;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            View.RefreshMatrix();
            return;
        }

        if (await LoadAsync())
        {
            View.ShowStatus(Saved, emphasis: false);
            View.ToastSuccess("Đã lưu phân quyền.");
        }
    }

    public Task AddRoleAsync() => OpenEditorAsync(null);

    public Task EditRoleAsync(RoleDto role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return OpenEditorAsync(role);
    }

    /// <summary>Deletes an unused custom role after a red confirmation, then reloads.</summary>
    public async Task DeleteRoleAsync(RoleDto role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!Ready() || !CanDelete(role))
        {
            return;
        }

        if (!View.Confirm(DeleteQuestion(role), Title, "Xóa vai trò", danger: true))
        {
            return;
        }

        var result = await UseAsync<IRoleService, Result>(roles => roles.DeleteAsync(role.Id));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess($"Đã xóa vai trò {role.Name}.");
        await LoadAsync();
    }

    /// <summary>
    /// Asked before leaving the screen (another menu item, sign-out, closing the app): true when there is nothing
    /// unsaved or the user chose to discard it.
    /// </summary>
    public bool ConfirmLeave() =>
        Matrix is not { IsDirty: true } matrix
        || View.Confirm(LeaveQuestion(matrix.ChangedRoleCount), Title, "Bỏ thay đổi", danger: true);

    private void Edit(int roleId, Func<PermissionMatrix, bool> change)
    {
        if (!CanManage || _saving || Matrix is not { } matrix)
        {
            return;
        }

        if (change(matrix))
        {
            Changed();
        }
        else if (!matrix.CanEdit(roleId))
        {
            View.ShowStatus(AdminLocked, emphasis: true);
        }
    }

    private void Changed()
    {
        View.RefreshMatrix();
        View.ShowStatus(StatusText, emphasis: HasChanges);
    }

    /// <summary>Role actions need the permission, a loaded matrix and no pending matrix edits.</summary>
    private bool Ready()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return false;
        }

        if (Matrix is null)
        {
            return false;
        }

        if (Matrix.IsDirty)
        {
            View.ShowInfo(SaveFirst, Title);
            return false;
        }

        return true;
    }

    private async Task OpenEditorAsync(RoleDto? role)
    {
        if (!Ready() || !View.OpenRoleEditor(role, Matrix!.Roles) || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess(role is null ? "Đã thêm vai trò mới." : $"Đã lưu vai trò {role.Name}.");
        await LoadAsync();
    }

    /// <summary>True when the pending changes remove <c>roles.manage</c> from the signed-in user's own role.</summary>
    private bool LosesOwnAccess(PermissionMatrix matrix)
    {
        if (_currentUser.Session?.RoleName is not { } roleName
            || matrix.Roles.FirstOrDefault(r => string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase)) is not { } own)
        {
            return false;
        }

        return own.Permissions.Contains(Permissions.RolesManage) && !matrix.Has(own.Id, Permissions.RolesManage);
    }
}
