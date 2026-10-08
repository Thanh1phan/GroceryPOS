using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Tests;

public class BarcodeTests
{
    [Theory]
    [InlineData("8934563138165", true)]   // EAN-13 (Vietnam prefix 893)
    [InlineData("8934563138166", false)]  // wrong check digit
    [InlineData("96385074", true)]        // EAN-8
    [InlineData("036000291452", true)]    // UPC-A
    [InlineData("NOIBO-001", true)]       // internal store code
    [InlineData("bad code!", false)]
    public void IsValid_checks_gtin_check_digit_and_charset(string code, bool expected) =>
        Assert.Equal(expected, Barcode.IsValid(code));
}

public class ProductTests
{
    private static Product NewProduct() => new("sp001", "Sữa tươi Vinamilk 1L", 1, "hộp", 28_000m, 34_000m, VatRate.Five);

    [Fact]
    public void Code_is_uppercased()
    {
        Assert.Equal("SP001", NewProduct().Code);
    }

    [Fact]
    public void Invalid_vat_rate_is_rejected()
    {
        Assert.Throws<DomainException>(() => NewProduct().ChangeVatRate(7));
    }

    [Fact]
    public void Invalid_barcode_is_rejected()
    {
        Assert.Throws<DomainException>(() => NewProduct().SetBarcode("8934563138166"));
    }

    [Fact]
    public void ChangePrices_returns_previous_values_for_audit()
    {
        var product = NewProduct();

        var change = product.ChangePrices(29_000m, 35_000m);

        Assert.True(change.HasChanged);
        Assert.Equal(34_000m, change.OldSellPrice);
        Assert.Equal(35_000m, product.SellPrice);
    }

    [Fact]
    public void Stock_cannot_go_negative_unless_allowed()
    {
        var product = NewProduct();
        product.AdjustStock(5);

        Assert.Throws<DomainException>(() => product.AdjustStock(-6));
        product.AdjustStock(-6, allowNegative: true);
        Assert.Equal(-1m, product.StockQuantity);
    }

    [Fact]
    public void Low_stock_when_at_or_below_minimum()
    {
        var product = NewProduct();
        product.SetMinStockLevel(10);
        product.AdjustStock(10);

        Assert.True(product.IsLowStock);
        product.AdjustStock(1);
        Assert.False(product.IsLowStock);
    }
}
