using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Shifts;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

internal sealed class StockMovementRepository : EfRepository<StockMovement>, IStockMovementRepository
{
    public StockMovementRepository(GroceryDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<StockMovement>> ListByProductAsync(
        int productId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = Set.Where(m => m.ProductId == productId);
        if (from is { } start)
        {
            query = query.Where(m => m.Timestamp >= start);
        }

        if (to is { } end)
        {
            query = query.Where(m => m.Timestamp < end);
        }

        return await query.OrderBy(m => m.Timestamp).ThenBy(m => m.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class GoodsReceiptRepository : EfRepository<GoodsReceipt>, IGoodsReceiptRepository
{
    public GoodsReceiptRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<GoodsReceipt?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(r => r.Code == normalized, cancellationToken);
    }

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(r => r.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<GoodsReceipt>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<GoodsReceipt> query = Set;
        if (filter.From is { } from)
        {
            query = query.Where(r => r.ReceiptDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.ReceiptDate < to);
        }

        if (filter.SupplierId is { } supplierId)
        {
            query = query.Where(r => r.SupplierId == supplierId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query.OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class SupplierPaymentRepository : EfRepository<SupplierPayment>, ISupplierPaymentRepository
{
    public SupplierPaymentRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(p => p.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<SupplierPayment>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<SupplierPayment> query = Set;
        if (filter.From is { } from)
        {
            query = query.Where(p => p.PaidAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(p => p.PaidAt < to);
        }

        if (filter.SupplierId is { } supplierId)
        {
            query = query.Where(p => p.SupplierId == supplierId);
        }

        return await query.OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierPayment>> ListActivityAsync(int supplierId, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        var query = Set.Where(p => p.SupplierId == supplierId);
        if (since is { } start)
        {
            query = query.Where(p => p.PaidAt >= start || p.CancelledAt >= start);
        }

        return await query.OrderBy(p => p.PaidAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class SupplierReturnRepository : EfRepository<SupplierReturn>, ISupplierReturnRepository
{
    public SupplierReturnRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<SupplierReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(r => r.Code == normalized, cancellationToken);
    }

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(r => r.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<SupplierReturn>> ListByGoodsReceiptAsync(int goodsReceiptId, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.GoodsReceiptId == goodsReceiptId)
            .OrderBy(r => r.ReturnDate).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SupplierReturn>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<SupplierReturn> query = Set;
        if (filter.From is { } from)
        {
            query = query.Where(r => r.ReturnDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.ReturnDate < to);
        }

        if (filter.SupplierId is { } supplierId)
        {
            query = query.Where(r => r.SupplierId == supplierId);
        }

        return await query.OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.Id).ToListAsync(cancellationToken);
    }
}

internal sealed class ShiftRepository : EfRepository<Shift>, IShiftRepository
{
    public ShiftRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<Shift?> GetOpenByUserAsync(int userId, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open, cancellationToken);

    public Task<Shift?> GetLastClosedAsync(CancellationToken cancellationToken = default) =>
        Set.Where(s => s.Status != ShiftStatus.Open)
            .OrderByDescending(s => s.ClosedAt).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(s => s.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<Shift>> SearchAsync(ShiftFilter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<Shift> query = Set;
        if (filter.From is { } from)
        {
            query = query.Where(s => s.OpenedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(s => s.OpenedAt < to);
        }

        if (filter.UserId is { } userId)
        {
            query = query.Where(s => s.UserId == userId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        return await query.OrderByDescending(s => s.OpenedAt).ThenByDescending(s => s.Id).ToListAsync(cancellationToken);
    }
}
