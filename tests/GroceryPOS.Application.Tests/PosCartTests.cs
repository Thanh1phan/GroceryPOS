using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class PosCartTests
{
    private static readonly PosProductDto Milk = new(1, "SUA01", "8934673000011", "Sữa tươi Vinamilk 1L", "hộp", 35_000, 5, 20, false);
    private static readonly PosProductDto Noodles = new(2, "MI01", null, "Mì Hảo Hảo", "gói", 5_000, 10, 3, false);
    private static readonly PosCustomer Member = new(9, "KH000009", "Nguyễn Văn A", "0901234567", 50);

    [Fact]
    public void Scanning_the_same_product_again_increases_its_line()
    {
        var cart = new PosCart();
        cart.Add(Milk);
        cart.Add(Noodles, 2);
        var line = cart.Add(Milk);

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(80_000m, cart.Total);
        Assert.Equal(4m, cart.TotalQuantity);
    }

    [Fact]
    public void Discounted_line_is_not_merged_into()
    {
        var cart = new PosCart();
        var first = cart.Add(Milk);
        cart.SetLineDiscount(first, DiscountInput.Percent(10));
        var second = cart.Add(Milk);

        Assert.NotSame(first, second);
        Assert.Equal(3_500m, first.DiscountAmount);
        Assert.Equal("10%", first.DiscountText);
        Assert.Equal(66_500m, cart.Total);
    }

    [Fact]
    public void Decrement_below_one_removes_the_line()
    {
        var cart = new PosCart();
        var line = cart.Add(Noodles);
        cart.Increment(line);
        Assert.Equal(2m, line.Quantity);

        cart.Increment(line, -1);
        cart.Increment(line, -1);

        Assert.True(cart.IsEmpty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void Invalid_quantity_is_rejected(decimal quantity)
    {
        var cart = new PosCart();
        var line = cart.Add(Noodles);
        Assert.Throws<DomainException>(() => cart.SetQuantity(line, quantity));
    }

    [Fact]
    public void Weighed_quantity_is_rounded_to_grams()
    {
        var cart = new PosCart();
        var line = cart.Add(Milk, 1.23456m);
        Assert.Equal(1.235m, line.Quantity);
        Assert.Equal(43_225m, line.GrossAmount);
    }

    [Fact]
    public void Invoice_discount_applies_after_line_discounts()
    {
        var cart = new PosCart();
        cart.Add(Milk, 2);                               // 70.000
        var noodles = cart.Add(Noodles, 6);             // 30.000
        cart.SetLineDiscount(noodles, DiscountInput.Amount(5_000));
        cart.SetInvoiceDiscount(DiscountInput.Percent(10));

        Assert.Equal(95_000m, cart.Subtotal);
        Assert.Equal(9_500m, cart.InvoiceDiscountAmount);
        Assert.Equal(85_500m, cart.Total);
        Assert.Equal(14_500m, cart.TotalDiscount);
    }

    [Fact]
    public void Invalid_discount_uses_domain_rules()
    {
        var cart = new PosCart();
        Assert.Throws<DomainException>(() => cart.SetInvoiceDiscount(DiscountInput.Percent(120)));
        Assert.Throws<DomainException>(() => cart.SetInvoiceDiscount(DiscountInput.Amount(-1)));

        cart.SetInvoiceDiscount(DiscountInput.Percent(0));
        Assert.Null(cart.InvoiceDiscount);
    }

    [Fact]
    public void Points_redemption_is_capped_and_shrinks_with_the_cart()
    {
        var cart = new PosCart();
        var milk = cart.Add(Milk);
        cart.AttachCustomer(Member);

        Assert.Equal(50, cart.MaxRedeemablePoints);     // 50 points = 5.000 ₫ < 35.000 ₫
        cart.RedeemPoints(50);
        Assert.Equal(5_000m, cart.PointsDiscountAmount);
        Assert.Equal(30_000m, cart.Total);
        Assert.Throws<DomainException>(() => cart.RedeemPoints(51));

        // A smaller basket can no longer absorb all points: redemption is trimmed automatically.
        cart.Add(Noodles, 0.2m);                        // 1.000 ₫
        cart.Remove(milk);
        Assert.Equal(10, cart.PointsToRedeem);
        Assert.Equal(0m, cart.Total);
        Assert.Equal("Tổng tiền phải lớn hơn 0 ₫.", cart.CheckoutBlocker);
    }

    [Fact]
    public void Points_need_a_member_and_are_dropped_when_member_changes()
    {
        var cart = new PosCart();
        cart.Add(Milk);
        Assert.Throws<DomainException>(() => cart.RedeemPoints(10));

        cart.AttachCustomer(Member);
        cart.RedeemPoints(10);
        cart.AttachCustomer(Member with { Id = 10, LoyaltyPoints = 500 });
        Assert.Equal(0, cart.PointsToRedeem);

        cart.RedeemPoints(20);
        cart.DetachCustomer();
        Assert.Equal(0, cart.PointsToRedeem);
        Assert.Equal(0, cart.PointsToEarn);
    }

    [Fact]
    public void Cash_change_and_amount_due()
    {
        var cart = new PosCart();
        cart.Add(Milk, 2);
        cart.Add(Noodles, 3);                           // 85.000

        Assert.Equal(0m, cart.Change);                  // exact amount by default
        Assert.True(cart.CanCheckout);

        cart.SetCashTendered(100_000);
        Assert.Equal(15_000m, cart.Change);

        cart.SetCashTendered(50_000);
        Assert.Equal(35_000m, cart.AmountDue);
        Assert.Equal("Khách còn thiếu 35.000 ₫.", cart.CheckoutBlocker);
        Assert.True(cart.BuildRequest().IsFailure);
    }

    [Fact]
    public void Transfer_pays_exactly_the_total_and_gives_no_change()
    {
        var cart = new PosCart();
        cart.Add(Milk);
        cart.SetCashTendered(500_000);
        cart.SetPaymentMethod(PaymentMethod.BankTransfer);
        cart.SetPaymentReference("  FT26297123  ");

        Assert.Equal(0m, cart.Change);
        var request = cart.BuildRequest().Value;
        var payment = Assert.Single(request.Payments);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(35_000m, payment.Amount);
        Assert.Equal("FT26297123", payment.Reference);
    }

    [Fact]
    public void Stock_warning_counts_all_lines_of_a_product()
    {
        var cart = new PosCart();
        var a = cart.Add(Noodles, 2);
        cart.SetLineDiscount(a, DiscountInput.Amount(1_000));
        var b = cart.Add(Noodles, 2);                   // 4 > 3 in stock

        Assert.True(a.ExceedsStock);
        Assert.True(b.ExceedsStock);
        cart.Remove(b);
        Assert.False(a.ExceedsStock);
    }

    [Theory]
    [InlineData(87_000, new[] { 87_000, 90_000, 100_000, 200_000, 500_000 })]
    [InlineData(100_000, new[] { 100_000, 200_000, 500_000, 1_000_000 })]
    [InlineData(12_500, new[] { 12_500, 20_000, 50_000, 100_000, 200_000 })]
    public void Quick_cash_suggestions(int total, int[] expected)
    {
        Assert.Equal(expected.Select(e => (decimal)e), PosCart.SuggestCashAmounts(total));
    }

    [Fact]
    public void Clear_starts_a_new_sale_and_raises_changed()
    {
        var cart = new PosCart();
        var changes = 0;
        cart.Changed += (_, _) => changes++;
        cart.Add(Milk);
        cart.AttachCustomer(Member);
        cart.SetPaymentMethod(PaymentMethod.Card);
        cart.Clear();

        Assert.True(cart.IsEmpty);
        Assert.Null(cart.Customer);
        Assert.Equal(PaymentMethod.Cash, cart.PaymentMethod);
        Assert.Equal(4, changes);
    }

    [Theory]
    [InlineData("8934673000011", 1, "8934673000011")]
    [InlineData(" 3*8934673000011 ", 3, "8934673000011")]
    [InlineData("1,5*GAO01", 1.5, "GAO01")]
    [InlineData("0.25 * GAO01", 0.25, "GAO01")]
    [InlineData("*GAO01", 1, "*GAO01")]
    [InlineData("abc*GAO01", 1, "abc*GAO01")]
    [InlineData("0*GAO01", 1, "0*GAO01")]
    [InlineData("3*", 1, "3*")]
    public void Scan_quantity_prefix(string input, double quantity, string text)
    {
        var (q, t) = PosCart.ParseScan(input);
        Assert.Equal((decimal)quantity, q);
        Assert.Equal(text, t);
    }

    [Fact]
    public async Task Cart_totals_match_what_checkout_charges()
    {
        var products = new InMemoryProductRepository();
        var customers = new InMemoryCustomerRepository();
        var sales = new InMemorySaleRepository();
        var session = new UserSessionStore();
        var clock = new FakeClock();

        var milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", 28_000, 35_900, VatRate.Five);
        var rice = new Product("GAO01", "Gạo ST25", 1, "kg", 25_000, 33_333, VatRate.Five);
        products.Add(milk);
        products.Add(rice);
        var member = new Customer("KH001", "Nguyễn Văn A", "0901234567");
        member.RecordPurchase(1_230_000, LoyaltyPolicy.Default); // 123 points
        customers.Add(member);
        session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier,
            new HashSet<string> { Permissions.SalesCreate, Permissions.SalesDiscount }, clock.Now));

        var cart = new PosCart();
        var milkLine = cart.Add(PosLookupService.ToDto(milk), 3);
        cart.SetLineDiscount(milkLine, DiscountInput.Percent(7.5m));
        cart.Add(PosLookupService.ToDto(rice), 2.345m);
        cart.SetInvoiceDiscount(DiscountInput.Amount(3_333));
        cart.AttachCustomer(PosCustomer.From(member));
        cart.RedeemPoints(123);
        cart.SetCashTendered(200_000);

        var checkout = new CheckoutService(
            sales, products, new InMemoryStockMovementRepository(), customers,
            new InvoiceNumberGenerator(sales), new FakeUnitOfWork(), session, new RecordingAuditService(), clock);
        var receipt = (await checkout.CheckoutAsync(cart.BuildRequest().Value)).Value;

        Assert.Equal(cart.Total, receipt.Total);
        Assert.Equal(cart.Change, receipt.Change);
        Assert.Equal(cart.TotalDiscount, receipt.TotalDiscount);
        Assert.Equal(cart.PointsToEarn, receipt.PointsEarned);
    }
}
