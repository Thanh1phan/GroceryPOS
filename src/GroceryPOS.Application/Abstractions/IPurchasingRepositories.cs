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

/// <summary>Period/supplier filter for supplier payments and supplier returns; [From, To).</summary>
public sealed record SupplierDocumentFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? SupplierId = null);

public interface ISupplierPaymentRepository : IRepository<SupplierPayment>
{
    /// <summary>Highest payment code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Payments (cancelled ones included) whose <see cref="SupplierPayment.PaidAt"/> lies in [From, To).</summary>
    Task<IReadOnlyList<SupplierPayment>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Payments of one supplier that were made or cancelled at or after <paramref name="since"/> (all when null),
    /// for the supplier debt ledger.
    /// </summary>
    Task<IReadOnlyList<SupplierPayment>> ListActivityAsync(int supplierId, DateTimeOffset? since, CancellationToken cancellationToken = default);
}

public interface ISupplierReturnRepository : IRepository<SupplierReturn>
{
    /// <summary>Loads a return including its lines. (<see cref="IRepository{T}.GetByIdAsync"/> must also include them.)</summary>
    Task<SupplierReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Highest return code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Returns (with lines) made against one goods receipt, oldest first.</summary>
    Task<IReadOnlyList<SupplierReturn>> ListByGoodsReceiptAsync(int goodsReceiptId, CancellationToken cancellationToken = default);

    /// <summary>Returns (with lines) whose <see cref="SupplierReturn.ReturnDate"/> lies in [From, To), newest first.</summary>
    Task<IReadOnlyList<SupplierReturn>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default);
}
