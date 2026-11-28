using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakePosLookupService : IPosLookupService
{
    /// <summary>Products found by barcode or code (case-insensitive).</summary>
    public Dictionary<string, PosProductDto> ByCode { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Products a name search can find.</summary>
    public List<PosProductDto> Catalog { get; } = new();

    public Dictionary<string, Customer> CustomersByPhone { get; } = new();
    public List<string> Calls { get; } = new();

    /// <summary>Awaited inside <see cref="FindForScanAsync"/> for the given input (to overlap scans).</summary>
    public Dictionary<string, Func<Task>> BeforeScanReturns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<PosProductDto>> FindForScanAsync(string input, CancellationToken cancellationToken = default)
    {
        Calls.Add($"scan:{input}");
        if (BeforeScanReturns.TryGetValue(input, out var wait))
        {
            await wait();
        }

        return ByCode.TryGetValue(input, out var product)
            ? Result.Success(product)
            : Result.Failure<PosProductDto>(Error.NotFound($"Không tìm thấy sản phẩm có mã \"{input}\"."));
    }

    public Task<IReadOnlyList<PosProductDto>> SearchAsync(string term, int take = 20, CancellationToken cancellationToken = default)
    {
        Calls.Add($"search:{term}");
        IReadOnlyList<PosProductDto> hits = Catalog
            .Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(take)
            .ToList();
        return Task.FromResult(hits);
    }

    public Task<Result<Customer>> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        Calls.Add($"phone:{phone}");
        return Task.FromResult(CustomersByPhone.TryGetValue(phone, out var customer)
            ? Result.Success(customer)
            : Result.Failure<Customer>(Error.NotFound("Không tìm thấy khách hàng với số điện thoại này.")));
    }
}

internal sealed class FakeCheckoutService : ICheckoutService
{
    public List<CheckoutRequest> Requests { get; } = new();
    public Error? FailWith { get; set; }
    public int PointsEarned { get; set; }
    public List<LowStockAlert> LowStock { get; } = new();

    public Task<Result<CheckoutReceipt>> CheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (FailWith is { } e)
        {
            return Task.FromResult(Result.Failure<CheckoutReceipt>(e));
        }

        var paid = request.Payments.Sum(p => p.Amount);
        var code = $"HD261128-{Requests.Count:0000}";
        return Task.FromResult(Result.Success(new CheckoutReceipt(
            Requests.Count, code, DateTimeOffset.UnixEpoch, Total, paid, Math.Max(0, paid - Total), 0, 0,
            PointsEarned, request.PointsToRedeem, null, LowStock.ToList())));
    }

    /// <summary>The total the fake reports (tests set it to the cart total).</summary>
    public decimal Total { get; set; }

    public Task<Result> VoidAsync(int saleId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class FakeReceiptService : IReceiptService
{
    public List<string> Calls { get; } = new();
    public Error? FailWith { get; set; }

    public Task<Result<ReceiptData>> GetForSaleAsync(int saleId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"sale:{saleId}");
        return Task.FromResult(FailWith is { } e ? Result.Failure<ReceiptData>(e) : Result.Success(Receipt($"#{saleId}", false)));
    }

    public Task<Result<ReceiptData>> ReprintAsync(string invoiceCode, CancellationToken cancellationToken = default)
    {
        Calls.Add($"reprint:{invoiceCode}");
        return Task.FromResult(FailWith is { } e ? Result.Failure<ReceiptData>(e) : Result.Success(Receipt(invoiceCode, true)));
    }

    private static ReceiptData Receipt(string code, bool reprint) => new(
        StoreProfile.Default, code, DateTimeOffset.UnixEpoch, "Thu ngân An", SaleStatus.Completed, [], 0, 0, null, 0, 0, 0,
        [], 0, 0, [], null, null, reprint);
}

internal sealed class FakeParkedOrderService : IParkedOrderService
{
    private readonly List<(ParkedOrder Order, List<(PosProductDto Product, decimal Quantity)> Lines)> _orders = new();

    public List<string> Calls { get; } = new();
    public Error? FailParkWith { get; set; }
    public List<string> ResumeNotices { get; } = new();

    public int Count => _orders.Count;

    public Task<Result<ParkedOrder>> ParkAsync(PosCart cart, string? label = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"park:{label ?? "<auto>"}");
        if (FailParkWith is { } e)
        {
            return Task.FromResult(Result.Failure<ParkedOrder>(e));
        }

        var order = new ParkedOrder(
            Guid.NewGuid(), label ?? $"Đơn {_orders.Count + 1}", DateTimeOffset.UnixEpoch, 1, "Thu ngân An", [],
            cart.Customer?.Id, cart.Customer?.Name, cart.Customer?.Phone, cart.InvoiceDiscount, cart.Note, cart.Total);
        _orders.Add((order, cart.Lines.Select(l => (l.Product, l.Quantity)).ToList()));
        cart.Clear();
        return Task.FromResult(Result.Success(order));
    }

    public Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        IReadOnlyList<ParkedOrder> list = _orders.Select(o => o.Order).ToList();
        return Task.FromResult(list);
    }

    public Guid IdAt(int index) => _orders[index].Order.Id;

    public Task<Result<ParkedOrderResume>> ResumeAsync(Guid id, PosCart cart, CancellationToken cancellationToken = default)
    {
        Calls.Add("resume");
        var index = _orders.FindIndex(o => o.Order.Id == id);
        if (index < 0)
        {
            return Task.FromResult(Result.Failure<ParkedOrderResume>(Error.NotFound("Đơn tạm không còn.")));
        }

        var (order, lines) = _orders[index];
        _orders.RemoveAt(index);
        cart.Clear();
        foreach (var (product, quantity) in lines)
        {
            cart.Add(product, quantity);
        }

        return Task.FromResult(Result.Success(new ParkedOrderResume(order, lines.Count, ResumeNotices.ToList())));
    }

    public Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
