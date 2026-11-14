using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Catalog;

namespace GroceryPOS.Presentation.Tests;

public sealed class ProductEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, IProductEditView
    {
        public string Code { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal CostPrice { get; set; }
        public decimal SellPrice { get; set; }
        public decimal? VatRate { get; set; }
        public decimal MinStockLevel { get; set; }
        public bool IsActive { get; set; } = true;
        public List<CategoryDto>? Categories { get; private set; }
        public int? SelectedCategory { get; private set; }
        public List<string> Focused { get; } = new();

        public void ShowCategories(IReadOnlyList<CategoryDto> categories, int? selectedCategoryId)
        {
            Categories = categories.ToList();
            SelectedCategory = selectedCategoryId;
        }

        public void FocusField(string field) => Focused.Add(field);

        public void Fill(ProductDetail p)
        {
            Code = p.Code;
            Barcode = p.Barcode ?? string.Empty;
            ProductName = p.Name;
            CategoryId = p.CategoryId;
            Unit = p.Unit;
            CostPrice = p.CostPrice;
            SellPrice = p.SellPrice;
            VatRate = p.VatRate;
            MinStockLevel = p.MinStockLevel;
            IsActive = p.IsActive;
        }
    }

    private static readonly ProductDetail Milk = ProductListPresenterTests.MilkDetail;

    private static (TestHost Host, ProductEditPresenter Presenter, FakeEditView View) Create(ProductDetail? product = null, bool canChangePrice = true)
    {
        var host = canChangePrice
            ? new TestHost(Permissions.ProductsManage, Permissions.PricesChange)
            : new TestHost(Permissions.ProductsManage);
        if (product is not null)
        {
            host.Products.Details[product.Id] = product;
        }

        var presenter = host.Get<ProductEditPresenter>().For(product);
        var view = new FakeEditView();
        presenter.Attach(view);
        if (product is not null)
        {
            view.Fill(product);
        }

        return (host, presenter, view);
    }

    private static void FillNew(FakeEditView view)
    {
        view.Code = " MI01 ";
        view.Barcode = "  ";
        view.ProductName = " Mì Hảo Hảo tôm chua cay ";
        view.CategoryId = 2;
        view.Unit = "gói";
        view.CostPrice = 3_500;
        view.SellPrice = 4_500;
        view.VatRate = 8;
    }

    [Fact]
    public void Title_vat_and_price_permission()
    {
        var (host, add, _) = Create();
        using var _h = host;
        Assert.Equal("Thêm sản phẩm", add.Title);
        Assert.Equal(0m, add.InitialVat.Rate);
        Assert.Equal("Không chịu thuế", add.InitialVat.Text);
        Assert.Equal(new[] { "Không chịu thuế", "5%", "8%", "10%" }, ProductEditPresenter.VatOptions.Select(o => o.Text));
        Assert.True(add.CanChangePrice);

        var (h2, edit, _) = Create(Milk, canChangePrice: false);
        using var _h2 = h2;
        Assert.Equal("Sửa sản phẩm – SUA01", edit.Title);
        Assert.Equal(8m, edit.InitialVat.Rate);
        Assert.False(edit.CanChangePrice);
        Assert.Equal("Bạn không có quyền thay đổi giá.", edit.CostHelperText);
    }

    [Fact]
    public async Task Category_list_shows_active_ones_plus_the_current_one()
    {
        var (host, presenter, view) = Create(Milk with { CategoryId = 5 });
        using var _h = host;
        host.Categories.Items.AddRange(new[]
        {
            new CategoryDto(1, "Sữa", null, true),
            new CategoryDto(5, "Hàng cũ", null, false),
            new CategoryDto(6, "Ngừng", null, false),
        });

        Assert.Null(await presenter.LoadCategoriesAsync());

        Assert.Equal(new[] { 1, 5 }, view.Categories!.Select(c => c.Id));
        Assert.Equal(5, view.SelectedCategory);

        host.Categories.FailWith = Error.Forbidden();
        Assert.Equal(Error.Forbidden().Message, await presenter.LoadCategoriesAsync());
    }

    [Fact]
    public async Task Missing_category_or_vat_focuses_the_field()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        FillNew(view);
        view.CategoryId = null;
        Assert.Equal(ProductEditPresenter.ChooseCategory, await presenter.SaveAsync());

        view.CategoryId = 2;
        view.VatRate = null;
        Assert.Equal(ProductEditPresenter.ChooseVat, await presenter.SaveAsync());

        Assert.Equal(new[] { "CategoryId", "VatRate" }, view.Focused);
        Assert.Empty(host.Products.Calls);
    }

    [Theory]
    [InlineData("", "Tên", "gói", "Code")]
    [InlineData("MI01", " ", "gói", "Name")]
    [InlineData("MI01", "Tên", "", "Unit")]
    public async Task Invalid_fields_are_focused(string code, string name, string unit, string field)
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        FillNew(view);
        view.Code = code;
        view.ProductName = name;
        view.Unit = unit;

        Assert.False(string.IsNullOrEmpty(await presenter.SaveAsync()));
        Assert.Equal(new[] { field }, view.Focused);
        Assert.Empty(host.Products.Calls);
    }

    [Fact]
    public async Task Creating_trims_and_drops_a_blank_barcode()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        FillNew(view);

        Assert.Null(await presenter.SaveAsync());

        var request = host.Products.LastRequest!;
        Assert.Equal("MI01", request.Code);
        Assert.Null(request.Barcode);
        Assert.Equal("Mì Hảo Hảo tôm chua cay", request.Name);
        Assert.Equal(new[] { "create:MI01" }, host.Products.Calls);
        Assert.Empty(view.Questions);
    }

    [Fact]
    public async Task Selling_at_a_loss_asks_with_a_red_button()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        FillNew(view);
        view.SellPrice = 3_000;

        view.ConfirmAnswer = false;
        Assert.Equal(string.Empty, await presenter.SaveAsync());
        Assert.Equal(new[] { "SellPrice" }, view.Focused);
        Assert.Empty(host.Products.Calls);

        view.ConfirmAnswer = true;
        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(2, view.DangerQuestions.Count);
        Assert.Equal(new[] { "create:MI01" }, host.Products.Calls);
    }

    [Fact]
    public async Task Unchanged_loss_making_prices_do_not_nag_on_edit()
    {
        var lossLeader = Milk with { CostPrice = 9_000, SellPrice = 8_000 };
        var (host, presenter, view) = Create(lossLeader);
        using var _h = host;
        view.ProductName = "Sữa tươi Vinamilk 180ml (khuyến mãi)";

        Assert.False(presenter.NeedsLossConfirmation(9_000, 8_000));
        Assert.True(presenter.NeedsLossConfirmation(9_000, 7_900));
        Assert.Null(await presenter.SaveAsync());
        Assert.Empty(view.Questions);
        Assert.Equal(new[] { "update:7" }, host.Products.Calls);
    }

    [Fact]
    public async Task Unticking_dang_ban_stops_selling_after_saving()
    {
        var (host, presenter, view) = Create(Milk);
        using var _h = host;
        view.IsActive = false;

        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(new[] { "update:7", "active:7:False" }, host.Products.Calls);

        host.Products.SetActiveFailsWith = Error.Conflict("Không thể ngừng bán.");
        Assert.Equal("Không thể ngừng bán.", await presenter.SaveAsync());
    }

    [Fact]
    public async Task Service_error_goes_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        FillNew(view);
        host.Products.FailWith = Error.Conflict("Mã sản phẩm đã tồn tại.");

        Assert.Equal("Mã sản phẩm đã tồn tại.", await presenter.SaveAsync());
        Assert.Empty(view.Focused);
    }
}
