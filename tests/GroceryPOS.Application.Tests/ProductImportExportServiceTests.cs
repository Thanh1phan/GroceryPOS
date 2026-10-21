using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

/// <summary>Canned rows, never touches real Excel/ClosedXML I/O.</summary>
internal sealed class FakeTranscoder : IProductImportExportService
{
    public List<ProductImportRow> RowsToReturn { get; } = new();

    public Task<byte[]> ExportAsync(IReadOnlyList<ProductListItem> products, CancellationToken cancellationToken = default) =>
        Task.FromResult(Array.Empty<byte>());

    public Task<IReadOnlyList<ProductImportRow>> ParseAsync(Stream xlsxFile, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProductImportRow>>(RowsToReturn);
}

public class ProductImportExportServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCategoryRepository _categories = new();
    private readonly InMemoryPriceHistoryRepository _priceHistory = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();
    private readonly FakeTranscoder _transcoder = new();
    private readonly Category _category;

    public ProductImportExportServiceTests()
    {
        _category = new Category("Tạp hóa");
        _categories.Add(_category);
        SignIn(Permissions.ProductsView, Permissions.ProductsManage, Permissions.PricesChange);
    }

    private ProductService Products => new(_products, _categories, _priceHistory, _uow, _session, _audit, _clock);

    private ProductImportExportService CreateSut() => new(Products, _products, _categories, _transcoder, _session, _audit);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), _clock.Now));

    private ProductImportRow NewRow(int rowNumber, string code, decimal cost = 10_000, decimal sell = 12_000, bool isActive = true) =>
        new(rowNumber, code, null, "Sữa tươi Vinamilk 1L", _category.Name, "hộp", cost, sell, VatRate.Five, 10, isActive);

    [Fact]
    public async Task New_code_is_created_with_zero_stock()
    {
        _transcoder.RowsToReturn.Add(NewRow(2, "SUA01"));

        var result = await CreateSut().ImportAsync(Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Created);
        Assert.Equal(0, result.Value.Failed);
        Assert.Equal(ImportRowOutcome.Created, Assert.Single(result.Value.Rows).Outcome);
        Assert.Equal(0m, Assert.Single(_products.All).StockQuantity);
    }

    [Fact]
    public async Task Existing_code_is_updated_and_recorded_once()
    {
        await Products.CreateAsync(new SaveProductRequest("SUA01", null, "Sữa tươi Vinamilk 1L", _category.Id, "hộp", 10_000, 12_000, VatRate.Five, 10));
        _transcoder.RowsToReturn.Add(NewRow(2, "SUA01", cost: 10_000, sell: 12_000)); // unchanged price

        var result = await CreateSut().ImportAsync(Stream.Null);

        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(0, result.Value.Created);
        Assert.Single(_products.All);
    }

    [Fact]
    public async Task Price_change_on_existing_product_without_PricesChange_rejects_whole_import()
    {
        await Products.CreateAsync(new SaveProductRequest("SUA01", null, "Sữa tươi Vinamilk 1L", _category.Id, "hộp", 10_000, 12_000, VatRate.Five, 10));
        _transcoder.RowsToReturn.Add(NewRow(2, "SUA02")); // new product, no price history to violate
        _transcoder.RowsToReturn.Add(NewRow(3, "SUA01", sell: 20_000)); // price differs from existing

        SignIn(Permissions.ProductsView, Permissions.ProductsManage); // no PricesChange
        var result = await CreateSut().ImportAsync(Stream.Null);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Single(_products.All); // SUA02 was never written
    }

    [Fact]
    public async Task Unknown_category_fails_that_row_but_other_rows_still_process()
    {
        _transcoder.RowsToReturn.Add(NewRow(2, "SUA01"));
        _transcoder.RowsToReturn.Add(new ProductImportRow(3, "SUA02", null, "Bia lạ", "Không tồn tại", "lon", 10_000, 12_000, VatRate.Five, 10, true));

        var result = await CreateSut().ImportAsync(Stream.Null);

        Assert.Equal(1, result.Value.Created);
        Assert.Equal(1, result.Value.Failed);
        var failedRow = result.Value.Rows.Single(r => r.Outcome == ImportRowOutcome.Failed);
        Assert.Equal(3, failedRow.RowNumber);
        Assert.Contains("Không tồn tại", failedRow.Message);
        Assert.Single(_products.All);
    }

    [Fact]
    public async Task Import_requires_products_manage_permission()
    {
        SignIn(Permissions.ProductsView);
        _transcoder.RowsToReturn.Add(NewRow(2, "SUA01"));

        var result = await CreateSut().ImportAsync(Stream.Null);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(_products.All);
    }
}
