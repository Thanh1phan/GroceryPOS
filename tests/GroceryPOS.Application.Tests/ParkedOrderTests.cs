using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Tests;

internal sealed class InMemoryParkedOrderStore : IParkedOrderStore
{
    public List<ParkedOrder> Orders { get; } = new();

    public Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ParkedOrder>>(Orders.OrderBy(o => o.ParkedAt).ToList());

    public Task<ParkedOrder?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));

    public Task SaveAsync(ParkedOrder order, CancellationToken cancellationToken = default)
    {
        Orders.RemoveAll(o => o.Id == order.Id);
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Orders.RemoveAll(o => o.Id == id) > 0);
}

public class ParkedOrderServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryParkedOrderStore _store = new();
    private readonly UserSessionStore _session = new();
    private readonly FakeClock _clock = new();
    private readonly Product _milk;
    private readonly Product _noodles;
    private readonly Customer _member;
    private readonly ParkedOrderService _sut;

    public ParkedOrderServiceTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", 28_000, 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _products.Add(_milk);
        _noodles = new Product("MI01", "Mì Hảo Hảo", 1, "gói", 3_500, 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);
        _member = new Customer("KH000001", "Nguyễn Văn A", "0901234567");
        _member.RecordPurchase(500_000, LoyaltyPolicy.Default);
        _customers.Add(_member);

        SignIn(Permissions.SalesCreate, Permissions.SalesDiscount);
        _sut = new ParkedOrderService(_store, _products, _customers, _session, _clock);
    }

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    private PosCart FilledCart()
    {
        var cart = new PosCart();
        var milk = cart.Add(PosLookupService.ToDto(_milk), 2);
        cart.SetLineDiscount(milk, DiscountInput.Amount(5_000));
        cart.Add(PosLookupService.ToDto(_noodles), 5);
        cart.AttachCustomer(PosCustomer.From(_member));
        cart.RedeemPoints(10);
        cart.SetInvoiceDiscount(DiscountInput.Percent(10));
        cart.SetCashTendered(200_000);
        cart.SetNote("Khách quay lại lấy thêm trứng");
        return cart;
    }

    [Fact]
    public async Task Park_saves_the_cart_and_clears_it()
    {
        var cart = FilledCart();

        var result = await _sut.ParkAsync(cart);

        Assert.True(result.IsSuccess);
        Assert.True(cart.IsEmpty);
        Assert.Null(cart.Customer);
        var order = Assert.Single(_store.Orders);
        Assert.Equal("Nguyễn Văn A", order.Label); // member name when nothing typed
        Assert.Equal(2, order.LineCount);
        Assert.Equal(7m, order.TotalQuantity);
        Assert.Equal(_member.Id, order.CustomerId);
        Assert.Equal("Trần Thị B", order.ParkedByName);
        Assert.Equal(_clock.Now, order.ParkedAt);
        // (70.000 − 5.000 + 25.000) − 10% = 81.000; points are not part of a parked order.
        Assert.Equal(81_000m, order.Total);
        Assert.Equal("Sữa tươi Vinamilk 1L, Mì Hảo Hảo", order.Preview);
    }

    [Fact]
    public async Task Park_refuses_empty_cart_full_list_and_missing_permission()
    {
        Assert.Equal("validation", (await _sut.ParkAsync(new PosCart())).Error.Code);

        for (var i = 0; i < ParkedOrderRules.MaxParked; i++)
        {
            var cart = new PosCart();
            cart.Add(PosLookupService.ToDto(_noodles));
            Assert.True((await _sut.ParkAsync(cart)).IsSuccess);
        }

        var extra = new PosCart();
        extra.Add(PosLookupService.ToDto(_noodles));
        Assert.Equal("conflict", (await _sut.ParkAsync(extra)).Error.Code);
        Assert.False(extra.IsEmpty);
        Assert.Equal("Đơn tạm 20", _store.Orders[^1].Label);

        SignIn(Permissions.ProductsView);
        Assert.Equal("forbidden", (await _sut.ParkAsync(extra)).Error.Code);
        _session.SignOut();
        Assert.False((await _sut.ParkAsync(extra)).IsSuccess);
    }

    [Fact]
    public async Task Resume_restores_lines_discounts_member_and_note_then_removes_the_order()
    {
        var parked = (await _sut.ParkAsync(FilledCart())).Value;
        var cart = new PosCart();

        var result = await _sut.ResumeAsync(parked.Id, cart);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasNotices);
        Assert.Equal(2, result.Value.RestoredLines);
        Assert.Empty(_store.Orders);
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(DiscountInput.Amount(5_000), cart.Lines[0].Discount);
        Assert.Equal(DiscountInput.Percent(10), cart.InvoiceDiscount);
        Assert.Equal(_member.Id, cart.Customer!.Id);
        Assert.Equal(0, cart.PointsToRedeem);
        Assert.Null(cart.CashTendered);
        Assert.Equal("Khách quay lại lấy thêm trứng", cart.Note);
        Assert.Equal(parked.Total, cart.Total);
    }

    [Fact]
    public async Task Resume_uses_current_prices_and_drops_discontinued_products_with_notices()
    {
        var parked = (await _sut.ParkAsync(FilledCart())).Value;
        _milk.ChangePrices(28_000, 36_000);
        _noodles.Deactivate();
        _member.Deactivate();
        var cart = new PosCart();

        var result = await _sut.ResumeAsync(parked.Id, cart);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Notices.Count);
        Assert.Contains(result.Value.Notices, n => n.Contains("35.000") && n.Contains("36.000"));
        Assert.Contains(result.Value.Notices, n => n.Contains("Mì Hảo Hảo"));
        Assert.Contains(result.Value.Notices, n => n.Contains("Nguyễn Văn A"));
        var line = Assert.Single(cart.Lines);
        Assert.Equal(36_000m, line.UnitPrice);
        Assert.Null(cart.Customer);
    }

    [Fact]
    public async Task Resume_with_nothing_left_to_sell_fails_and_deletes_the_order()
    {
        var cart = new PosCart();
        cart.Add(PosLookupService.ToDto(_noodles));
        var parked = (await _sut.ParkAsync(cart)).Value;
        _noodles.Deactivate();

        var result = await _sut.ResumeAsync(parked.Id, cart);

        Assert.False(result.IsSuccess);
        Assert.Empty(_store.Orders);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public async Task Resume_requires_an_empty_cart_and_an_existing_order()
    {
        var parked = (await _sut.ParkAsync(FilledCart())).Value;
        var busy = new PosCart();
        busy.Add(PosLookupService.ToDto(_noodles));

        Assert.Equal("conflict", (await _sut.ResumeAsync(parked.Id, busy)).Error.Code);
        Assert.Single(_store.Orders);
        Assert.Equal("not_found", (await _sut.ResumeAsync(Guid.NewGuid(), new PosCart())).Error.Code);
    }

    [Fact]
    public async Task Cashier_without_discount_permission_resumes_without_discounts()
    {
        var parked = (await _sut.ParkAsync(FilledCart())).Value;
        SignIn(Permissions.SalesCreate);
        var cart = new PosCart();

        var result = await _sut.ResumeAsync(parked.Id, cart);

        Assert.True(result.IsSuccess);
        Assert.False(cart.HasDiscounts);
        Assert.Contains(result.Value.Notices, n => n.Contains("giảm giá"));
        Assert.Equal(95_000m, cart.Total);
    }

    [Fact]
    public async Task Discard_removes_the_order_once()
    {
        var parked = (await _sut.ParkAsync(FilledCart())).Value;

        Assert.True((await _sut.DiscardAsync(parked.Id)).IsSuccess);
        Assert.Equal("not_found", (await _sut.DiscardAsync(parked.Id)).Error.Code);
    }
}

public class ParkedOrderRulesTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    [Fact]
    public void Label_prefers_typed_text_then_member_then_next_free_number()
    {
        Assert.Equal("Chị Lan áo xanh", ParkedOrderRules.LabelFor("  Chị Lan áo xanh ", "Nguyễn Văn A", []));
        Assert.Equal("Nguyễn Văn A", ParkedOrderRules.LabelFor(" ", "Nguyễn Văn A", []));
        Assert.Equal("Đơn tạm 1", ParkedOrderRules.LabelFor(null, null, []));
        Assert.Equal("Đơn tạm 2", ParkedOrderRules.LabelFor(null, null, ["Đơn tạm 1", "Đơn tạm 3", "Anh Tư"]));
        Assert.Equal(ParkedOrderRules.MaxLabelLength, ParkedOrderRules.LabelFor(new string('x', 100), null, []).Length);
    }

    [Fact]
    public void Preview_lists_two_names_and_counts_the_rest()
    {
        Assert.Equal(string.Empty, ParkedOrderRules.Preview([]));
        Assert.Equal("Sữa", ParkedOrderRules.Preview(["Sữa"]));
        Assert.Equal("Sữa, Mì +2", ParkedOrderRules.Preview(["Sữa", "Mì", "Trứng", "Gạo"]));
    }

    [Fact]
    public void Age_reads_naturally()
    {
        var now = new DateTimeOffset(2026, 11, 4, 3, 0, 0, TimeSpan.Zero); // 10:00 in Vietnam

        Assert.Equal("vừa xong", ParkedOrderRules.Age(now.AddSeconds(-20), now, Vn));
        Assert.Equal("5 phút trước", ParkedOrderRules.Age(now.AddMinutes(-5), now, Vn));
        Assert.Equal("2 giờ trước", ParkedOrderRules.Age(now.AddHours(-2.5), now, Vn));
        Assert.Equal("hôm qua 18:20", ParkedOrderRules.Age(new DateTimeOffset(2026, 11, 3, 11, 20, 0, TimeSpan.Zero), now, Vn));
        Assert.Equal("01/11 09:15", ParkedOrderRules.Age(new DateTimeOffset(2026, 11, 1, 2, 15, 0, TimeSpan.Zero), now, Vn));
        Assert.True(ParkedOrderRules.IsStale(now.AddHours(-12), now));
        Assert.False(ParkedOrderRules.IsStale(now.AddHours(-11), now));
    }
}

public class CustomerDisplayStateTests
{
    private static readonly PosProductDto Milk = new(1, "SUA01", null, "Sữa tươi Vinamilk 1L", "hộp", 35_000, 5, 20, false);
    private static readonly PosProductDto Rice = new(2, "GAO01", null, "Gạo ST25", "kg", 32_000, 5, 50, false);

    [Fact]
    public void Empty_cart_shows_the_welcome_screen()
    {
        var state = CustomerDisplayState.FromCart(new PosCart(), "Tạp hóa Cô Ba");

        Assert.Equal(CustomerDisplayMode.Welcome, state.Mode);
        Assert.Equal("Tạp hóa Cô Ba", state.Headline);
        Assert.Empty(state.Lines);
    }

    [Fact]
    public void Shopping_shows_last_item_total_and_savings()
    {
        var cart = new PosCart();
        var milk = cart.Add(Milk, 2);
        cart.Add(Rice, 1.5m);
        cart.SetLineDiscount(milk, DiscountInput.Amount(10_000));

        var state = CustomerDisplayState.FromCart(cart, "Tạp hóa", milk);

        Assert.Equal(CustomerDisplayMode.Shopping, state.Mode);
        Assert.Equal("Sữa tươi Vinamilk 1L", state.LastItemName);
        Assert.Equal("2 hộp × 35.000 ₫ = 60.000 ₫", state.LastItemDetail);
        Assert.Equal(108_000m, state.Amount);
        Assert.Equal("2 mặt hàng · SL 3,5", state.ItemsText);
        Assert.Equal("Tiết kiệm 10.000 ₫", state.DiscountText);
        Assert.Equal("1,5 × 32.000 ₫", state.Lines[1].Detail);
    }

    [Fact]
    public void Only_the_newest_lines_are_listed()
    {
        var cart = new PosCart();
        for (var i = 1; i <= 10; i++)
        {
            cart.Add(Milk with { Id = i, Name = $"SP {i}" });
        }

        var state = CustomerDisplayState.FromCart(cart, "Tạp hóa", maxLines: 3);

        Assert.Equal(["SP 8", "SP 9", "SP 10"], state.Lines.Select(l => l.Name));
        Assert.Equal("SP 10", state.LastItemName);
    }

    [Fact]
    public void Thank_you_shows_change_or_amount_paid()
    {
        var withChange = CustomerDisplayState.ThankYou("Tạp hóa", 87_000, 100_000, 13_000, 8);
        Assert.Equal(CustomerDisplayMode.ThankYou, withChange.Mode);
        Assert.Equal(13_000m, withChange.Amount);
        Assert.Equal("Khách đưa 100.000 ₫ · Tiền thừa 13.000 ₫ · +8 điểm", withChange.FooterText);

        var exact = CustomerDisplayState.ThankYou("Tạp hóa", 87_000, 87_000, 0);
        Assert.Equal("Đã thanh toán", exact.AmountCaption);
        Assert.Equal(87_000m, exact.Amount);
    }
}

public class PosCartLoadTests
{
    private static readonly PosProductDto Milk = new(1, "SUA01", null, "Sữa tươi Vinamilk 1L", "hộp", 35_000, 5, 20, false);

    [Fact]
    public void Load_replaces_the_cart_and_keeps_split_lines()
    {
        var cart = new PosCart();
        cart.Add(Milk with { Id = 99 });
        var changes = 0;
        cart.Changed += (_, _) => changes++;

        cart.Load([(Milk, 1m, DiscountInput.Percent(10)), (Milk, 2m, null)], note: "  ");

        Assert.Equal(1, changes);
        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(101_500m, cart.Total);
        Assert.Null(cart.Note);
    }

    [Fact]
    public void Load_validates_quantities_and_discounts()
    {
        var cart = new PosCart();

        Assert.ThrowsAny<Exception>(() => cart.Load([(Milk, 0m, null)]));
        Assert.ThrowsAny<Exception>(() => cart.Load([(Milk, 1m, DiscountInput.Percent(150))]));
        Assert.True(cart.IsEmpty);
    }
}
