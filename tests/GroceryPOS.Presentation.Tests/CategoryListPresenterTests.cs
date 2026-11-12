using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Catalog;

namespace GroceryPOS.Presentation.Tests;

public sealed class CategoryListPresenterTests
{
    private sealed class FakeListView : RecordingView, ICategoryListView
    {
        public bool EditorResult { get; set; }
        public List<CategoryDto?> OpenedFor { get; } = new();
        public int Reloads { get; private set; }
        public Action? OnEditorClosed { get; set; }

        public bool OpenEditor(CategoryDto? category)
        {
            OpenedFor.Add(category);
            OnEditorClosed?.Invoke();
            return EditorResult;
        }

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    private static readonly CategoryDto Milk = new(1, "Sữa", null, true);
    private static readonly CategoryDto Stopped = new(2, "Bánh kẹo", "Tết", false);

    private static (TestHost Host, CategoryListPresenter Presenter, FakeListView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.CategoriesManage) : new TestHost();
        var presenter = host.Get<CategoryListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Can_manage_follows_the_categories_manage_permission()
    {
        using var allowed = new TestHost(Permissions.CategoriesManage);
        using var denied = new TestHost(Permissions.ProductsView);
        Assert.True(allowed.Get<CategoryListPresenter>().CanManage);
        Assert.False(denied.Get<CategoryListPresenter>().CanManage);
    }

    [Fact]
    public async Task Load_returns_the_categories()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Categories.Items.AddRange(new[] { Milk, Stopped });

        var items = await presenter.LoadAsync();

        Assert.Equal(new[] { Milk, Stopped }, items);
        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task Failed_load_shows_an_error_toast_and_returns_null()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Categories.FailWith = Error.Forbidden();

        Assert.Null(await presenter.LoadAsync());
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
    }

    [Fact]
    public async Task Failed_load_after_the_view_closed_does_not_touch_it()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Categories.FailWith = Error.Conflict("x");
        view.IsAlive = false;

        Assert.Null(await presenter.LoadAsync());
        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task Saved_add_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.AddAsync();

        Assert.Equal(new CategoryDto?[] { null }, view.OpenedFor);
        Assert.Equal(new[] { "Đã thêm danh mục mới." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Saved_edit_names_the_category()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.EditAsync(Milk);

        Assert.Same(Milk, Assert.Single(view.OpenedFor));
        Assert.Equal(new[] { "Đã lưu danh mục Sữa." }, view.Successes);
    }

    [Fact]
    public async Task Cancelled_editor_does_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.EditAsync(Milk);

        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Screen_closed_while_the_editor_was_open_is_not_updated()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;
        view.OnEditorClosed = () => view.IsAlive = false;

        await presenter.AddAsync();

        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Without_permission_the_editor_is_not_opened()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        await presenter.AddAsync();
        await presenter.ToggleActiveAsync(Milk);

        Assert.Empty(view.OpenedFor);
        Assert.Empty(host.Categories.Calls);
        Assert.Equal(2, view.Warnings.Count);
    }

    [Fact]
    public async Task Toggle_stops_an_active_category()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ToggleActiveAsync(Milk);

        Assert.Equal(new[] { "deactivate:1" }, host.Categories.Calls);
        Assert.Equal(new[] { "Đã ngừng dùng danh mục Sữa." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Toggle_resumes_a_stopped_category()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ToggleActiveAsync(Stopped);

        Assert.Equal(new[] { "activate:2" }, host.Categories.Calls);
        Assert.Equal(new[] { "Đã dùng lại danh mục Bánh kẹo." }, view.Successes);
    }

    [Fact]
    public async Task Failed_toggle_warns_and_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Categories.FailWith = Error.Conflict("Danh mục đang có sản phẩm.");

        await presenter.ToggleActiveAsync(Milk);

        Assert.Equal(new[] { "Danh mục đang có sản phẩm." }, view.Warnings);
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Theory]
    [InlineData(true, "Ngừng dùng")]
    [InlineData(false, "Dùng lại")]
    public void Toggle_text_follows_the_state(bool active, string expected) =>
        Assert.Equal(expected, CategoryListPresenter.ToggleText(Milk with { IsActive = active }));

    [Fact]
    public void Toggle_text_without_selection_is_the_default() =>
        Assert.Equal("Ngừng dùng", CategoryListPresenter.ToggleText(null));

    [Fact]
    public async Task Using_the_presenter_before_attaching_a_view_fails_loudly()
    {
        using var host = new TestHost(Permissions.CategoriesManage);
        var presenter = host.Get<CategoryListPresenter>();
        await Assert.ThrowsAsync<InvalidOperationException>(presenter.AddAsync);
    }

    [Fact]
    public void A_presenter_serves_one_view()
    {
        using var host = new TestHost();
        var presenter = host.Get<CategoryListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        presenter.Attach(view); // re-attaching the same view is harmless
        Assert.Throws<InvalidOperationException>(() => presenter.Attach(new FakeListView()));
    }

    [Fact]
    public void Each_resolution_is_a_new_presenter()
    {
        using var host = new TestHost();
        Assert.NotSame(host.Get<CategoryListPresenter>(), host.Get<CategoryListPresenter>());
    }
}
