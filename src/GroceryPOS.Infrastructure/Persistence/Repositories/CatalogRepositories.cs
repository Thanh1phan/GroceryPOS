using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Partners;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository : EfRepository<Category>, ICategoryRepository
{
    public CategoryRepository(GroceryDbContext context) : base(context)
    {
    }
}

internal sealed class ProductRepository : EfRepository<Product>, IProductRepository
{
    public ProductRepository(GroceryDbContext context) : base(context)
    {
    }

    protected override IQueryable<Product> Query => Set.Include(p => p.Category);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<Product>();
        }

        var distinct = ids.Distinct().ToList();
        return await Query.Where(p => distinct.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
        Query.FirstOrDefaultAsync(p => p.Barcode == barcode, cancellationToken);

    public Task<Product?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Query.FirstOrDefaultAsync(p => p.Code == normalized, cancellationToken);
    }

    /// <summary>
    /// Accent/case-insensitive search ("sua" finds "Sữa"). Neither SQLite nor a default SQL Server collation folds
    /// Vietnamese diacritics, so the (small) name/code/barcode columns of active products are matched in memory and
    /// only the hits are loaded as entities. Exact code/barcode first, then names starting with the term.
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(string term, int take, CancellationToken cancellationToken = default)
    {
        var folded = SearchText.Fold(term);
        if (folded.Length == 0 || take <= 0)
        {
            return Array.Empty<Product>();
        }

        var candidates = await Set.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Code, p.Barcode })
            .ToListAsync(cancellationToken);

        var hitIds = candidates
            .Where(c => SearchText.Matches(term, c.Name, c.Code, c.Barcode))
            .Select(c => new
            {
                c.Id,
                Rank = SearchText.Fold(c.Code) == folded || c.Barcode == term.Trim() ? 0
                    : SearchText.Fold(c.Name).StartsWith(folded, StringComparison.Ordinal) ? 1
                    : 2,
                c.Name,
            })
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), true))
            .Take(take)
            .Select(c => c.Id)
            .ToList();

        var products = await GetByIdsAsync(hitIds, cancellationToken);
        var byId = products.ToDictionary(p => p.Id);
        return hitIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<Product>> ListLowStockAsync(CancellationToken cancellationToken = default) =>
        await Query.Where(p => p.IsActive && p.StockQuantity <= p.MinStockLevel)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
}

internal sealed class PriceHistoryRepository : EfRepository<PriceHistory>, IPriceHistoryRepository
{
    public PriceHistoryRepository(GroceryDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<PriceHistory>> ListByProductAsync(
        int productId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = Set.Where(h => h.ProductId == productId);
        if (from is { } start)
        {
            query = query.Where(h => h.ChangedAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(h => h.ChangedAt < end);
        }

        return await query.OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class CustomerRepository : EfRepository<Customer>, ICustomerRepository
{
    public CustomerRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<Customer?> GetByPhoneAsync(string normalizedPhone, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(c => c.Phone == normalizedPhone, cancellationToken);

    public async Task<IReadOnlyList<string>> ListCodesWithPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var normalized = prefix.Trim().ToUpperInvariant();
        return await Set.Where(c => c.Code.StartsWith(normalized)).Select(c => c.Code).ToListAsync(cancellationToken);
    }
}

internal sealed class SupplierRepository : EfRepository<Supplier>, ISupplierRepository
{
    public SupplierRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<Supplier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(s => s.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCodesWithPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var normalized = prefix.Trim().ToUpperInvariant();
        return await Set.Where(s => s.Code.StartsWith(normalized)).Select(s => s.Code).ToListAsync(cancellationToken);
    }
}
