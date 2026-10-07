using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Identity;

public sealed class Role : AuditableEntity
{
    private readonly List<RolePermission> _permissions = new();

    private Role()
    {
    }

    public Role(string name, string? description = null, bool isSystem = false)
    {
        Rename(name);
        Description = Guard.Optional(description);
        IsSystem = isSystem;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>System roles cannot be deleted or renamed (permissions may still be edited, except Admin).</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public IReadOnlySet<string> PermissionKeys => _permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);

    public void Rename(string name)
    {
        if (IsSystem)
        {
            throw new DomainException("role.system", "Không thể đổi tên vai trò hệ thống.");
        }

        Name = Guard.NotEmpty(name, "role.name", "Tên vai trò không được để trống", 50);
    }

    public void UpdateDescription(string? description) => Description = Guard.Optional(description);

    public bool HasPermission(string permission) => _permissions.Any(p => p.Permission == permission);

    public void Grant(string permission)
    {
        if (!Identity.Permissions.IsKnown(permission))
        {
            throw new DomainException("role.permission", $"Quyền không hợp lệ: {permission}");
        }

        if (!HasPermission(permission))
        {
            _permissions.Add(new RolePermission(permission));
        }
    }

    public void Revoke(string permission)
    {
        if (IsSystem && Name == SystemRoles.Admin)
        {
            throw new DomainException("role.admin", "Không thể thu hồi quyền của vai trò Admin.");
        }

        _permissions.RemoveAll(p => p.Permission == permission);
    }

    /// <summary>Replaces the permission set in one go (used by the role editor).</summary>
    public void SetPermissions(IEnumerable<string> permissions)
    {
        var desired = permissions.Distinct(StringComparer.Ordinal).ToList();
        foreach (var existing in PermissionKeys.Except(desired).ToList())
        {
            Revoke(existing);
        }

        foreach (var permission in desired)
        {
            Grant(permission);
        }
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(string permission)
    {
        Permission = permission;
    }

    public int RoleId { get; private set; }
    public string Permission { get; private set; } = string.Empty;
}
