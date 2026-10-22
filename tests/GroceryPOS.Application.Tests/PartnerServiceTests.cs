using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class SequentialCodeTests
{
    [Fact]
    public void Starts_at_one_and_pads() => Assert.Equal("KH000001", SequentialCode.Next("KH", 6, Array.Empty<string>()));

    [Fact]
    public void Continues_after_the_highest_numeric_code_and_ignores_hand_typed_ones()
    {
        var next = SequentialCode.Next("ncc", 4, new[] { "NCC0002", "NCC0010", "NCC-VINAMILK", "NCC", "KH0099", "ncc0007" });

        Assert.Equal("NCC0011", next);
    }

    [Fact]
    public void Grows_past_the_padding_width() => Assert.Equal("NCC10000", SequentialCode.Next("NCC", 4, new[] { "NCC9999" }));
}

public class SupplierServiceTests
{
    private readonly InMemorySupplierRepository _suppliers = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();

    public SupplierServiceTests() => SignIn(Permissions.SuppliersManage);

    private SupplierService CreateSut() => new(_suppliers, _uow, _session, _audit);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(1, "quanly", "Lê Văn C", SystemRoles.Manager, permissions.ToHashSet(), _clock.Now));

    [Fact]
    public async Task Create_assigns_next_code_normalizes_phone_audits_and_saves()
    {
        var sut = CreateSut();
        var first = await sut.CreateAsync(new SaveSupplierRequest(null, "Công ty Vinamilk", "Chị Hoa", "+84 901 234 567", TaxCode: "0300588569"));
        var second = await sut.CreateAsync(new SaveSupplierRequest("  ", "Đại lý Bia Sài Gòn"));

        Assert.True(first.IsSuccess);
        Assert.Equal("NCC0001", first.Value.Code);
        Assert.Equal("0901234567", first.Value.Phone);
        Assert.Equal("NCC0002", second.Value.Code);
        Assert.Equal(2, _audit.Actions.Count(a => a == AuditActions.SupplierCreated));
        Assert.Equal(2, _uow.SaveCount);
    }

    [Fact]
    public async Task Create_keeps_a_typed_code_upper_case_and_rejects_duplicates()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(new SaveSupplierRequest("vnm", "Vinamilk"));
        var duplicate = await sut.CreateAsync(new SaveSupplierRequest("VNM ", "Vinamilk 2"));
        var suggested = await sut.SuggestCodeAsync();

        Assert.Equal("VNM", created.Value.Code);
        Assert.Equal(PartnerErrors.DuplicateSupplierCode, duplicate.Error);
        Assert.Equal("NCC0001", suggested.Value);
        Assert.Single(_suppliers.All);
    }

    [Fact]
    public async Task Create_returns_domain_errors_instead_of_throwing()
    {
        var result = await CreateSut().CreateAsync(new SaveSupplierRequest(null, "Vinamilk", Phone: "12345"));

        Assert.True(result.IsFailure);
        Assert.Equal("phone.invalid", result.Error.Code);
        Assert.Empty(_suppliers.All);
    }

    [Fact]
    public async Task Update_changes_details_but_never_the_code_and_audits_only_real_changes()
    {
        var sut = CreateSut();
        var created = (await sut.CreateAsync(new SaveSupplierRequest(null, "Vinamilk"))).Value;

        var updated = await sut.UpdateAsync(created.Id, new SaveSupplierRequest("KHAC", "Vinamilk HCM", Address: "Q.7, TP.HCM"));
        var unchanged = await sut.UpdateAsync(created.Id, new SaveSupplierRequest(null, "Vinamilk HCM", Address: "Q.7, TP.HCM"));

        Assert.True(updated.IsSuccess);
        Assert.True(unchanged.IsSuccess);
        Assert.Equal("NCC0001", updated.Value.Code);
        Assert.Equal("Vinamilk HCM", updated.Value.Name);
        Assert.Single(_audit.Actions, AuditActions.SupplierUpdated);
    }

    [Fact]
    public async Task Supplier_with_debt_cannot_be_deactivated()
    {
        var supplier = new Supplier("NCC0001", "Vinamilk");
        supplier.AddDebt(1_500_000);
        _suppliers.Add(supplier);

        var result = await CreateSut().DeactivateAsync(supplier.Id);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Contains("1.500.000", result.Error.Message);
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public async Task Deactivate_and_activate_toggle_status_with_audit()
    {
        var sut = CreateSut();
        var created = (await sut.CreateAsync(new SaveSupplierRequest(null, "Vinamilk"))).Value;

        Assert.True((await sut.DeactivateAsync(created.Id)).IsSuccess);
        Assert.False(_suppliers.All.Single().IsActive);
        Assert.True((await sut.DeactivateAsync(created.Id)).IsSuccess); // no-op
        Assert.True((await sut.ActivateAsync(created.Id)).IsSuccess);

        Assert.True(_suppliers.All.Single().IsActive);
        Assert.Equal(2, _audit.Actions.Count(a => a == AuditActions.SupplierStatusChanged));
        Assert.Equal(PartnerErrors.SupplierNotFound(99), (await sut.ActivateAsync(99)).Error);
    }

    [Fact]
    public async Task List_is_sorted_by_vietnamese_name_and_open_to_purchasing_roles()
    {
        _suppliers.Add(new Supplier("B", "Đại lý Bình"));
        _suppliers.Add(new Supplier("A", "Công ty An"));
        _suppliers.Add(new Supplier("C", "Dầu ăn Tường An"));

        SignIn(Permissions.PurchasesManage);
        var list = await CreateSut().ListAsync();
        var create = await CreateSut().CreateAsync(new SaveSupplierRequest(null, "X"));

        Assert.Equal(new[] { "Công ty An", "Dầu ăn Tường An", "Đại lý Bình" }, list.Value.Select(s => s.Name));
        Assert.Equal("forbidden", create.Error.Code);

        SignIn(Permissions.SalesCreate);
        Assert.Equal("forbidden", (await CreateSut().ListAsync()).Error.Code);
    }
}

public class CustomerServiceTests
{
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemorySaleReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();

    public CustomerServiceTests() => SignIn(Permissions.CustomersView, Permissions.CustomersManage);

    private CustomerService CreateSut() => new(_customers, _sales, _returns, _uow, _session, _audit);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    [Fact]
    public async Task Create_assigns_code_normalizes_phone_and_audits()
    {
        var result = await CreateSut().CreateAsync(new SaveCustomerRequest("Nguyễn Văn A", "090.123.4567", "a@example.com"));
        var second = await CreateSut().CreateAsync(new SaveCustomerRequest("Trần Thị B"));

        Assert.True(result.IsSuccess);
        Assert.Equal("KH000001", result.Value.Code);
        Assert.Equal("0901234567", result.Value.Phone);
        Assert.Equal(0, result.Value.LoyaltyPoints);
        Assert.Equal("KH000002", second.Value.Code);
        Assert.Contains(AuditActions.CustomerCreated, _audit.Actions);
    }

    [Fact]
    public async Task Phone_must_be_unique_across_customers()
    {
        var sut = CreateSut();
        var a = (await sut.CreateAsync(new SaveCustomerRequest("A", "0901234567"))).Value;
        var b = (await sut.CreateAsync(new SaveCustomerRequest("B", "0912345678"))).Value;

        var duplicateCreate = await sut.CreateAsync(new SaveCustomerRequest("C", "+84901234567"));
        var duplicateUpdate = await sut.UpdateAsync(b.Id, new SaveCustomerRequest("B", "0901 234 567"));
        var keepOwnPhone = await sut.UpdateAsync(a.Id, new SaveCustomerRequest("A đổi tên", "0901234567"));

        Assert.Equal(PartnerErrors.DuplicateCustomerPhone, duplicateCreate.Error);
        Assert.Equal(PartnerErrors.DuplicateCustomerPhone, duplicateUpdate.Error);
        Assert.True(keepOwnPhone.IsSuccess);
        Assert.Equal("0912345678", _customers.All.Single(c => c.Id == b.Id).Phone);
    }

    [Fact]
    public async Task Update_with_invalid_phone_returns_error()
    {
        var created = (await CreateSut().CreateAsync(new SaveCustomerRequest("A"))).Value;

        var result = await CreateSut().UpdateAsync(created.Id, new SaveCustomerRequest("A", "abc"));

        Assert.Equal("phone.invalid", result.Error.Code);
    }

    [Fact]
    public async Task View_only_users_cannot_change_customers()
    {
        SignIn(Permissions.CustomersView);

        Assert.True((await CreateSut().ListAsync()).IsSuccess);
        Assert.Equal("forbidden", (await CreateSut().CreateAsync(new SaveCustomerRequest("A"))).Error.Code);
        Assert.Equal("forbidden", (await CreateSut().DeactivateAsync(1)).Error.Code);

        _session.SignOut();
        Assert.Equal(Authentication.AuthErrors.NotAuthenticated, (await CreateSut().ListAsync()).Error);
    }

    [Fact]
    public async Task Deactivate_keeps_points()
    {
        var customer = new Customer("KH000001", "A", "0901234567");
        customer.RecordPurchase(250_000, LoyaltyPolicy.Default);
        _customers.Add(customer);

        var result = await CreateSut().DeactivateAsync(customer.Id);

        Assert.True(result.IsSuccess);
        Assert.False(customer.IsActive);
        Assert.Equal(25, customer.LoyaltyPoints);
        Assert.Contains(AuditActions.CustomerStatusChanged, _audit.Actions);
    }

    [Fact]
    public async Task Loyalty_history_lists_purchases_returns_and_voids_with_running_balance()
    {
        var milk = new Product("SUA01", "Sữa tươi 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        milk.AdjustStock(50);
        _products.Add(milk);
        var noodles = new Product("MI01", "Mì gói", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        noodles.AdjustStock(100);
        _products.Add(noodles);
        var member = new Customer("KH000001", "Nguyễn Văn A", "0901234567");
        _customers.Add(member);

        SignIn(Permissions.SalesCreate, Permissions.SalesVoid, Permissions.ReturnsCreate, Permissions.CustomersView);
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var request = new CheckoutRequest(
            new[] { new CheckoutLine(milk.Id, 2), new CheckoutLine(noodles.Id, 10) },
            new[] { new PaymentInput(PaymentMethod.Cash, 120_000) },
            CustomerId: member.Id);

        var first = (await checkout.CheckoutAsync(request)).Value;           // +12
        _clock.Advance(TimeSpan.FromHours(1));
        var second = (await checkout.CheckoutAsync(request with { PointsToRedeem = 10 })).Value; // −10 then earns on 119.000
        _clock.Advance(TimeSpan.FromHours(1));
        var sale1 = _sales.All.Single(s => s.Id == first.SaleId);
        var returnResult = await new ReturnService(_sales, _returns, _products, _movements, _customers, _uow, _session, _audit, _clock)
            .CreateAsync(new CreateReturnRequest(sale1.Id, new[] { new ReturnLineInput(sale1.Lines.Single(l => l.ProductId == milk.Id).Id, 2) }, "Hết hạn"));
        Assert.True(returnResult.IsSuccess);
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.True((await checkout.VoidAsync(second.SaleId, "Nhập nhầm")).IsSuccess);

        var history = await CreateSut().GetLoyaltyHistoryAsync(member.Id);

        Assert.True(history.IsSuccess);
        var entries = history.Value.Entries;
        Assert.Equal(
            new[] { LoyaltyEntryKind.Void, LoyaltyEntryKind.Return, LoyaltyEntryKind.Purchase, LoyaltyEntryKind.Purchase },
            entries.Select(e => e.Kind));
        Assert.Equal(1, history.Value.InvoiceCount);
        Assert.Equal(member.LoyaltyPoints, entries[0].BalanceAfter);
        Assert.Equal(0, entries[^1].BalanceAfter - entries[^1].PointsChange); // started with no points
        for (var i = 0; i < entries.Count - 1; i++)
        {
            Assert.Equal(entries[i].BalanceAfter - entries[i].PointsChange, entries[i + 1].BalanceAfter);
        }

        Assert.Equal(12, entries[^1].PointsEarned);
        Assert.Equal(10, entries[^2].PointsRedeemed);
        Assert.Equal(second.Code, entries[0].DocumentCode);
        Assert.Equal(-second.Total, entries[0].Amount);
        Assert.Contains(first.Code, entries[1].Description);
        Assert.True(entries[1].PointsChange < 0);
        Assert.True(entries[0].PointsChange > 0); // void gives back the 10 redeemed points, the earned ones stop at zero
    }

    [Fact]
    public async Task Points_without_documents_show_as_an_opening_entry()
    {
        var customer = new Customer("KH000001", "A");
        customer.RecordPurchase(250_000, LoyaltyPolicy.Default);
        _customers.Add(customer);

        var history = await CreateSut().GetLoyaltyHistoryAsync(customer.Id);

        var entry = Assert.Single(history.Value.Entries);
        Assert.Equal(LoyaltyEntryKind.Opening, entry.Kind);
        Assert.Equal(25, entry.PointsChange);
        Assert.Equal(25, entry.BalanceAfter);
        Assert.Equal(0, history.Value.InvoiceCount);
    }

    [Fact]
    public async Task Loyalty_history_of_unknown_customer_is_not_found()
    {
        var result = await CreateSut().GetLoyaltyHistoryAsync(42);

        Assert.Equal(PartnerErrors.CustomerNotFound(42), result.Error);
    }
}

public class PartnerValidatorTests
{
    [Theory]
    [InlineData("0300588569", true)]
    [InlineData("0300588569-001", true)]
    [InlineData("0300588569001", true)]
    [InlineData("030058856", false)]
    [InlineData("03005885AB", false)]
    [InlineData(null, true)]
    public void Supplier_tax_code(string? taxCode, bool valid)
    {
        var result = new Validation.SaveSupplierRequestValidator().Validate(new SaveSupplierRequest(null, "Vinamilk", TaxCode: taxCode));

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("a@b.vn", true)]
    [InlineData("nguyen.van.a@gmail.com", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.vn", false)]
    [InlineData("@c.vn", false)]
    [InlineData("", true)]
    public void Customer_email(string email, bool valid)
    {
        var result = new Validation.SaveCustomerRequestValidator().Validate(new SaveCustomerRequest("A", Email: email));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Customer_requires_name_and_valid_phone()
    {
        var result = new Validation.SaveCustomerRequestValidator().Validate(new SaveCustomerRequest(" ", "12"));

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Số điện thoại không hợp lệ.");
    }
}
