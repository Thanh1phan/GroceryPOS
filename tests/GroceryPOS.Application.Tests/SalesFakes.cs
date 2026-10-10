using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests.Fakes;

/// <summary>List-backed repository that assigns identities on Add, like the database would.</summary>
internal class InMemoryRepository<T> : IRepository<T> where T : Entity
{
    private int _nextId = 1;

    protected List<T> Items { get; } = new();

    public IReadOnlyList<T> All => Items;

    public Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(Items.ToList());

    public void Add(T entity)
    {
        if (entity.IsTransient)
        {
            entity.WithId(Math.Max(_nextId, Items.Count == 0 ? 1 : Items.Max(i => i.Id) + 1));
        }

        _nextId = entity.Id + 1;
        Items.Add(entity);
    }

    public void Remove(T entity) => Items.Remove(entity);
}

internal sealed class InMemoryProductRepository : InMemoryRepository<Product>, IProductRepository
{
    public Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Product>>(Items.Where(p => ids.Contains(p.Id)).ToList());

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Barcode == barcode));

    public Task<Product?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Code == code));

    public Task<IReadOnlyList<Product>> SearchAsync(string term, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Product>>(Items
            .Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || p.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (p.Barcode?.Contains(term) ?? false))
            .Take(take)
            .ToList());

    public Task<IReadOnlyList<Product>> ListLowStockAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Product>>(Items.Where(p => p.IsActive && p.IsLowStock).ToList());
}

internal sealed class InMemoryStockMovementRepository : InMemoryRepository<StockMovement>, IStockMovementRepository
{
    public Task<IReadOnlyList<StockMovement>> ListByProductAsync(
        int productId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StockMovement>>(Items
            .Where(m => m.ProductId == productId
                && (from is null || m.Timestamp >= from)
                && (to is null || m.Timestamp < to))
            .OrderBy(m => m.Timestamp)
            .ThenBy(m => m.Id)
            .ToList());
}

internal sealed class InMemoryCustomerRepository : InMemoryRepository<Customer>, ICustomerRepository
{
    public Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(c => c.Phone == normalizedPhone));
}

internal sealed class InMemorySaleRepository : InMemoryRepository<Sale>, ISaleRepository
{
    public Task<Sale?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Code == code));

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(s => s.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());
}

internal static class EntityTestExtensions
{
    /// <summary>Simulates a persisted entity by setting its identity (normally assigned by the database).</summary>
    public static T WithId<T>(this T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }
}
