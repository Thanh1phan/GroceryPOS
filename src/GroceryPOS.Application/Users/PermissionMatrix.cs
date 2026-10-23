namespace GroceryPOS.Application.Users;

/// <summary>How many permissions of a group a role holds (drives the tri-state group checkbox).</summary>
public enum GroupCheck
{
    None,
    Some,
    All,
}

/// <summary>
/// Editable state behind the permission matrix screen (rows = permissions grouped by area, columns = roles).
/// Pure and UI-free: the screen renders it and forwards clicks; <see cref="Changes"/> feeds
/// <see cref="IRoleService.SavePermissionMatrixAsync"/>. Roles with <see cref="RoleDto.PermissionsLocked"/> (Admin) never change.
/// </summary>
public sealed class PermissionMatrix
{
    private readonly Dictionary<int, IReadOnlySet<string>> _original;
    private readonly Dictionary<int, HashSet<string>> _working;

    public PermissionMatrix(IReadOnlyList<PermissionGroupDto> groups, IReadOnlyList<RoleDto> roles)
    {
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));
        Roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _original = roles.ToDictionary(r => r.Id, r => r.Permissions);
        _working = roles.ToDictionary(r => r.Id, r => r.Permissions.ToHashSet(StringComparer.Ordinal));
    }

    public IReadOnlyList<PermissionGroupDto> Groups { get; }

    public IReadOnlyList<RoleDto> Roles { get; }

    public bool IsDirty => Roles.Any(r => IsRoleChanged(r.Id));

    public int ChangedRoleCount => Roles.Count(r => IsRoleChanged(r.Id));

    /// <summary>The full desired permission set of every changed role.</summary>
    public IReadOnlyList<RolePermissionsUpdate> Changes => Roles
        .Where(r => IsRoleChanged(r.Id))
        .Select(r => new RolePermissionsUpdate(r.Id, _working[r.Id].OrderBy(k => k, StringComparer.Ordinal).ToList()))
        .ToList();

    public bool CanEdit(int roleId) => Role(roleId) is { PermissionsLocked: false };

    public bool Has(int roleId, string permission) => _working.TryGetValue(roleId, out var set) && set.Contains(permission);

    /// <summary>True when the cell differs from what is saved (the screen highlights it).</summary>
    public bool IsChanged(int roleId, string permission) =>
        _original.TryGetValue(roleId, out var original) && original.Contains(permission) != Has(roleId, permission);

    public bool IsRoleChanged(int roleId) => !_working[roleId].SetEquals(_original[roleId]);

    public GroupCheck StateOf(int roleId, PermissionGroupDto group)
    {
        var held = group.Items.Count(i => Has(roleId, i.Key));
        return held == 0 ? GroupCheck.None : held == group.Items.Count ? GroupCheck.All : GroupCheck.Some;
    }

    /// <summary>Flips one permission. Returns false when the role is locked or unknown.</summary>
    public bool Toggle(int roleId, string permission)
    {
        if (!CanEdit(roleId))
        {
            return false;
        }

        var set = _working[roleId];
        if (!set.Remove(permission))
        {
            set.Add(permission);
        }

        return true;
    }

    /// <summary>Grants the whole group, or revokes it when the role already holds all of it.</summary>
    public bool ToggleGroup(int roleId, PermissionGroupDto group)
    {
        if (!CanEdit(roleId))
        {
            return false;
        }

        var set = _working[roleId];
        if (StateOf(roleId, group) == GroupCheck.All)
        {
            set.ExceptWith(group.Items.Select(i => i.Key));
        }
        else
        {
            set.UnionWith(group.Items.Select(i => i.Key));
        }

        return true;
    }

    /// <summary>Discards every unsaved change.</summary>
    public void Reset()
    {
        foreach (var (roleId, original) in _original)
        {
            _working[roleId] = original.ToHashSet(StringComparer.Ordinal);
        }
    }

    private RoleDto? Role(int roleId) => Roles.FirstOrDefault(r => r.Id == roleId);
}
