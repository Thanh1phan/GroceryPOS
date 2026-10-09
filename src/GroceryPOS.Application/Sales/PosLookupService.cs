using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Sales;

public interface IPosLookupService
{
    /// <summary>
    /// Resolves what the cashier scanned or typed in the POS input box: a barcode first, then a product code.
    /// Only active products are returned.
    /// </summary>
    Task<Result<PosProductDto>> FindForScanAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>Quick search by name/code/barcode for the POS search popup.</summary>
    Task<IReadOnlyList<PosProductDto>> SearchAsync(string term, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>Finds a member by phone number (any common VN format).</summary>
    Task<Result<Customer>> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken = default);
}

public sealed class PosLookupService : IPosLookupService
{
    private readonly IProductRepository _products;
    private readonly ICustomerRepository _customers;

    public PosLookupService(IProductRepository products, ICustomerRepository customers)
    {
        _products = products;
        _customers = customers;
    }

    public async Task<Result<PosProductDto>> FindForScanAsync(string input, CancellationToken cancellationToken = default)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Error.Validation("Vui lòng quét mã vạch hoặc nhập mã sản phẩm.");
        }

        Product? product = null;
        if (Barcode.Normalize(text) is { } barcode)
        {
            product = await _products.GetByBarcodeAsync(barcode, cancellationToken);
        }

        product ??= await _products.GetByCodeAsync(text.ToUpperInvariant(), cancellationToken);

        if (product is null)
        {
            return SaleErrors.ProductNotFound(text);
        }

        if (!product.IsActive)
        {
            return new Error("sale.product", $"Sản phẩm \"{product.Name}\" đã ngừng kinh doanh.");
        }

        return ToDto(product);
    }

    public async Task<IReadOnlyList<PosProductDto>> SearchAsync(string term, int take = 20, CancellationToken cancellationToken = default)
    {
        var text = term?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Array.Empty<PosProductDto>();
        }

        var found = await _products.SearchAsync(text, Math.Clamp(take, 1, 100), cancellationToken);
        return found.Where(p => p.IsActive).Select(ToDto).ToList();
    }

    public async Task<Result<Customer>> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        string? normalized;
        try
        {
            normalized = PhoneNumber.NormalizeOptional(phone);
        }
        catch (Domain.Common.DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        if (normalized is null)
        {
            return Error.Validation("Vui lòng nhập số điện thoại khách hàng.");
        }

        var customer = await _customers.GetByPhoneAsync(normalized, cancellationToken);
        if (customer is null || !customer.IsActive)
        {
            return SaleErrors.CustomerNotFound;
        }

        return customer;
    }

    internal static PosProductDto ToDto(Product p) =>
        new(p.Id, p.Code, p.Barcode, p.Name, p.Unit, p.SellPrice, p.VatRate, p.StockQuantity, p.IsLowStock);
}
