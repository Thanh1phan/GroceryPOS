using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public class ProductServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCategoryRepository _categories = new();
    private readonly InMemoryPriceHistoryRepository _priceHistory = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();
    private readonly Category _category;

    public ProductServiceTests()
    {
        _category = new Category("Tạp hóa");
        _categories.Add(_category);
        SignIn(Permissions.ProductsView, Permissions.ProductsManage, Permissions.PricesChange);
    }

    private ProductService CreateSut() => new(_products, _categories, _priceHistory, _uow, _session, _audit, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), _clock.Now));

    private SaveProductRequest NewRequest(string code = "SUA01", decimal cost = 10_000, decimal sell = 12_000) =>
        new(code, null, "Sữa tươi Vinamilk 1L", _category.Id, "hộp", cost, sell, VatRate.Five, 10);

    [Fact]
    public async Task Create_adds_product_with_zero_stock_and_audits()
    {
        var result = await CreateSut().CreateAsync(NewRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.StockQuantity);
        Assert.Equal("Tạp hóa", result.Value.CategoryName);
        Assert.Equal(AuditActions.ProductCreated, Assert.Single(_audit.Entries).Action);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Create_rejects_duplicate_code_duplicate_barcode_and_missing_category()
    {
        await CreateSut().CreateAsync(NewRequest());

        var duplicateCode = await CreateSut().CreateAsync(NewRequest());
        Assert.Equal("conflict", duplicateCode.Error.Code);

        var withBarcode = NewRequest("SUA02") with { Barcode = "8934673000017" };
        await CreateSut().CreateAsync(withBarcode);
        var duplicateBarcode = await CreateSut().CreateAsync(NewRequest("SUA03") with { Barcode = "8934673000017" });
        Assert.Equal("conflict", duplicateBarcode.Error.Code);

        var missingCategory = await CreateSut().CreateAsync(NewRequest("SUA04") with { CategoryId = 999 });
        Assert.Equal("not_found", missingCategory.Error.Code);
    }

    [Fact]
    public async Task Update_non_price_fields_does_not_require_PricesChange()
    {
        var created = await CreateSut().CreateAsync(NewRequest());
        SignIn(Permissions.ProductsView, Permissions.ProductsManage); // no PricesChange

        var result = await CreateSut().UpdateAsync(created.Value.Id, NewRequest() with { Name = "Sữa tươi Vinamilk 1L (mới)" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Sữa tươi Vinamilk 1L (mới)", result.Value.Name);
        Assert.Empty(_priceHistory.All);
    }

    [Fact]
    public async Task Update_price_without_PricesChange_is_forbidden_and_writes_nothing()
    {
        var created = await CreateSut().CreateAsync(NewRequest());
        SignIn(Permissions.ProductsView, Permissions.ProductsManage); // no PricesChange

        var result = await CreateSut().UpdateAsync(created.Value.Id, NewRequest(sell: 15_000));

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(12_000m, (await _products.GetByIdAsync(created.Value.Id))!.SellPrice);
        Assert.Empty(_priceHistory.All);
    }

    [Fact]
    public async Task Update_price_with_PricesChange_writes_one_price_history_row_and_audits()
    {
        var created = await CreateSut().CreateAsync(NewRequest());

        var result = await CreateSut().UpdateAsync(created.Value.Id, NewRequest(cost: 11_000, sell: 15_000));

        Assert.True(result.IsSuccess);
        Assert.Equal(15_000m, result.Value.SellPrice);
        var history = Assert.Single(_priceHistory.All);
        Assert.Equal(10_000m, history.OldCostPrice);
        Assert.Equal(11_000m, history.NewCostPrice);
        Assert.Equal(12_000m, history.OldSellPrice);
        Assert.Equal(15_000m, history.NewSellPrice);
        Assert.Equal(PriceChangeSource.ManualEdit, history.Source);
        Assert.Contains(AuditActions.PriceChanged, _audit.Actions);
        Assert.Contains(AuditActions.ProductUpdated, _audit.Actions);
    }

    [Fact]
    public async Task Update_without_price_change_writes_no_history_row()
    {
        var created = await CreateSut().CreateAsync(NewRequest());

        await CreateSut().UpdateAsync(created.Value.Id, NewRequest());

        Assert.Empty(_priceHistory.All);
        Assert.DoesNotContain(AuditActions.PriceChanged, _audit.Actions);
    }

    [Fact]
    public async Task SetActive_toggles_status_and_audits()
    {
        var created = await CreateSut().CreateAsync(NewRequest());

        var deactivated = await CreateSut().SetActiveAsync(created.Value.Id, isActive: false);
        Assert.True(deactivated.IsSuccess);
        Assert.False((await _products.GetByIdAsync(created.Value.Id))!.IsActive);

        var activated = await CreateSut().SetActiveAsync(created.Value.Id, isActive: true);
        Assert.True(activated.IsSuccess);
        Assert.True((await _products.GetByIdAsync(created.Value.Id))!.IsActive);
        Assert.Equal(2, _audit.Actions.Count(a => a == AuditActions.ProductStatusChanged));
    }

    [Fact]
    public async Task Operations_require_permission_and_sign_in()
    {
        SignIn(Permissions.ProductsView);
        var forbidden = await CreateSut().CreateAsync(NewRequest());
        Assert.Equal("forbidden", forbidden.Error.Code);

        _session.SignOut();
        var unauthenticated = await CreateSut().ListAsync();
        Assert.Equal("auth.required", unauthenticated.Error.Code);
    }
}
