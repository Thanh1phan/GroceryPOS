using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Partners;

namespace GroceryPOS.Presentation.Tests;

public sealed class SupplierEditPresenterTests
{
    private sealed class FakeEditView : RecordingView, ISupplierEditView
    {
        public string Code { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string TaxCode { get; set; } = string.Empty;
        public List<string> Focused { get; } = new();

        public void FocusField(string field) => Focused.Add(field);
    }

    private static readonly SupplierDto Vinamilk = new(5, "NCC0005", "Vinamilk", null, null, null, null, null, 2_000_000, true);

    private static (TestHost Host, SupplierEditPresenter Presenter, FakeEditView View) Create(SupplierDto? supplier = null)
    {
        var host = new TestHost();
        var presenter = host.Get<SupplierEditPresenter>().For(supplier);
        var view = new FakeEditView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Add_and_edit_modes()
    {
        var (addHost, add, _) = Create();
        var (editHost, edit, _) = Create(Vinamilk);
        using var a = addHost;
        using var e = editHost;

        Assert.True(add.IsNew);
        Assert.Equal("Thêm nhà cung cấp", add.Title);
        Assert.False(add.CodeReadOnly);
        Assert.Equal(0, add.DebtBalance);

        Assert.False(edit.IsNew);
        Assert.Equal("Sửa nhà cung cấp", edit.Title);
        Assert.True(edit.CodeReadOnly);
        Assert.Equal(2_000_000, edit.DebtBalance);
    }

    [Fact]
    public void Build_request_trims_optional_fields_and_nulls_blanks()
    {
        var request = SupplierEditPresenter.BuildRequest("  ", "Vinamilk", " Chị Lan ", "", "  ", null, " 0300588569 ");

        Assert.Equal(new SaveSupplierRequest(null, "Vinamilk", "Chị Lan", null, null, null, "0300588569"), request);
    }

    [Fact]
    public async Task Suggests_the_next_code_only_when_adding()
    {
        var (host, add, _) = Create();
        using var _h = host;
        Assert.Equal("NCC0007", await add.SuggestCodeAsync());

        var (editHost, edit, _) = Create(Vinamilk);
        using var _e = editHost;
        Assert.Null(await edit.SuggestCodeAsync());
        Assert.Empty(editHost.Suppliers.Calls);
    }

    [Fact]
    public async Task Failed_or_late_code_suggestion_returns_null()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Suppliers.FailWith = Error.Forbidden();
        Assert.Null(await presenter.SuggestCodeAsync());

        host.Suppliers.FailWith = null;
        view.IsAlive = false;
        Assert.Null(await presenter.SuggestCodeAsync());
    }

    [Fact]
    public async Task Invalid_input_focuses_the_first_bad_field_without_saving()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.SupplierName = "Vinamilk";
        view.TaxCode = "12345";

        var error = await presenter.SaveAsync();

        Assert.StartsWith("Mã số thuế", error);
        Assert.Equal(new[] { nameof(SaveSupplierRequest.TaxCode) }, view.Focused);
        Assert.Empty(host.Suppliers.Calls);
    }

    [Fact]
    public async Task Blank_name_focuses_the_name()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        Assert.Equal("Tên nhà cung cấp không được để trống.", await presenter.SaveAsync());
        Assert.Equal(new[] { nameof(SaveSupplierRequest.Name) }, view.Focused);
    }

    [Fact]
    public async Task Add_creates_with_the_typed_values()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.Code = "NCC0007";
        view.SupplierName = "Vinamilk";
        view.Phone = "0901 234 567";

        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(new[] { "create:Vinamilk" }, host.Suppliers.Calls);
        Assert.Equal("NCC0007", host.Suppliers.LastRequest!.Code);
        Assert.Equal("0901 234 567", host.Suppliers.LastRequest.Phone);
    }

    [Fact]
    public async Task Edit_updates_the_supplier_and_reports_service_errors()
    {
        var (host, presenter, view) = Create(Vinamilk);
        using var _ = host;
        view.SupplierName = "Vinamilk Việt Nam";

        Assert.Null(await presenter.SaveAsync());
        Assert.Equal(new[] { "update:5:Vinamilk Việt Nam" }, host.Suppliers.Calls);

        host.Suppliers.FailWith = Error.Conflict("Mã số thuế đã được dùng.");
        Assert.Equal("Mã số thuế đã được dùng.", await presenter.SaveAsync());
    }
}
