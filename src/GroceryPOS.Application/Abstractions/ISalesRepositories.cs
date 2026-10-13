using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Abstractions;

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Finds a product by its (normalized) barcode.</summary>
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>Finds a product by its internal code (case-insensitive, stored upper-case).</summary>
    Task<Product?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Active products whose name, code or barcode contains <paramref name="term"/>.</summary>
    Task<IReadOnlyList<Product>> SearchAsync(string term, int take, CancellationToken cancellationToken = default);

    /// <summary>Active products whose stock is at or below their minimum level.</summary>
    Task<IReadOnlyList<Product>> ListLowStockAsync(CancellationToken cancellationToken = default);
}

public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken = default);
}

public interface ISaleRepository : IRepository<Sale>
{
    /// <summary>
    /// Loads a sale including its lines and payments.
    /// (<see cref="IRepository{T}.GetByIdAsync"/> must also include them for sales.)
    /// </summary>
    Task<Sale?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Highest invoice code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sales (with lines and payments) completed by <paramref name="cashierId"/> with
    /// <see cref="Sale.CompletedAt"/> in [<paramref name="from"/>, <paramref name="to"/>] (both inclusive),
    /// whatever their current status (a sale voided later is still listed).
    /// </summary>
    Task<IReadOnlyList<Sale>> ListCompletedByCashierAsync(
        int cashierId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Sales (with payments) voided by <paramref name="userId"/> with <see cref="Sale.VoidedAt"/> in [from, to].</summary>
    Task<IReadOnlyList<Sale>> ListVoidedByAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public interface ISaleReturnRepository : IRepository<SaleReturn>
{
    /// <summary>Loads a return including its lines. (<see cref="IRepository{T}.GetByIdAsync"/> must also include them.)</summary>
    Task<SaleReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Highest return code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Returns (with lines) made against one invoice, oldest first.</summary>
    Task<IReadOnlyList<SaleReturn>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default);

    /// <summary>Returns (with lines) taken by <paramref name="userId"/> with <see cref="SaleReturn.ReturnedAt"/> in [from, to].</summary>
    Task<IReadOnlyList<SaleReturn>> ListByUserAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
