using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository base. Results are tracked, so services can change the loaded aggregates and save them through
/// <see cref="IUnitOfWork"/> (the same scoped <see cref="GroceryDbContext"/>). Child collections are configured as
/// auto-included navigations, so aggregates always load complete.
/// </summary>
internal class EfRepository<T> : IRepository<T> where T : Entity
{
    public EfRepository(GroceryDbContext context)
    {
        Context = context;
    }

    protected GroceryDbContext Context { get; }

    protected DbSet<T> Set => Context.Set<T>();

    /// <summary>Base query used by <see cref="GetByIdAsync"/> and <see cref="ListAsync"/>; override to add includes.</summary>
    protected virtual IQueryable<T> Query => Set;

    public virtual Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default) =>
        await Query.OrderBy(e => e.Id).ToListAsync(cancellationToken);

    public void Add(T entity) => Set.Add(entity);

    public void Remove(T entity) => Set.Remove(entity);

    /// <summary>Highest document code with the given prefix (codes are fixed-width, so ordinal order is numeric order).</summary>
    protected static Task<string?> LastCodeAsync(IQueryable<string> codes, string prefix, CancellationToken cancellationToken) =>
        codes.Where(c => c.StartsWith(prefix))
            .OrderByDescending(c => c)
            .FirstOrDefaultAsync(cancellationToken);
}
