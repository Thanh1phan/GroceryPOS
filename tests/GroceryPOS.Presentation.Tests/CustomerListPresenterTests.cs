using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Partners;

namespace GroceryPOS.Presentation.Tests;

public sealed class CustomerListPresenterTests
{
    private sealed class FakeListView : RecordingView, ICustomerListView
    {
        public bool EditorResult { get; set; }
        public List<CustomerDto?> OpenedFor { get; } = new();
        public List<CustomerDto> HistoryFor { get; } = new();
        public int Reloads { get; private set; }

        public bool OpenEditor(CustomerDto? customer)
        {
            OpenedFor.Add(customer);
            return EditorResult;
        }

        public void OpenLoyaltyHistory(CustomerDto customer) => HistoryFor.Add(customer);

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    private static readonly CustomerDto An = new(1, "KH000001", "Nguyễn Văn An", "0901234567", null, null, 120, 2_400_000, true);
    private static readonly CustomerDto Binh = new(2, "KH000002", "Trần Thị Bình", null, null, null, 0, 0, false);

    private static (TestHost Host, CustomerListPresenter Presenter, FakeListView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.CustomersManage) : new TestHost(Permissions.SalesCreate);
        var presenter = host.Get<CustomerListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Theory]
    [InlineData(true, "Ngừng hoạt động")]
    [InlineData(false, "Kích hoạt lại")]
    public void Toggle_text_follows_the_selected_row(bool active, string expected) =>
        Assert.Equal(expected, CustomerListPresenter.ToggleText(An with { IsActive = active }));

    [Fact]
    public async Task Load_returns_customers_or_toasts_the_error()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Customers.Items.AddRange(new[] { An, Binh });
        Assert.Equal(new[] { An, Binh }, await presenter.LoadAsync());

        host.Customers.FailWith = Error.Forbidden();
        Assert.Null(await presenter.LoadAsync());
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
    }

    [Fact]
    public async Task Activating_a_row_edits_for_managers()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.ActivateAsync(An);

        Assert.Same(An, Assert.Single(view.OpenedFor));
        Assert.Empty(view.HistoryFor);
        Assert.Equal(new[] { "Đã lưu khách hàng Nguyễn Văn An." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Activating_a_row_shows_the_history_for_cashiers()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        await presenter.ActivateAsync(An);

        Assert.Empty(view.OpenedFor);
        Assert.Same(An, Assert.Single(view.HistoryFor));
        Assert.Empty(view.Warnings);
    }

    [Fact]
    public void History_is_open_without_the_manage_permission()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        presenter.ShowHistory(Binh);

        Assert.Same(Binh, Assert.Single(view.HistoryFor));
    }

    [Fact]
    public async Task Saved_add_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.AddAsync();

        Assert.Equal(new[] { "Đã thêm khách hàng mới." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Without_permission_add_edit_and_toggle_are_refused()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;

        await presenter.AddAsync();
        await presenter.EditAsync(An);
        await presenter.ToggleActiveAsync(An);

        Assert.Empty(view.OpenedFor);
        Assert.Empty(host.Customers.Calls);
        Assert.Equal(3, view.Warnings.Count);
    }

    [Fact]
    public async Task Deactivating_asks_first()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        view.ConfirmAnswer = false;
        await presenter.ToggleActiveAsync(An);
        Assert.Empty(host.Customers.Calls);

        view.ConfirmAnswer = true;
        await presenter.ToggleActiveAsync(An);
        Assert.Equal(2, view.Questions.Count);
        Assert.Contains("điểm tích lũy được giữ nguyên", view.Questions[0]);
        Assert.Equal(new[] { "deactivate:1" }, host.Customers.Calls);
        Assert.Equal(new[] { "Đã ngừng hoạt động khách hàng Nguyễn Văn An." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Reactivating_needs_no_confirmation()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ToggleActiveAsync(Binh);

        Assert.Empty(view.Questions);
        Assert.Equal(new[] { "activate:2" }, host.Customers.Calls);
        Assert.Equal(new[] { "Đã kích hoạt lại khách hàng Trần Thị Bình." }, view.Successes);
    }

    [Fact]
    public async Task Failed_toggle_shows_the_reason_and_closed_view_is_left_alone()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Customers.FailWith = Error.NotFound("Không tìm thấy khách hàng.");

        await presenter.ToggleActiveAsync(Binh);
        Assert.Equal(new[] { "Không tìm thấy khách hàng." }, view.Warnings);

        view.IsAlive = false;
        host.Customers.FailWith = null;
        await presenter.ToggleActiveAsync(Binh);
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }
}
