using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Partners;

namespace GroceryPOS.Presentation.Tests;

public sealed class SupplierListPresenterTests
{
    private sealed class FakeListView : RecordingView, ISupplierListView
    {
        public bool EditorResult { get; set; }
        public List<SupplierDto?> OpenedFor { get; } = new();
        public int Reloads { get; private set; }
        public Action? OnEditorClosed { get; set; }

        public bool OpenEditor(SupplierDto? supplier)
        {
            OpenedFor.Add(supplier);
            OnEditorClosed?.Invoke();
            return EditorResult;
        }

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    private static readonly SupplierDto Vinamilk = new(1, "NCC0001", "Vinamilk", "Chị Lan", "0901234567", null, null, "0300588569", 1_500_000, true);
    private static readonly SupplierDto Stopped = new(2, "NCC0002", "Đại lý Hòa Bình", null, null, null, null, null, 0, false);

    private static (TestHost Host, SupplierListPresenter Presenter, FakeListView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.SuppliersManage) : new TestHost();
        var presenter = host.Get<SupplierListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Can_manage_follows_the_suppliers_manage_permission()
    {
        using var allowed = new TestHost(Permissions.SuppliersManage);
        using var denied = new TestHost(Permissions.CategoriesManage);
        Assert.True(allowed.Get<SupplierListPresenter>().CanManage);
        Assert.False(denied.Get<SupplierListPresenter>().CanManage);
    }

    [Theory]
    [InlineData(true, "Ngừng giao dịch")]
    [InlineData(false, "Giao dịch lại")]
    public void Toggle_text_follows_the_selected_row(bool active, string expected) =>
        Assert.Equal(expected, SupplierListPresenter.ToggleText(Vinamilk with { IsActive = active }));

    [Fact]
    public void Toggle_text_without_selection_is_the_stop_caption() =>
        Assert.Equal("Ngừng giao dịch", SupplierListPresenter.ToggleText(null));

    [Fact]
    public async Task Load_returns_the_suppliers()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.Items.AddRange(new[] { Vinamilk, Stopped });

        Assert.Equal(new[] { Vinamilk, Stopped }, await presenter.LoadAsync());
        Assert.Empty(view.Errors);
    }

    [Fact]
    public async Task Failed_load_shows_an_error_toast_and_returns_null()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Forbidden();

        Assert.Null(await presenter.LoadAsync());
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
    }

    [Fact]
    public async Task Failed_load_after_the_view_closed_does_not_touch_it()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Forbidden();
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

        Assert.Equal(new SupplierDto?[] { null }, view.OpenedFor);
        Assert.Equal(new[] { "Đã thêm nhà cung cấp mới." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Saved_edit_names_the_supplier()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.EditAsync(Vinamilk);

        Assert.Same(Vinamilk, Assert.Single(view.OpenedFor));
        Assert.Equal(new[] { "Đã lưu nhà cung cấp Vinamilk." }, view.Successes);
    }

    [Fact]
    public async Task Cancelled_or_closed_editor_does_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.EditAsync(Vinamilk);
        view.EditorResult = true;
        view.OnEditorClosed = () => view.IsAlive = false;
        await presenter.AddAsync();

        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Without_permission_nothing_is_opened_or_changed()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        await presenter.AddAsync();
        await presenter.EditAsync(Vinamilk);
        await presenter.ToggleActiveAsync(Vinamilk);

        Assert.Empty(view.OpenedFor);
        Assert.Empty(view.Questions);
        Assert.Empty(host.Suppliers.Calls);
        Assert.Equal(3, view.Warnings.Count);
        Assert.All(view.Warnings, w => Assert.Equal(SupplierListPresenter.NoPermission, w));
    }

    [Fact]
    public async Task Stopping_asks_first_then_deactivates_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ToggleActiveAsync(Vinamilk);

        Assert.Equal(new[] { SupplierListPresenter.StopQuestion(Vinamilk) }, view.Questions);
        Assert.Contains("Vinamilk", view.Questions[0]);
        Assert.Equal(new[] { "deactivate:1" }, host.Suppliers.Calls);
        Assert.Equal(new[] { "Đã ngừng giao dịch với Vinamilk." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Declining_the_question_changes_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.ConfirmAnswer = false;

        await presenter.ToggleActiveAsync(Vinamilk);

        Assert.Single(view.Questions);
        Assert.Empty(host.Suppliers.Calls);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Resuming_needs_no_confirmation()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ToggleActiveAsync(Stopped);

        Assert.Empty(view.Questions);
        Assert.Equal(new[] { "activate:2" }, host.Suppliers.Calls);
        Assert.Equal(new[] { "Đã giao dịch lại với Đại lý Hòa Bình." }, view.Successes);
    }

    [Fact]
    public async Task Failed_stop_shows_the_reason_and_does_not_reload()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Conflict("Nhà cung cấp còn công nợ.");

        await presenter.ToggleActiveAsync(Vinamilk);

        Assert.Equal(new[] { "Nhà cung cấp còn công nợ." }, view.Warnings);
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }
}
