using GroceryPOS.Application.Common;
using GroceryPOS.Application.Users;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeRoleService : IRoleService
{
    public List<PermissionGroupDto> Catalog { get; } = new();
    public List<RoleDto> Items { get; } = new();
    public Error? FailWith { get; set; }

    /// <summary>Only <see cref="ListAsync"/> fails.</summary>
    public Error? ListFailsWith { get; set; }

    public List<string> Calls { get; } = new();
    public SaveRoleRequest? LastRequest { get; private set; }
    public IReadOnlyList<RolePermissionsUpdate>? LastMatrix { get; private set; }

    public IReadOnlyList<PermissionGroupDto> GetPermissionCatalog() => Catalog.ToList();

    public Task<Result<IReadOnlyList<RoleDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        var error = ListFailsWith ?? FailWith;
        return Task.FromResult(error is { } e ? Result.Failure<IReadOnlyList<RoleDto>>(e) : Result.Success<IReadOnlyList<RoleDto>>(Items.ToList()));
    }

    public Task<Result<RoleDto>> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<RoleDto>(e) : Result.Success(ToDto(99, request, isSystem: false)));
    }

    public Task<Result<RoleDto>> UpdateAsync(int roleId, SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"update:{roleId}:{request.Name}");
        LastRequest = request;
        return Task.FromResult(FailWith is { } e ? Result.Failure<RoleDto>(e) : Result.Success(ToDto(roleId, request, isSystem: false)));
    }

    public Task<Result> SavePermissionMatrixAsync(IReadOnlyList<RolePermissionsUpdate> updates, CancellationToken cancellationToken = default)
    {
        Calls.Add($"save:{string.Join(',', updates.Select(u => u.RoleId))}");
        LastMatrix = updates;
        if (FailWith is { } e)
        {
            return Task.FromResult(Result.Failure(e));
        }

        foreach (var update in updates)
        {
            var index = Items.FindIndex(r => r.Id == update.RoleId);
            Items[index] = Items[index] with { Permissions = update.Permissions.ToHashSet() };
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteAsync(int roleId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{roleId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure(e) : Result.Success());
    }

    private static RoleDto ToDto(int id, SaveRoleRequest r, bool isSystem) =>
        new(id, r.Name!, r.Description, isSystem, false, r.Permissions.ToHashSet(), 0);
}
