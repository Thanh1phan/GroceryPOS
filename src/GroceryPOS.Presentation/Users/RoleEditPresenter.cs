using FluentValidation;
using GroceryPOS.Application.Users;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Users;

/// <summary>
/// The add/edit role dialog as seen by its presenter. Field names are <see cref="SaveRoleRequest"/> property names
/// (<c>Name</c>, <c>Description</c>).
/// </summary>
public interface IRoleEditView : IView
{
    string RoleName { get; }

    string Description { get; }

    /// <summary>The role whose permissions a new role starts from (add only); null or 0 = start empty.</summary>
    int? CopyFromRoleId { get; }

    /// <summary>Moves focus to the field for <paramref name="field"/>.</summary>
    void FocusField(string field);
}

/// <summary>An entry of the "Sao chép quyền từ" list (Id 0 = start with no permissions).</summary>
public sealed record RoleCopySource(int Id, string Name);

/// <summary>
/// Add/edit role logic: name and description only – permissions are edited in the matrix. A new role can start from
/// another role's permissions; an edited role keeps the permissions it has. System role names cannot change.
/// </summary>
public sealed class RoleEditPresenter : Presenter<IRoleEditView>
{
    public const string StartEmpty = "(Không – bắt đầu trống)";

    private IReadOnlyList<RoleDto> _roles = Array.Empty<RoleDto>();

    public RoleEditPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    /// <summary>The role being edited, or null when adding.</summary>
    public RoleDto? Role { get; private set; }

    public bool IsNew => Role is null;

    public string Title => Role is null ? "Thêm vai trò" : $"Sửa vai trò – {Role.Name}";

    /// <summary>System roles (Admin, Quản lý, Thu ngân, Thủ kho) keep their name.</summary>
    public bool NameLocked => Role is { IsSystem: true };

    public string? NameHelperText => NameLocked ? "Vai trò hệ thống – không đổi được tên." : null;

    /// <summary>"Sao chép quyền từ" choices (add only): start empty, then every existing role.</summary>
    public IReadOnlyList<RoleCopySource> CopySources =>
        IsNew ? new[] { new RoleCopySource(0, StartEmpty) }.Concat(_roles.Select(r => new RoleCopySource(r.Id, r.Name))).ToList()
              : Array.Empty<RoleCopySource>();

    /// <summary>Chooses what the dialog edits and which roles a new one may copy from; call before the view is built.</summary>
    public RoleEditPresenter For(RoleDto? role, IReadOnlyList<RoleDto> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        Role = role;
        _roles = roles;
        return this;
    }

    /// <summary>The permissions saved with the role: its own on edit, the copied role's (or none) on add.</summary>
    public IReadOnlyList<string> PermissionsFor(int? copyFromRoleId) =>
        Role is not null
            ? Role.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList()
            : _roles.FirstOrDefault(r => r.Id == copyFromRoleId)?.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList()
              ?? new List<string>();

    /// <summary>Validates and saves; returns the message for the dialog's error banner, or null when saved.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var name = NameLocked ? Role!.Name : View.RoleName.Trim();
        var request = new SaveRoleRequest(name, FormInput.Optional(View.Description), PermissionsFor(View.CopyFromRoleId));
        if (FormInput.FirstError(services.GetRequiredService<IValidator<SaveRoleRequest>>(), request) is { } invalid)
        {
            View.FocusField(invalid.Field);
            return invalid.Message;
        }

        var roles = services.GetRequiredService<IRoleService>();
        var result = Role is null ? await roles.CreateAsync(request) : await roles.UpdateAsync(Role.Id, request);
        return result.IsSuccess ? null : result.Error.Message;
    });
}
