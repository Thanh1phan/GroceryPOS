using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeInventoryService : IInventoryService
{
    public List<StockItem> Stock { get; } = new();
    public List<LowStockItem> Low { get; } = new();
    public Error? FailStockWith { get; set; }
    public Error? FailLowWith { get; set; }
    public Error? FailAdjustWith { get; set; }
    public List<string> Calls { get; } = new();
    public StockAdjustmentRequest? LastAdjustment { get; private set; }

    /// <summary>Awaited inside <see cref="GetStockListAsync"/> when set (to overlap two loads).</summary>
    public Func<Task>? BeforeStockReturns { get; set; }

    public Task<Result<StockAdjustmentResult>> AdjustAsync(StockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"adjust:{request.ProductId}:{request.Quantity}:{request.Reason}");
        LastAdjustment = request;
        if (FailAdjustWith is { } e)
        {
            return Task.FromResult(Result.Failure<StockAdjustmentResult>(e));
        }

        var product = Stock.Single(s => s.ProductId == request.ProductId);
        var after = product.StockQuantity + request.Quantity;
        return Task.FromResult(Result.Success(new StockAdjustmentResult(
            product.ProductId, product.ProductCode, product.ProductName, request.Quantity, after,
            Math.Round(request.Quantity * product.CostPrice, 0), after <= product.MinStockLevel)));
    }

    public Task<Result<StockCountResult>> CountAsync(StockCountRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The stock count form saves on its own.");

    public Task<Result<StockLedger>> GetLedgerAsync(int productId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The stock card form loads its own data.");

    public async Task<Result<IReadOnlyList<StockItem>>> GetStockListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("stock");
        var snapshot = Stock.ToList();
        if (BeforeStockReturns is { } wait)
        {
            await wait();
        }

        return FailStockWith is { } e ? Result.Failure<IReadOnlyList<StockItem>>(e) : Result.Success<IReadOnlyList<StockItem>>(snapshot);
    }

    public Task<Result<IReadOnlyList<LowStockItem>>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("low");
        return Task.FromResult(FailLowWith is { } e ? Result.Failure<IReadOnlyList<LowStockItem>>(e) : Result.Success<IReadOnlyList<LowStockItem>>(Low.ToList()));
    }
}
