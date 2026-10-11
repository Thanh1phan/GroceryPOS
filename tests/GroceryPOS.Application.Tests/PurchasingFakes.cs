using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Tests.Fakes;

internal sealed class InMemorySupplierRepository : InMemoryRepository<Supplier>, ISupplierRepository
{
    public Task<Supplier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Code == code));
}

internal sealed class InMemoryGoodsReceiptRepository : InMemoryRepository<GoodsReceipt>, IGoodsReceiptRepository
{
    public Task<GoodsReceipt?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.Code == code));

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(r => r.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<GoodsReceipt>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoodsReceipt>>(Items
            .Where(r => (filter.From is null || r.ReceiptDate >= filter.From)
                && (filter.To is null || r.ReceiptDate < filter.To)
                && (filter.SupplierId is null || r.SupplierId == filter.SupplierId)
                && (filter.Status is null || r.Status == filter.Status))
            .OrderByDescending(r => r.ReceiptDate)
            .ToList());
}
