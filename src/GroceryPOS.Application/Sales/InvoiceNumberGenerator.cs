using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Sales;

public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Daily sequential invoice codes, e.g. <c>HD261009-0001</c> (see <see cref="DailyCode"/>).</summary>
public sealed class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    private readonly ISaleRepository _sales;
    private readonly SalesOptions _options;

    public InvoiceNumberGenerator(ISaleRepository sales, SalesOptions? options = null)
    {
        _sales = sales;
        _options = options ?? SalesOptions.Default;
    }

    public static string PrefixFor(string invoicePrefix, DateTimeOffset localNow) => DailyCode.PrefixFor(invoicePrefix, localNow);

    public async Task<string> NextAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var prefix = PrefixFor(_options.InvoicePrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _sales.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
        return DailyCode.Next(prefix, last);
    }
}
