using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Presentation.Catalog;

namespace GroceryPOS.Presentation.Tests;

public sealed class CategoryEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, ICategoryEditView
    {
        public string CategoryName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int NameFocused { get; private set; }

        public void FocusName() => NameFocused++;
    }

    private static readonly CategoryDto Milk = new(7, "Sữa", "Sữa tươi, sữa hộp", true);

    private static (TestHost Host, CategoryEditPresenter Presenter, FakeEditView View) Create(CategoryDto? category)
    {
        var host = new TestHost();
        var presenter = host.Get<CategoryEditPresenter>().For(category);
        var view = new FakeEditView { CategoryName = presenter.InitialName, Description = presenter.InitialDescription };
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void New_category_starts_blank()
    {
        var (host, presenter, _) = Create(null);
        using var _h = host;
        Assert.True(presenter.IsNew);
        Assert.Equal("Thêm danh mục", presenter.Title);
        Assert.Equal(string.Empty, presenter.InitialName);
        Assert.Equal(string.Empty, presenter.InitialDescription);
    }

    [Fact]
    public void Editing_starts_from_the_category()
    {
        var (host, presenter, _) = Create(Milk);
        using var _h = host;
        Assert.False(presenter.IsNew);
        Assert.Equal("Sửa danh mục", presenter.Title);
        Assert.Equal("Sữa", presenter.InitialName);
        Assert.Equal("Sữa tươi, sữa hộp", presenter.InitialDescription);
    }

    [Fact]
    public void Missing_description_starts_as_empty_text()
    {
        var (host, presenter, _) = Create(Milk with { Description = null });
        using var _h = host;
        Assert.Equal(string.Empty, presenter.InitialDescription);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("Đồ khô", "Đồ khô")]
    public void Blank_description_is_sent_as_null(string? description, string? expected) =>
        Assert.Equal(expected, CategoryEditPresenter.BuildRequest("Sữa", description).Description);

    [Fact]
    public async Task Adding_creates_the_category()
    {
        var (host, presenter, view) = Create(null);
        using var _h = host;
        view.CategoryName = "Đồ uống";

        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(new[] { "create:Đồ uống|<null>" }, host.Categories.Calls);
    }

    [Fact]
    public async Task Editing_updates_the_same_category()
    {
        var (host, presenter, view) = Create(Milk);
        using var _h = host;
        view.Description = "Sữa các loại";

        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(new[] { "update:7:Sữa|Sữa các loại" }, host.Categories.Calls);
    }

    [Fact]
    public async Task Empty_name_is_rejected_before_calling_the_service_and_focuses_the_name()
    {
        var (host, presenter, view) = Create(null);
        using var _h = host;
        view.CategoryName = "  ";

        Assert.Equal("Tên danh mục không được để trống.", await presenter.SaveAsync());
        Assert.Empty(host.Categories.Calls);
        Assert.Equal(1, view.NameFocused);
    }

    [Fact]
    public async Task Service_errors_go_to_the_error_banner()
    {
        var (host, presenter, view) = Create(Milk);
        using var _h = host;
        host.Categories.FailWith = Error.Conflict("Đã có danh mục tên Sữa.");

        Assert.Equal("Đã có danh mục tên Sữa.", await presenter.SaveAsync());
        Assert.Equal(0, view.NameFocused);
    }
}
