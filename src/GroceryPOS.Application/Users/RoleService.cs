using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Users;

public interface IRoleService
{
    /// <summary>The permission catalog grouped by area, for the matrix rows. No permission needed.</summary>
    IReadOnlyList<PermissionGroupDto> GetPermissionCatalog();

    /// <summary>Roles with their permissions and how many accounts use them (system roles first). Requires <c>roles.manage</c>.</summary>
    Task<Result<IReadOnlyList<RoleDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Requires <c>roles.manage</c>. Names are unique (case-insensitive).</summary>
    Task<Result<RoleDto>> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>roles.manage</c>. System roles keep their name; Admin keeps every permission.</summary>
    Task<Result<RoleDto>> UpdateAsync(int roleId, SaveRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the permission matrix in one go (only changed roles are touched and audited). Requires <c>roles.manage</c>.
    /// Refused as a whole if it would leave no active account able to manage roles. Applies at each user's next sign-in.
    /// </summary>
    Task<Result> SavePermissionMatrixAsync(IReadOnlyList<RolePermissionsUpdate> updates, CancellationToken cancellationToken = default);

    /// <summary>Requires <c>roles.manage</c>. Only custom roles that no account uses can be deleted.</summary>
    Task<Result> DeleteAsync(int roleId, CancellationToken cancellationToken = default);
}

/// <summary>Roles (vai trò) and their permission sets.</summary>
public sealed class RoleService : IRoleService
{
    private static readonly IReadOnlyList<PermissionGroupDto> Catalog = Permissions.Groups
        .Select(g => new PermissionGroupDto(g.Title, g.Keys.Select(k => new PermissionItem(k, Permissions.DisplayNames[k])).ToList()))
        .ToList();

    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public RoleService(IRoleRepository roles, IUserRepository users, IUnitOfWork unitOfWork, ICurrentUser currentUser, IAuditService audit)
    {
        _roles = roles;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
    }

    public IReadOnlyList<PermissionGroupDto> GetPermissionCatalog() => Catalog;

    public async Task<Result<IReadOnlyList<RoleDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var roles = await _roles.ListAsync(cancellationToken);
        var counts = await UserCountsAsync(cancellationToken);
        IReadOnlyList<RoleDto> items = roles
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.IsSystem ? r.Id.ToString("D10") : r.Name, UserManagementService.Names)
            .Select(r => ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
        return Result.Success(items);
    }

    public async Task<Result<RoleDto>> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (FindUnknown(request.Permissions) is { } unknown)
        {
            return UserErrors.UnknownPermission(unknown);
        }

        if (await NameTakenAsync(request.Name, exceptRoleId: null, cancellationToken))
        {
            return UserErrors.DuplicateRoleName;
        }

        Role role;
        try
        {
            role = new Role(request.Name ?? string.Empty, request.Description);
            role.SetPermissions(request.Permissions);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _roles.Add(role);
        _audit.Record(AuditActions.RoleCreated, nameof(Role), role.Name, DescribePermissions(role.PermissionKeys));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(role, 0);
    }

    public async Task<Result<RoleDto>> UpdateAsync(int roleId, SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return UserErrors.RoleNotFound(roleId);
        }

        if (FindUnknown(request.Permissions) is { } unknown)
        {
            return UserErrors.UnknownPermission(unknown);
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (role.IsSystem && name != role.Name)
        {
            return UserErrors.SystemRoleRename;
        }

        if (!role.IsSystem && await NameTakenAsync(name, role.Id, cancellationToken))
        {
            return UserErrors.DuplicateRoleName;
        }

        var desired = request.Permissions.ToHashSet(StringComparer.Ordinal);
        if (IsLocked(role) && !desired.SetEquals(role.PermissionKeys))
        {
            return UserErrors.AdminPermissionsLocked;
        }

        if (await LeavesNoAdministratorAsync(new Dictionary<int, IReadOnlySet<string>> { [role.Id] = desired }, cancellationToken))
        {
            return UserErrors.LastAdministrator;
        }

        var before = $"{role.Name} – {role.Description ?? "-"}";
        var oldPermissions = role.PermissionKeys;
        try
        {
            if (!role.IsSystem)
            {
                role.Rename(name);
            }

            role.UpdateDescription(request.Description);
            if (!IsLocked(role))
            {
                role.SetPermissions(desired);
            }
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        var after = $"{role.Name} – {role.Description ?? "-"}";
        var changes = new List<string>();
        if (before != after)
        {
            changes.Add($"{before} → {after}");
        }

        if (DescribeDiff(oldPermissions, role.PermissionKeys) is { } diff)
        {
            changes.Add(diff);
        }

        if (changes.Count > 0)
        {
            _audit.Record(AuditActions.RoleUpdated, nameof(Role), role.Name, string.Join("; ", changes));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var counts = await UserCountsAsync(cancellationToken);
        return ToDto(role, counts.GetValueOrDefault(role.Id));
    }

    public async Task<Result> SavePermissionMatrixAsync(IReadOnlyList<RolePermissionsUpdate> updates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var changed = new List<(Role Role, HashSet<string> Desired)>();
        foreach (var update in updates)
        {
            var role = await _roles.GetByIdAsync(update.RoleId, cancellationToken);
            if (role is null)
            {
                return UserErrors.RoleNotFound(update.RoleId);
            }

            if (FindUnknown(update.Permissions) is { } unknown)
            {
                return UserErrors.UnknownPermission(unknown);
            }

            var desired = update.Permissions.ToHashSet(StringComparer.Ordinal);
            if (desired.SetEquals(role.PermissionKeys))
            {
                continue;
            }

            if (IsLocked(role))
            {
                return UserErrors.AdminPermissionsLocked;
            }

            changed.Add((role, desired));
        }

        if (changed.Count == 0)
        {
            return Result.Success();
        }

        var planned = changed.ToDictionary(c => c.Role.Id, c => (IReadOnlySet<string>)c.Desired);
        if (await LeavesNoAdministratorAsync(planned, cancellationToken))
        {
            return UserErrors.LastAdministrator;
        }

        foreach (var (role, desired) in changed)
        {
            var old = role.PermissionKeys;
            role.SetPermissions(desired);
            _audit.Record(AuditActions.RoleUpdated, nameof(Role), role.Name, DescribeDiff(old, role.PermissionKeys));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        var role = await _roles.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return UserErrors.RoleNotFound(roleId);
        }

        if (role.IsSystem)
        {
            return UserErrors.SystemRoleDelete;
        }

        var counts = await UserCountsAsync(cancellationToken);
        if (counts.GetValueOrDefault(role.Id) is var used and > 0)
        {
            return UserErrors.RoleInUse(used);
        }

        _roles.Remove(role);
        _audit.Record(AuditActions.RoleDeleted, nameof(Role), role.Name);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Admin always holds every permission (new permissions are granted to it at start-up).</summary>
    private static bool IsLocked(Role role) => role.IsSystem && role.Name == SystemRoles.Admin;

    private static string? FindUnknown(IEnumerable<string> permissions) => permissions.FirstOrDefault(p => !Permissions.IsKnown(p));

    /// <summary>True when, after the planned permission sets, no active account could manage roles any more.</summary>
    private async Task<bool> LeavesNoAdministratorAsync(IReadOnlyDictionary<int, IReadOnlySet<string>> planned, CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);
        var before = users.Any(UserRules.IsAdministrator);
        var after = users.Any(u => u.IsActive
                                   && (planned.TryGetValue(u.RoleId, out var set)
                                       ? set.Contains(Permissions.RolesManage)
                                       : u.Role.HasPermission(Permissions.RolesManage)));

        // Only refuse changes that remove the last administrator, not edits made while none exists.
        return before && !after;
    }

    private async Task<bool> NameTakenAsync(string? name, int? exceptRoleId, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        var roles = await _roles.ListAsync(cancellationToken);
        return roles.Any(r => r.Id != exceptRoleId && string.Equals(r.Name, trimmed, StringComparison.CurrentCultureIgnoreCase));
    }

    private async Task<Dictionary<int, int>> UserCountsAsync(CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);
        return users.GroupBy(u => u.RoleId).ToDictionary(g => g.Key, g => g.Count());
    }

    private static string DescribePermissions(IEnumerable<string> keys) =>
        string.Join(", ", keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => Permissions.DisplayNames[k]));

    private static string? DescribeDiff(IReadOnlySet<string> before, IReadOnlySet<string> after)
    {
        var added = after.Except(before).OrderBy(k => k, StringComparer.Ordinal).Select(k => "+" + Permissions.DisplayNames[k]);
        var removed = before.Except(after).OrderBy(k => k, StringComparer.Ordinal).Select(k => "−" + Permissions.DisplayNames[k]);
        var parts = added.Concat(removed).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static RoleDto ToDto(Role r, int userCount) =>
        new(r.Id, r.Name, r.Description, r.IsSystem, IsLocked(r), r.PermissionKeys, userCount);

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.RolesManage)
            ? null
            : Error.Forbidden("Bạn không có quyền quản lý vai trò & phân quyền.");
    }
}
