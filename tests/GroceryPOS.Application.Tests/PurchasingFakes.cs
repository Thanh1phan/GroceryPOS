using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Tests.Fakes;

internal sealed class InMemorySupplierRepository : InMemoryRepository<Supplier>, ISupplierRepository
{
    public Task<Supplier?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Code == code));

    public Task<IReadOnlyList<string>> ListCodesWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Items.Select(s => s.Code).Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).ToList());
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

internal sealed class InMemorySupplierPaymentRepository : InMemoryRepository<SupplierPayment>, ISupplierPaymentRepository
{
    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(p => p.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<SupplierPayment>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupplierPayment>>(Items
            .Where(p => (filter.From is null || p.PaidAt >= filter.From)
                && (filter.To is null || p.PaidAt < filter.To)
                && (filter.SupplierId is null || p.SupplierId == filter.SupplierId))
            .OrderByDescending(p => p.PaidAt)
            .ToList());

    public Task<IReadOnlyList<SupplierPayment>> ListActivityAsync(int supplierId, DateTimeOffset? since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupplierPayment>>(Items
            .Where(p => p.SupplierId == supplierId
                && (since is null || p.PaidAt >= since || p.CancelledAt >= since))
            .ToList());
}

internal sealed class InMemorySupplierReturnRepository : InMemoryRepository<SupplierReturn>, ISupplierReturnRepository
{
    public Task<SupplierReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.Code == code));

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Select(r => r.Code)
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(c => c, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<IReadOnlyList<SupplierReturn>> ListByGoodsReceiptAsync(int goodsReceiptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupplierReturn>>(Items
            .Where(r => r.GoodsReceiptId == goodsReceiptId)
            .OrderBy(r => r.ReturnDate)
            .ToList());

    public Task<IReadOnlyList<SupplierReturn>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SupplierReturn>>(Items
            .Where(r => (filter.From is null || r.ReturnDate >= filter.From)
                && (filter.To is null || r.ReturnDate < filter.To)
                && (filter.SupplierId is null || r.SupplierId == filter.SupplierId))
            .OrderByDescending(r => r.ReturnDate)
            .ToList());
}
