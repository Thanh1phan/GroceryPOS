using GroceryPOS.Application.Sales;

namespace GroceryPOS.Application.Abstractions;

/// <summary>
/// Where parked carts are kept between "Lưu tạm" and "Gọi lại đơn". They are counter-local working state, not
/// business records, so they live outside the database (Infrastructure keeps them in a JSON file per machine)
/// and survive an app restart or a crash.
/// </summary>
public interface IParkedOrderStore
{
    /// <summary>All parked orders, oldest first.</summary>
    Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default);

    Task<ParkedOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds the order, or replaces the one with the same id.</summary>
    Task SaveAsync(ParkedOrder order, CancellationToken cancellationToken = default);

    /// <summary>Removes the order; false when it was not there (e.g. already resumed on another tab).</summary>
    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
