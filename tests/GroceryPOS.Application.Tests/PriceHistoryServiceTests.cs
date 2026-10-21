using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public class PriceHistoryServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryPriceHistoryRepository _history = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly UserSessionStore _session = new();
    private readonly FakeClock _clock = new();
    private readonly Product _product;

    public PriceHistoryServiceTests()
    {
        _product = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", 10_000, 12_000);
        _products.Add(_product);

        var admin = new User("admin", "Quản trị viên", "hash", new Role(SystemRoles.Admin, isSystem: true));
        _users.Add(admin);

        var change1 = _product.ChangePrices(11_000, 13_000);
        _history.Add(PriceHistory.From(_product, change1, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), admin.Id, PriceChangeSource.ManualEdit)!);

        var change2 = _product.ChangePrices(12_000, 14_000);
        _history.Add(PriceHistory.From(_product, change2, new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero), null, PriceChangeSource.GoodsReceipt)!);

        SignIn(Permissions.ProductsView);
    }

    private PriceHistoryService CreateSut() => new(_products, _history, _users, _session);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), _clock.Now));

    [Fact]
    public async Task Lists_entries_newest_first_with_username_and_source()
    {
        var result = await CreateSut().ListByProductAsync(_product.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(14_000m, result.Value[0].NewSellPrice);
        Assert.Equal("Nhập hàng", result.Value[0].SourceName);
        Assert.Null(result.Value[0].ChangedByUsername);
        Assert.Equal("admin", result.Value[1].ChangedByUsername);
        Assert.Equal("Sửa trực tiếp", result.Value[1].SourceName);
    }

    [Fact]
    public async Task Date_range_filters_entries()
    {
        var result = await CreateSut().ListByProductAsync(_product.Id, from: new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(14_000m, Assert.Single(result.Value).NewSellPrice);
    }

    [Fact]
    public async Task Unknown_product_returns_not_found()
    {
        var result = await CreateSut().ListByProductAsync(999);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Requires_permission_and_sign_in()
    {
        SignIn();
        var forbidden = await CreateSut().ListByProductAsync(_product.Id);
        Assert.Equal("forbidden", forbidden.Error.Code);

        _session.SignOut();
        var unauthenticated = await CreateSut().ListByProductAsync(_product.Id);
        Assert.Equal("auth.required", unauthenticated.Error.Code);
    }
}
