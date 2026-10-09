using System.Globalization;
using GroceryPOS.Application.Abstractions;

namespace GroceryPOS.Application.Sales;

public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// Daily sequential invoice codes: <c>{prefix}{yyMMdd}-{seq:0000}</c>, e.g. <c>HD261009-0001</c>.
/// The date is the store's local date. Uniqueness is finally enforced by a unique index on Sale.Code.
/// </summary>
public sealed class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    private readonly ISaleRepository _sales;
    private readonly SalesOptions _options;

    public InvoiceNumberGenerator(ISaleRepository sales, SalesOptions? options = null)
    {
        _sales = sales;
        _options = options ?? SalesOptions.Default;
    }

    public static string PrefixFor(string invoicePrefix, DateTimeOffset localNow) =>
        $"{invoicePrefix.Trim().ToUpperInvariant()}{localNow.ToString("yyMMdd", CultureInfo.InvariantCulture)}-";

    public async Task<string> NextAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var prefix = PrefixFor(_options.InvoicePrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _sales.GetLastCodeWithPrefixAsync(prefix, cancellationToken);

        var next = 1;
        if (last is not null
            && last.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(last.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var seq))
        {
            next = seq + 1;
        }

        return prefix + next.ToString("0000", CultureInfo.InvariantCulture);
    }
}
