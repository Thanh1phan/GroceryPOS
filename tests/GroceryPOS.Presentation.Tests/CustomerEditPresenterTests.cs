using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Partners;

namespace GroceryPOS.Presentation.Tests;

public sealed class CustomerEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, ICustomerEditView
    {
        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public List<string> Focused { get; } = new();

        public void FocusField(string field) => Focused.Add(field);
    }

    private static readonly CustomerDto An = new(3, "KH000003", "Nguyễn Văn An", "0901234567", null, null, 120, 2_400_000, true);

    private static (TestHost Host, CustomerEditPresenter Presenter, FakeEditView View) Create(CustomerDto? customer = null)
    {
        var host = new TestHost();
        var presenter = host.Get<CustomerEditPresenter>().For(customer);
        var view = new FakeEditView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Title_and_loyalty_fields_follow_the_mode()
    {
        var (addHost, add, _) = Create();
        var (editHost, edit, _) = Create(An);
        using var a = addHost;
        using var e = editHost;

        Assert.Equal("Thêm khách hàng", add.Title);
        Assert.False(add.ShowsLoyalty);
        Assert.Equal("Sửa khách hàng – KH000003", edit.Title);
        Assert.True(edit.ShowsLoyalty);
    }

    [Fact]
    public void Build_request_nulls_blank_optional_fields() =>
        Assert.Equal(
            new SaveCustomerRequest("An", null, "an@example.com", null),
            CustomerEditPresenter.BuildRequest("An", "  ", " an@example.com ", ""));

    [Theory]
    [InlineData("", "0901234567", "", nameof(SaveCustomerRequest.Name))]
    [InlineData("An", "12", "", nameof(SaveCustomerRequest.Phone))]
    [InlineData("An", "", "not-an-email", nameof(SaveCustomerRequest.Email))]
    public async Task Invalid_input_focuses_the_offending_field(string name, string phone, string email, string field)
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.CustomerName = name;
        view.Phone = phone;
        view.Email = email;

        Assert.NotNull(await presenter.SaveAsync());
        Assert.Equal(new[] { field }, view.Focused);
        Assert.Empty(host.Customers.Calls);
    }

    [Fact]
    public async Task Add_creates_and_edit_updates()
    {
        var (host, add, addView) = Create();
        using var _ = host;
        addView.CustomerName = "Lê Văn Cường";
        addView.Phone = "0912 345 678";
        Assert.Null(await add.SaveAsync());
        Assert.Equal("0912 345 678", host.Customers.LastRequest!.Phone);

        var (editHost, edit, editView) = Create(An);
        using var _e = editHost;
        editView.CustomerName = "Nguyễn Văn An";
        Assert.Null(await edit.SaveAsync());

        Assert.Equal(new[] { "create:Lê Văn Cường" }, host.Customers.Calls);
        Assert.Equal(new[] { "update:3:Nguyễn Văn An" }, editHost.Customers.Calls);
    }

    [Fact]
    public async Task Service_error_goes_to_the_banner()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.CustomerName = "An";
        host.Customers.FailWith = Error.Conflict("Số điện thoại đã được dùng cho khách hàng khác.");

        Assert.Equal("Số điện thoại đã được dùng cho khách hàng khác.", await presenter.SaveAsync());
        Assert.Empty(view.Focused);
    }
}
