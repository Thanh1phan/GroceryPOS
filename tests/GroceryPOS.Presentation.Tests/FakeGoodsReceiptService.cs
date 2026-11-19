using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeGoodsReceiptService : IGoodsReceiptService
{
    public List<GoodsReceiptSummary> Receipts { get; } = new();
    public Dictionary<int, GoodsReceiptDto> Details { get; } = new();
    public List<ReorderSuggestion> Suggestions { get; } = new();
    public List<GoodsReceiptFilter> Searches { get; } = new();
    public List<string> Calls { get; } = new();
    public Error? FailSearchWith { get; set; }
    public Error? FailDraftsWith { get; set; }
    public Error? FailCancelWith { get; set; }
    public Error? FailSuggestWith { get; set; }

    /// <summary>Awaited inside a period search when set (to overlap two loads).</summary>
    public Func<Task>? BeforeSearchReturns { get; set; }

    public Task<Result<GoodsReceiptDto>> SaveDraftAsync(SaveGoodsReceiptRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The editor saves on its own.");

    public Task<Result<GoodsReceiptCompletion>> CompleteAsync(int receiptId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The editor completes on its own.");

    public Task<Result> CancelAsync(int receiptId, string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add($"cancel:{receiptId}:{reason}");
        return Task.FromResult(FailCancelWith is { } e ? Result.Failure(e) : Result.Success());
    }

    public Task<Result<GoodsReceiptDto>> GetAsync(int receiptId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"get:{receiptId}");
        return Task.FromResult(Details.TryGetValue(receiptId, out var dto)
            ? Result.Success(dto)
            : Result.Failure<GoodsReceiptDto>(new Error("receipt.not_found", "Không tìm thấy phiếu nhập.")));
    }

    public async Task<Result<IReadOnlyList<GoodsReceiptSummary>>> SearchAsync(GoodsReceiptFilter filter, CancellationToken cancellationToken = default)
    {
        Searches.Add(filter);
        var isDraftCount = filter.From is null && filter.To is null && filter.Status == GoodsReceiptStatus.Draft;
        var rows = Receipts
            .Where(r => filter.Status is null || r.Status == filter.Status)
            .Where(r => filter.SupplierId is null || r.SupplierId == filter.SupplierId)
            .Where(r => filter.From is null || r.ReceiptDate >= filter.From)
            .Where(r => filter.To is null || r.ReceiptDate < filter.To)
            .ToList();
        if (!isDraftCount && BeforeSearchReturns is { } wait)
        {
            await wait();
        }

        var fail = isDraftCount ? FailDraftsWith : FailSearchWith;
        return fail is { } e ? Result.Failure<IReadOnlyList<GoodsReceiptSummary>>(e) : Result.Success<IReadOnlyList<GoodsReceiptSummary>>(rows);
    }

    public Task<Result<IReadOnlyList<ReorderSuggestion>>> SuggestReorderAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("suggest");
        return Task.FromResult(FailSuggestWith is { } e
            ? Result.Failure<IReadOnlyList<ReorderSuggestion>>(e)
            : Result.Success<IReadOnlyList<ReorderSuggestion>>(Suggestions.ToList()));
    }
}
