using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository : EfRepository<User>, IUserRepository
{
    public UserRepository(GroceryDbContext context) : base(context)
    {
    }

    // The role (with its permissions, auto-included) is needed for every sign-in and permission check.
    protected override IQueryable<User> Query => Set.Include(u => u.Role);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return Query.FirstOrDefaultAsync(u => u.Username == normalized, cancellationToken);
    }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return Set.AnyAsync(u => u.Username == normalized, cancellationToken);
    }
}

internal sealed class RoleRepository : EfRepository<Role>, IRoleRepository
{
    public RoleRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        return Set.FirstOrDefaultAsync(r => r.Name == trimmed, cancellationToken);
    }
}
