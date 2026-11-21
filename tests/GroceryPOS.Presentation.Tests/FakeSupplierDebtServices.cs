using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Purchasing;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeSupplierPaymentService : ISupplierPaymentService
{
    public List<SupplierPayableDto> Payables { get; } = new();
    public List<SupplierPaymentDto> Payments { get; } = new();
    public List<SupplierDocumentFilter> Searches { get; } = new();
    public List<string> Calls { get; } = new();
    public Error? FailPayablesWith { get; set; }
    public Error? FailSearchWith { get; set; }
    public Error? FailCancelWith { get; set; }

    /// <summary>Awaited inside a payment search when set (to overlap two loads).</summary>
    public Func<Task>? BeforeSearchReturns { get; set; }

    /// <summary>Runs after <see cref="ListPayablesAsync"/> returned (simulates another machine changing the debt).</summary>
    public Action? AfterPayablesListed { get; set; }

    public Task<Result<SupplierPaymentDto>> PayAsync(SupplierPaymentRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The payment dialog pays on its own.");

    public Task<Result<SupplierPaymentDto>> CancelAsync(int paymentId, string? reason, CancellationToken cancellationToken = default)
    {
        Calls.Add($"cancel:{paymentId}:{reason}");
        if (FailCancelWith is { } e)
        {
            return Task.FromResult(Result.Failure<SupplierPaymentDto>(e));
        }

        var payment = Payments.Single(p => p.Id == paymentId) with { IsCancelled = true, CancelReason = reason };
        return Task.FromResult(Result.Success(payment));
    }

    public Task<Result<IReadOnlyList<SupplierPayableDto>>> ListPayablesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("payables");
        var result = FailPayablesWith is { } e
            ? Result.Failure<IReadOnlyList<SupplierPayableDto>>(e)
            : Result.Success<IReadOnlyList<SupplierPayableDto>>(Payables.ToList());
        AfterPayablesListed?.Invoke();
        return Task.FromResult(result);
    }

    public async Task<Result<IReadOnlyList<SupplierPaymentDto>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        Searches.Add(filter);
        var rows = Payments
            .Where(p => filter.From is null || p.PaidAt >= filter.From)
            .Where(p => filter.To is null || p.PaidAt < filter.To)
            .ToList();
        if (BeforeSearchReturns is { } wait)
        {
            await wait();
        }

        return FailSearchWith is { } e ? Result.Failure<IReadOnlyList<SupplierPaymentDto>>(e) : Result.Success<IReadOnlyList<SupplierPaymentDto>>(rows);
    }

    public Task<Result<SupplierDebtLedgerDto>> GetLedgerAsync(int supplierId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The ledger window loads its own data.");
}

internal sealed class FakeSupplierReturnService : ISupplierReturnService
{
    public List<SupplierReturnSummary> Returns { get; } = new();
    public Dictionary<int, SupplierReturnDto> Details { get; } = new();
    public List<SupplierDocumentFilter> Searches { get; } = new();
    public Error? FailSearchWith { get; set; }

    public Task<Result<ReturnableReceiptDto>> GetReturnableAsync(string receiptCode, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The return editor loads receipts on its own.");

    public Task<Result<SupplierReturnReceipt>> CreateAsync(CreateSupplierReturnRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The return editor posts on its own.");

    public Task<Result<SupplierReturnDto>> GetAsync(int returnId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Details.TryGetValue(returnId, out var dto)
            ? Result.Success(dto)
            : Result.Failure<SupplierReturnDto>(new Error("supplier_return.not_found", "Không tìm thấy phiếu trả hàng.")));

    public Task<Result<IReadOnlyList<SupplierReturnSummary>>> SearchAsync(SupplierDocumentFilter filter, CancellationToken cancellationToken = default)
    {
        Searches.Add(filter);
        var rows = Returns
            .Where(r => filter.From is null || r.ReturnDate >= filter.From)
            .Where(r => filter.To is null || r.ReturnDate < filter.To)
            .ToList();
        return Task.FromResult(FailSearchWith is { } e
            ? Result.Failure<IReadOnlyList<SupplierReturnSummary>>(e)
            : Result.Success<IReadOnlyList<SupplierReturnSummary>>(rows));
    }
}
