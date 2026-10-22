using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

internal sealed class SaleRepository : EfRepository<Sale>, ISaleRepository
{
    public SaleRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<Sale?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(s => s.Code == normalized, cancellationToken);
    }

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(s => s.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListCompletedByCashierAsync(
        int cashierId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await Set.Where(s => s.CashierId == cashierId && s.CompletedAt >= from && s.CompletedAt <= to)
            .OrderBy(s => s.CompletedAt).ThenBy(s => s.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListVoidedByAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await Set.Where(s => s.VoidedBy == userId && s.VoidedAt >= from && s.VoidedAt <= to)
            .OrderBy(s => s.VoidedAt).ThenBy(s => s.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListCompletedInPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await Set.Where(s => s.CompletedAt >= from && s.CompletedAt < to)
            .OrderBy(s => s.CompletedAt).ThenBy(s => s.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default) =>
        await Set.Where(s => s.CustomerId == customerId && s.CompletedAt != null)
            .OrderBy(s => s.CompletedAt).ThenBy(s => s.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
}

internal sealed class SaleReturnRepository : EfRepository<SaleReturn>, ISaleReturnRepository
{
    public SaleReturnRepository(GroceryDbContext context) : base(context)
    {
    }

    public Task<SaleReturn?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Set.FirstOrDefaultAsync(r => r.Code == normalized, cancellationToken);
    }

    public Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        LastCodeAsync(Set.Select(r => r.Code), prefix, cancellationToken);

    public async Task<IReadOnlyList<SaleReturn>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.SaleId == saleId)
            .OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleReturn>> ListByUserAsync(
        int userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.UserId == userId && r.ReturnedAt >= from && r.ReturnedAt <= to)
            .OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleReturn>> ListInPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.ReturnedAt >= from && r.ReturnedAt < to)
            .OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SaleReturn>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default) =>
        await Set.Where(r => r.CustomerId == customerId)
            .OrderBy(r => r.ReturnedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
}
