using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Abstractions;

public interface ISupplierRepository : IRepository<Supplier>
{
    /// <summary>Finds a supplier by its code (stored upper-case).</summary>
    Task<Supplier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}

public sealed record GoodsReceiptFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? SupplierId = null,
    GoodsReceiptStatus? Status = null);

public interface IGoodsReceiptRepository : IRepository<GoodsReceipt>
{
    /// <summary>Loads a receipt including its lines. (<see cref="IRepository{T}.GetByIdAsync"/> must also include them.)</summary>
    Task<GoodsReceipt?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Highest receipt code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receipts (with lines) whose <see cref="GoodsReceipt.ReceiptDate"/> lies in [From, To), newest first,
    /// optionally limited to one supplier and status.
    /// </summary>
    Task<IReadOnlyList<GoodsReceipt>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default);
}
