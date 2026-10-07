using GroceryPOS.Domain.Common;

namespace GroceryPOS.Application.Abstractions;

/// <summary>Generic aggregate repository. Changes are persisted by <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default);

    void Add(T entity);

    void Remove(T entity);
}
