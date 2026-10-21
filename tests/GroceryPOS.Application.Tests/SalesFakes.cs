using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

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

    public virtual void Add(T entity)
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
    private int _nextLineId = 1;

    /// <summary>Also numbers the lines, like the database would (returns reference lines by id).</summary>
    public override void Add(Sale entity)
    {
        base.Add(entity);
        foreach (var line in entity.Lines.Where(l => l.IsTransient))
        {
            line.WithId(_nextLineId++);
        }
    }

    public Task<Sale?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Code == code));

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(s => s.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<Sale>> ListCompletedByCashierAsync(
        int cashierId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Sale>>(Items
            .Where(s => s.CashierId == cashierId && s.CompletedAt >= from && s.CompletedAt <= to)
            .ToList());

    public Task<IReadOnlyList<Sale>> ListVoidedByAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Sale>>(Items
            .Where(s => s.VoidedBy == userId && s.VoidedAt >= from && s.VoidedAt <= to)
            .ToList());

    public Task<IReadOnlyList<Sale>> ListCompletedInPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Sale>>(Items.Where(s => s.CompletedAt >= from && s.CompletedAt < to).ToList());
}

internal sealed class InMemorySaleReturnRepository : InMemoryRepository<SaleReturn>, ISaleReturnRepository
{
    public Task<SaleReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.Code == code));

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(r => r.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<SaleReturn>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SaleReturn>>(Items.Where(r => r.SaleId == saleId).OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id).ToList());

    public Task<IReadOnlyList<SaleReturn>> ListByUserAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SaleReturn>>(Items
            .Where(r => r.UserId == userId && r.ReturnedAt >= from && r.ReturnedAt <= to)
            .ToList());

    public Task<IReadOnlyList<SaleReturn>> ListInPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SaleReturn>>(Items.Where(r => r.ReturnedAt >= from && r.ReturnedAt < to).ToList());
}

internal sealed class InMemoryCategoryRepository : InMemoryRepository<Category>, ICategoryRepository
{
}

internal sealed class InMemoryPriceHistoryRepository : InMemoryRepository<PriceHistory>, IPriceHistoryRepository
{
    public Task<IReadOnlyList<PriceHistory>> ListByProductAsync(
        int productId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PriceHistory>>(Items
            .Where(h => h.ProductId == productId
                && (from is null || h.ChangedAt >= from)
                && (to is null || h.ChangedAt < to))
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .ToList());
}

internal sealed class InMemoryShiftRepository : InMemoryRepository<Shift>, IShiftRepository
{
    public Task<Shift?> GetOpenByUserAsync(int userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.UserId == userId && s.IsOpen));

    public Task<Shift?> GetLastClosedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Where(s => !s.IsOpen).OrderByDescending(s => s.ClosedAt).ThenByDescending(s => s.Id).FirstOrDefault());

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(s => s.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<Shift>> SearchAsync(ShiftFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Shift>>(Items
            .Where(s => (filter.From is null || s.OpenedAt >= filter.From)
                && (filter.To is null || s.OpenedAt < filter.To)
                && (filter.UserId is null || s.UserId == filter.UserId)
                && (filter.Status is null || s.Status == filter.Status))
            .OrderByDescending(s => s.OpenedAt)
            .ThenByDescending(s => s.Id)
            .ToList());
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
