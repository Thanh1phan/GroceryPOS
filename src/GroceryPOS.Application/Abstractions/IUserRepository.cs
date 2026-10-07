using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Abstractions;

public interface IUserRepository : IRepository<User>
{
    /// <summary>Finds a user by (normalized) username, including the role and its permissions.</summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);
}

public interface IRoleRepository : IRepository<Role>
{
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
