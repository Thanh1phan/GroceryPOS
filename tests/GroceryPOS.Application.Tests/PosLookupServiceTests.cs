using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Tests;

public class PosLookupServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly PosLookupService _sut;

    public PosLookupServiceTests()
    {
        var chocolate = new Product("KEO01", "Kẹo sô-cô-la", 2, "thanh", 8_000, 12_000);
        chocolate.SetBarcode("4006381333931");
        _products.Add(chocolate);

        var discontinued = new Product("KEO02", "Kẹo gừng (ngừng bán)", 2, "gói", 5_000, 7_000);
        discontinued.Deactivate();
        _products.Add(discontinued);

        _customers.Add(new Customer("KH001", "Lê Thị C", "0912 345 678"));
        _sut = new PosLookupService(_products, _customers);
    }

    [Theory]
    [InlineData("4006381333931")]
    [InlineData(" 4006381333931 ")]
    [InlineData("keo01")]
    public async Task Scan_finds_product_by_barcode_or_code(string input)
    {
        var result = await _sut.FindForScanAsync(input);

        Assert.True(result.IsSuccess);
        Assert.Equal("KEO01", result.Value.Code);
        Assert.Equal(12_000m, result.Value.SellPrice);
    }

    [Fact]
    public async Task Scan_reports_unknown_empty_and_discontinued_products()
    {
        Assert.Equal("not_found", (await _sut.FindForScanAsync("8930000000000")).Error.Code);
        Assert.Equal("validation", (await _sut.FindForScanAsync("  ")).Error.Code);
        Assert.Equal("sale.product", (await _sut.FindForScanAsync("KEO02")).Error.Code);
    }

    [Fact]
    public async Task Search_returns_only_active_products()
    {
        var found = await _sut.SearchAsync("kẹo");

        Assert.Equal("KEO01", Assert.Single(found).Code);
        Assert.Empty(await _sut.SearchAsync(""));
    }

    [Theory]
    [InlineData("0912345678")]
    [InlineData("+84 912 345 678")]
    public async Task Finds_member_by_phone_in_any_format(string phone)
    {
        var result = await _sut.FindCustomerByPhoneAsync(phone);

        Assert.True(result.IsSuccess);
        Assert.Equal("KH001", result.Value.Code);
    }

    [Fact]
    public async Task Unknown_phone_is_not_found()
    {
        Assert.Equal("not_found", (await _sut.FindCustomerByPhoneAsync("0987654321")).Error.Code);
        Assert.Equal("validation", (await _sut.FindCustomerByPhoneAsync("")).Error.Code);
    }
}
