using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Abstractions;

public interface IStockMovementRepository : IRepository<StockMovement>
{
    /// <summary>
    /// Movements of one product ordered by time (then id), optionally limited to
    /// <paramref name="from"/> (inclusive) .. <paramref name="to"/> (exclusive).
    /// </summary>
    Task<IReadOnlyList<StockMovement>> ListByProductAsync(
        int productId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);
}
