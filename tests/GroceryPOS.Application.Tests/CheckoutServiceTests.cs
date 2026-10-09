using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class CheckoutServiceTests
{
    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 02:00 UTC = 09:00 in Vietnam
    private readonly Product _milk;
    private readonly Product _noodles;
    private readonly Customer _member;

    public CheckoutServiceTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _milk.SetMinStockLevel(5);
        _products.Add(_milk);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        _member = new Customer("KH001", "Nguyễn Văn A", "0901234567");
        _member.RecordPurchase(500_000, LoyaltyPolicy.Default); // 50 points
        _customers.Add(_member);

        SignIn(Permissions.SalesCreate);
    }

    private CheckoutService CreateSut(SalesOptions? options = null) =>
        new(_sales, _products, _customers, new InvoiceNumberGenerator(_sales, options), _uow, _session, _audit, _clock, options);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    private static CheckoutRequest Cash(decimal paid, params CheckoutLine[] lines) =>
        new(lines, new[] { new PaymentInput(PaymentMethod.Cash, paid) });

    [Fact]
    public async Task Cash_checkout_completes_sale_deducts_stock_and_audits()
    {
        var result = await CreateSut().CheckoutAsync(Cash(100_000, new CheckoutLine(_milk.Id, 2), new CheckoutLine(_noodles.Id, 5)));

        Assert.True(result.IsSuccess);
        var receipt = result.Value;
        Assert.Equal("HD261008-0001", receipt.Code);
        Assert.Equal(95_000m, receipt.Total);
        Assert.Equal(5_000m, receipt.Change);
        Assert.Equal(18m, _milk.StockQuantity);
        Assert.Equal(95m, _noodles.StockQuantity);

        var sale = Assert.Single(_sales.All);
        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(7, sale.CashierId);
        Assert.Equal(sale.Id, receipt.SaleId);
        Assert.Contains(AuditActions.SaleCompleted, _audit.Actions);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Invoice_numbers_are_sequential_per_store_day()
    {
        var sut = CreateSut();
        var first = await sut.CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));
        var second = await sut.CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));

        // 18:00 UTC on 08/10 is already 01:00 on 09/10 in Vietnam → new day, sequence restarts.
        _clock.Now = new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);
        var nextDay = await sut.CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));

        Assert.Equal("HD261008-0001", first.Value.Code);
        Assert.Equal("HD261008-0002", second.Value.Code);
        Assert.Equal("HD261009-0001", nextDay.Value.Code);
    }

    [Fact]
    public async Task Invoice_generator_continues_after_existing_codes_and_honours_prefix()
    {
        _sales.Add(new Sale("BL261008-0041", 1, _clock.Now));
        var generator = new InvoiceNumberGenerator(_sales, SalesOptions.Default with { InvoicePrefix = "bl" });

        Assert.Equal("BL261008-0042", await generator.NextAsync(_clock.Now));
    }

    [Fact]
    public async Task Requires_signed_in_user_with_sales_permission()
    {
        _session.SignOut();
        var anonymous = await CreateSut().CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));

        SignIn(Permissions.InventoryView);
        var stockKeeper = await CreateSut().CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));

        Assert.Equal("auth.required", anonymous.Error.Code);
        Assert.Equal("forbidden", stockKeeper.Error.Code);
        Assert.Empty(_sales.All);
    }

    [Fact]
    public async Task Discounts_need_discount_permission()
    {
        var request = Cash(100_000, new CheckoutLine(_milk.Id, 1, DiscountInput.Percent(10)));

        var denied = await CreateSut().CheckoutAsync(request);
        SignIn(Permissions.SalesCreate, Permissions.SalesDiscount);
        var allowed = await CreateSut().CheckoutAsync(request);

        Assert.Equal(SaleErrors.DiscountNotAllowed, denied.Error);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(31_500m, allowed.Value.Total);
        Assert.Equal(3_500m, allowed.Value.TotalDiscount);
    }

    [Fact]
    public async Task Line_and_invoice_discounts_combine()
    {
        SignIn(Permissions.SalesCreate, Permissions.SalesDiscount);
        var request = new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 2, DiscountInput.Amount(5_000)), new CheckoutLine(_noodles.Id, 10) },
            new[] { new PaymentInput(PaymentMethod.BankTransfer, 103_500, "FT2610080001") },
            InvoiceDiscount: DiscountInput.Percent(10));

        var result = await CreateSut().CheckoutAsync(request);

        // (70.000 − 5.000) + 50.000 = 115.000 → −10% = 103.500
        Assert.True(result.IsSuccess);
        Assert.Equal(103_500m, result.Value.Total);
        Assert.Equal(0m, result.Value.Change);
    }

    [Fact]
    public async Task Rejects_empty_cart_missing_payment_and_unknown_product()
    {
        var sut = CreateSut();

        var empty = await sut.CheckoutAsync(Cash(10_000));
        var unpaid = await sut.CheckoutAsync(new CheckoutRequest(new[] { new CheckoutLine(_milk.Id, 1) }, Array.Empty<PaymentInput>()));
        var unknown = await sut.CheckoutAsync(Cash(10_000, new CheckoutLine(999, 1)));

        Assert.Equal(SaleErrors.EmptyCart, empty.Error);
        Assert.Equal(SaleErrors.NoPayment, unpaid.Error);
        Assert.Equal("not_found", unknown.Error.Code);
    }

    [Fact]
    public async Task Failed_checkout_changes_nothing()
    {
        var result = await CreateSut().CheckoutAsync(Cash(30_000,
            new CheckoutLine(_milk.Id, 1), new CheckoutLine(_noodles.Id, 2)));

        Assert.Equal("sale.unpaid", result.Error.Code);
        Assert.Contains("15.000", result.Error.Message.Replace(",", "."));
        Assert.Equal(20m, _milk.StockQuantity);
        Assert.Equal(100m, _noodles.StockQuantity);
        Assert.Empty(_sales.All);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Domain_rule_violations_become_failures()
    {
        _noodles.Deactivate();

        var result = await CreateSut().CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)));

        Assert.True(result.IsFailure);
        Assert.Equal("sale.product", result.Error.Code);
    }

    [Fact]
    public async Task Insufficient_stock_is_rejected_when_negative_stock_is_disabled()
    {
        var strict = SalesOptions.Default with { AllowNegativeStock = false };

        // Two lines of the same product are checked against stock together.
        var result = await CreateSut(strict).CheckoutAsync(Cash(1_000_000,
            new CheckoutLine(_milk.Id, 15), new CheckoutLine(_noodles.Id, 1), new CheckoutLine(_milk.Id, 6)));

        Assert.Equal("product.stock", result.Error.Code);
        Assert.Contains("Sữa tươi", result.Error.Message);
        Assert.Equal(20m, _milk.StockQuantity);
    }

    [Fact]
    public async Task Negative_stock_allowed_by_default_and_reported_as_low_stock()
    {
        var result = await CreateSut().CheckoutAsync(Cash(1_000_000, new CheckoutLine(_milk.Id, 21)));

        Assert.True(result.IsSuccess);
        Assert.Equal(-1m, _milk.StockQuantity);
        var alert = Assert.Single(result.Value.LowStockAlerts);
        Assert.Equal(_milk.Id, alert.ProductId);
        Assert.Equal(-1m, alert.StockQuantity);
    }

    [Fact]
    public async Task Low_stock_alert_only_for_products_at_or_below_minimum()
    {
        var result = await CreateSut().CheckoutAsync(Cash(1_000_000, new CheckoutLine(_milk.Id, 14), new CheckoutLine(_noodles.Id, 1)));

        Assert.Empty(result.Value.LowStockAlerts); // milk: 6 > 5
        var next = await CreateSut().CheckoutAsync(Cash(100_000, new CheckoutLine(_milk.Id, 1)));
        Assert.Equal("SUA01", Assert.Single(next.Value.LowStockAlerts).ProductCode);
    }

    [Fact]
    public async Task Member_redeems_and_earns_points()
    {
        // 50 points × 100 ₫ = 5.000 ₫ off; 3 × 35.000 = 105.000 → pays 100.000 → earns 10 points.
        var request = new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 3) },
            new[] { new PaymentInput(PaymentMethod.Cash, 100_000) },
            CustomerId: _member.Id,
            PointsToRedeem: 50);

        var result = await CreateSut().CheckoutAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(100_000m, result.Value.Total);
        Assert.Equal(50, result.Value.PointsRedeemed);
        Assert.Equal(10, result.Value.PointsEarned);
        Assert.Equal(10, result.Value.CustomerPointsBalance);
        Assert.Equal(10, _member.LoyaltyPoints);
        Assert.Equal(600_000m, _member.TotalSpent);
    }

    [Fact]
    public async Task Cannot_redeem_more_points_than_the_member_has_or_without_member()
    {
        var tooMany = await CreateSut().CheckoutAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 3) },
            new[] { new PaymentInput(PaymentMethod.Cash, 200_000) },
            CustomerId: _member.Id,
            PointsToRedeem: 51));
        var anonymous = await CreateSut().CheckoutAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 3) },
            new[] { new PaymentInput(PaymentMethod.Cash, 200_000) },
            PointsToRedeem: 10));
        var unknownCustomer = await CreateSut().CheckoutAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 1) },
            new[] { new PaymentInput(PaymentMethod.Cash, 200_000) },
            CustomerId: 404));

        Assert.Equal("sale.points", tooMany.Error.Code);
        Assert.Equal("sale.points", anonymous.Error.Code);
        Assert.Equal(SaleErrors.CustomerNotFound, unknownCustomer.Error);
        Assert.Equal(50, _member.LoyaltyPoints);
    }

    [Fact]
    public async Task Void_returns_stock_and_reverses_loyalty()
    {
        SignIn(Permissions.SalesCreate, Permissions.SalesVoid);
        var sut = CreateSut();
        var receipt = (await sut.CheckoutAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 3), new CheckoutLine(_noodles.Id, 4) },
            new[] { new PaymentInput(PaymentMethod.Cash, 200_000) },
            CustomerId: _member.Id,
            PointsToRedeem: 20))).Value;

        var result = await sut.VoidAsync(receipt.SaleId, "Khách đổi ý");

        Assert.True(result.IsSuccess);
        Assert.Equal(20m, _milk.StockQuantity);
        Assert.Equal(100m, _noodles.StockQuantity);
        Assert.Equal(50, _member.LoyaltyPoints);
        Assert.Equal(500_000m, _member.TotalSpent);
        Assert.Equal(SaleStatus.Voided, _sales.All[0].Status);
        Assert.Contains(AuditActions.SaleVoided, _audit.Actions);
    }

    [Fact]
    public async Task Void_requires_permission_reason_and_completed_sale()
    {
        var receipt = (await CreateSut().CheckoutAsync(Cash(5_000, new CheckoutLine(_noodles.Id, 1)))).Value;

        var forbidden = await CreateSut().VoidAsync(receipt.SaleId, "Nhầm hàng");
        SignIn(Permissions.SalesVoid);
        var sut = CreateSut();
        var noReason = await sut.VoidAsync(receipt.SaleId, "  ");
        var missing = await sut.VoidAsync(999, "Nhầm hàng");
        var ok = await sut.VoidAsync(receipt.SaleId, "Nhầm hàng");
        var twice = await sut.VoidAsync(receipt.SaleId, "Nhầm hàng");

        Assert.Equal("forbidden", forbidden.Error.Code);
        Assert.Equal("sale.void_reason", noReason.Error.Code);
        Assert.Equal(SaleErrors.SaleNotFound, missing.Error);
        Assert.True(ok.IsSuccess);
        Assert.Equal("sale.void", twice.Error.Code);
        Assert.Equal(100m, _noodles.StockQuantity);
    }
}
