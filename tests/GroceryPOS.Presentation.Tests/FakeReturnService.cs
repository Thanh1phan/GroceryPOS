using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeReturnService : IReturnService
{
    public Dictionary<string, ReturnableSaleDto> Sales { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, List<SaleReturnSummaryDto>> History { get; } = new();
    public List<CreateReturnRequest> Created { get; } = new();
    public List<string> Calls { get; } = new();
    public Error? FailCreateWith { get; set; }
    public Error? FailHistoryWith { get; set; }

    /// <summary>Awaited inside <see cref="GetReturnableAsync"/> for the given code (to overlap two scans).</summary>
    public Dictionary<string, Func<Task>> BeforeLoadReturns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs inside <see cref="CreateAsync"/> before it answers (e.g. to change the invoice meanwhile).</summary>
    public Action? DuringCreate { get; set; }

    public async Task<Result<ReturnableSaleDto>> GetReturnableAsync(string saleCode, CancellationToken cancellationToken = default)
    {
        Calls.Add($"load:{saleCode}");
        if (BeforeLoadReturns.TryGetValue(saleCode, out var wait))
        {
            await wait();
        }

        return Sales.TryGetValue(saleCode, out var sale)
            ? sale
            : Result.Failure<ReturnableSaleDto>(Error.NotFound($"Không tìm thấy hóa đơn {saleCode}."));
    }

    public Task<Result<ReturnReceipt>> CreateAsync(CreateReturnRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add("create");
        Created.Add(request);
        DuringCreate?.Invoke();
        if (FailCreateWith is { } e)
        {
            return Task.FromResult(Result.Failure<ReturnReceipt>(e));
        }

        var sale = Sales.Values.Single(s => s.SaleId == request.SaleId);
        var refund = request.Lines.Sum(l => l.Quantity * sale.Lines.Single(x => x.SaleLineId == l.SaleLineId).UnitRefund);
        return Task.FromResult(Result.Success(new ReturnReceipt(
            ReturnId: 50,
            Code: "TH261127-0001",
            SaleCode: sale.Code,
            ReturnedAt: new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero),
            RefundTotal: refund,
            RefundMethod: request.RefundMethod,
            PointsReversed: 0,
            PointsRestored: 0,
            CustomerPointsBalance: null,
            SaleFullyReturned: false,
            WrittenOffCost: 0,
            Lines: [],
            Reason: request.Reason,
            CustomerName: sale.CustomerName)));
    }

    public Task<Result<IReadOnlyList<SaleReturnSummaryDto>>> ListBySaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"history:{saleId}");
        if (FailHistoryWith is { } e)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<SaleReturnSummaryDto>>(e));
        }

        return Task.FromResult(Result.Success<IReadOnlyList<SaleReturnSummaryDto>>(
            History.TryGetValue(saleId, out var list) ? list.ToList() : []));
    }
}
