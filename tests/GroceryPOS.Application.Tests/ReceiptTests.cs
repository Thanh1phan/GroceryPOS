using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class ReceiptServiceTests
{
    private static readonly StoreProfile Store = new()
    {
        Name = "Tạp hóa Cô Ba",
        Address = "12 Nguyễn Trãi, P. Bến Thành, Q.1, TP.HCM",
        Phone = "0909 123 456",
        TaxCode = "0312345678",
        ReceiptFooter = "Cảm ơn quý khách!\nHàng mua rồi được đổi trong 7 ngày",
    };

    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 02:00 UTC = 09:00 in Vietnam
    private readonly Product _milk;
    private readonly Product _noodles;
    private readonly Customer _member;
    private readonly int _cashierId;

    public ReceiptServiceTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _products.Add(_milk);
        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        _member = new Customer("KH000001", "Nguyễn Văn A", "0901234567");
        _member.RecordPurchase(500_000, LoyaltyPolicy.Default); // 50 points
        _customers.Add(_member);

        var cashier = new User("thungan", "Trần Thị B", "hash", new Role(SystemRoles.Cashier));
        _users.Add(cashier);
        _cashierId = cashier.Id;
        SignIn(Permissions.SalesCreate, Permissions.SalesDiscount);
    }

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(_cashierId, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    private ReceiptService CreateSut() => new(_sales, _users, _customers, _uow, _session, _audit, _clock, Store);

    private async Task<CheckoutReceipt> SellAsync(CheckoutRequest request)
    {
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var result = await checkout.CheckoutAsync(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    [Fact]
    public async Task Receipt_snapshots_items_discounts_payments_and_member()
    {
        var sold = await SellAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_milk.Id, 2, DiscountInput.Percent(10)), new CheckoutLine(_noodles.Id, 5) },
            new[] { new PaymentInput(PaymentMethod.Cash, 100_000) },
            CustomerId: _member.Id,
            InvoiceDiscount: DiscountInput.Amount(3_000)));

        var result = await CreateSut().GetForSaleAsync(sold.SaleId);

        Assert.True(result.IsSuccess);
        var receipt = result.Value;
        Assert.Equal(sold.Code, receipt.Code);
        Assert.Equal("Trần Thị B", receipt.CashierName);
        Assert.Equal(TimeSpan.FromHours(7), receipt.IssuedAt.Offset);
        Assert.Equal(9, receipt.IssuedAt.Hour);
        Assert.Equal(2, receipt.Items.Count);
        Assert.Equal("Giảm 10%", receipt.Items[0].DiscountLabel);
        Assert.Equal(7_000m, receipt.Items[0].DiscountAmount);
        Assert.Equal(63_000m, receipt.Items[0].Amount);
        Assert.Equal(95_000m, receipt.GrossAmount);
        Assert.Equal(85_000m, receipt.Total); // 95.000 − 7.000 − 3.000
        Assert.Equal(15_000m, receipt.Change);
        Assert.Equal(sold.Total, receipt.Total);
        Assert.Equal("Tiền mặt", Assert.Single(receipt.Payments).Label);
        Assert.Equal("0901***567", receipt.Member!.MaskedPhone);
        Assert.Equal(sold.PointsEarned, receipt.Member.PointsEarned);
        Assert.Equal(_member.LoyaltyPoints, receipt.Member.PointsBalance);
        Assert.False(receipt.IsReprint);
        Assert.DoesNotContain(AuditActions.ReceiptReprinted, _audit.Actions);
    }

    [Fact]
    public async Task Reprint_is_marked_audited_and_hides_current_point_balance()
    {
        var sold = await SellAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_noodles.Id, 2) },
            new[] { new PaymentInput(PaymentMethod.Cash, 10_000) },
            CustomerId: _member.Id));
        var savesBefore = _uow.SaveCount;
        _clock.Advance(TimeSpan.FromHours(3));

        var result = await CreateSut().ReprintAsync($"  {sold.Code.ToLowerInvariant()} ");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsReprint);
        Assert.Equal(12, result.Value.PrintedAt!.Value.Hour);
        Assert.Null(result.Value.Member!.PointsBalance);
        Assert.Contains(AuditActions.ReceiptReprinted, _audit.Actions);
        Assert.Equal(savesBefore + 1, _uow.SaveCount);
    }

    [Fact]
    public async Task Unknown_invoice_and_missing_permission_are_refused()
    {
        var sold = await SellAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_noodles.Id, 1) }, new[] { new PaymentInput(PaymentMethod.Cash, 5_000) }));

        var missing = await CreateSut().ReprintAsync("HD000000-9999");
        var blank = await CreateSut().ReprintAsync(" ");
        SignIn(Permissions.InventoryView);
        var stockKeeper = await CreateSut().GetForSaleAsync(sold.SaleId);
        SignIn(Permissions.ReportsView);
        var manager = await CreateSut().GetForSaleAsync(sold.SaleId);
        _session.SignOut();
        var anonymous = await CreateSut().GetForSaleAsync(sold.SaleId);

        Assert.Equal("not_found", missing.Error.Code);
        Assert.Equal("validation", blank.Error.Code);
        Assert.Equal("forbidden", stockKeeper.Error.Code);
        Assert.True(manager.IsSuccess);
        Assert.Equal("auth.required", anonymous.Error.Code);
    }

    [Fact]
    public async Task Voided_sale_receipt_carries_status()
    {
        var sold = await SellAsync(new CheckoutRequest(
            new[] { new CheckoutLine(_noodles.Id, 1) }, new[] { new PaymentInput(PaymentMethod.Cash, 5_000) }));
        _sales.All.Single().Void(_clock.Now, _cashierId, "Khách đổi ý");

        var receipt = (await CreateSut().ReprintAsync(sold.Code)).Value;
        var text = ReceiptFormatter.ToText(ReceiptFormatter.Format(receipt));

        Assert.Equal(SaleStatus.Voided, receipt.Status);
        Assert.Contains("HÓA ĐƠN ĐÃ HỦY", text);
        Assert.Contains("BẢN IN LẠI", text);
    }

    [Theory]
    [InlineData("0901234567", "0901***567")]
    [InlineData("02838123456", "0283***456")]
    [InlineData("123", "123")]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    public void Phone_is_masked(string? phone, string? expected) =>
        Assert.Equal(expected, ReceiptService.MaskPhone(phone));
}

public class ReceiptFormatterTests
{
    private static ReceiptData Sample(StoreProfile? store = null, bool reprint = false, string? note = null) => new(
        store ?? new StoreProfile { Name = "Tạp hóa Cô Ba", Address = "12 Nguyễn Trãi, Q.1", Phone = "0909123456", TaxCode = "0312345678" },
        "HD261008-0001",
        new DateTimeOffset(2026, 10, 8, 9, 5, 0, TimeSpan.FromHours(7)),
        "Trần Thị B",
        SaleStatus.Completed,
        new[]
        {
            new ReceiptItem("Sữa tươi Vinamilk 1L", "hộp", 2, 35_000, 70_000, "Giảm 10%", 7_000, 63_000),
            new ReceiptItem("Dầu ăn Simply đậu nành nguyên chất chai 1 lít loại đặc biệt", "chai", 1, 62_000, 62_000, null, 0, 62_000),
            new ReceiptItem("Thịt heo xay", "kg", 0.35m, 140_000, 49_000, null, 0, 49_000),
        },
        GrossAmount: 181_000,
        LineDiscountTotal: 7_000,
        InvoiceDiscountLabel: "Giảm 5%",
        InvoiceDiscountAmount: 8_700,
        PointsDiscountAmount: 5_000,
        Total: 160_300,
        Payments: new[] { new ReceiptPayment("Chuyển khoản", 100_000, "FT26281001"), new ReceiptPayment("Tiền mặt", 70_000, null) },
        AmountPaid: 170_000,
        Change: 9_700,
        VatBreakdown: new[] { new VatSummary(0, 49_000, 0, 49_000), new VatSummary(5, 111_300, 5_300, 106_000) },
        Member: new ReceiptMember("Nguyễn Văn A", "0901***567", 16, 50, 66),
        Note: note,
        IsReprint: reprint,
        PrintedAt: reprint ? new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.FromHours(7)) : null);

    [Theory]
    [InlineData(ReceiptFormatter.Columns80Mm)]
    [InlineData(ReceiptFormatter.Columns58Mm)]
    public void Every_line_fits_the_paper_width_exactly(int columns)
    {
        var lines = ReceiptFormatter.Format(Sample(note: "Giao hàng tận nhà trước 17h, gọi trước khi đến"), columns);

        Assert.All(lines, l => Assert.Equal(columns, l.Text.Length));
    }

    [Fact]
    public void Layout_contains_header_items_totals_payments_vat_and_points()
    {
        var lines = ReceiptFormatter.Format(Sample());
        var text = ReceiptFormatter.ToText(lines);

        Assert.Equal("TẠP HÓA CÔ BA", lines[0].Text.Trim());
        Assert.True(lines[0].IsBold);
        Assert.Contains("ĐT: 0909123456 - MST: 0312345678", text);
        Assert.Contains(lines, l => l.Text.StartsWith("Số: HD261008-0001 ") && l.Text.EndsWith(" 08/10/2026 09:05"));
        Assert.Contains(lines, l => l.Text.StartsWith("Tổng tiền hàng (3 món)") && l.Text.EndsWith("181.000"));
        Assert.Contains("1. Sữa tươi Vinamilk 1L", text);
        Assert.Contains(lines, l => l.Text.StartsWith("   2 hộp x 35.000") && l.Text.EndsWith("70.000"));
        Assert.Contains(lines, l => l.Text.StartsWith("   Giảm 10%") && l.Text.EndsWith("-7.000"));
        Assert.Contains(lines, l => l.Text.StartsWith("   0,35 kg x 140.000") && l.Text.EndsWith("49.000"));
        Assert.Contains(lines, l => l.Text.StartsWith("Giảm giá hóa đơn (5%)") && l.Text.EndsWith("-8.700"));
        Assert.Contains(lines, l => l.Text.StartsWith("Đổi 50 điểm") && l.Text.EndsWith("-5.000"));
        var total = Assert.Single(lines, l => l.Text.StartsWith("TỔNG THANH TOÁN"));
        Assert.True(total.IsBold);
        Assert.EndsWith("160.300 ₫", total.Text);
        Assert.Contains(lines, l => l.Text.StartsWith("  Mã GD:") && l.Text.EndsWith("FT26281001"));
        Assert.Contains(lines, l => l.Text.StartsWith("Tiền thừa trả khách") && l.Text.EndsWith("9.700"));
        Assert.Contains(lines, l => l.Text.StartsWith("  Thuế 5% trên 111.300") && l.Text.EndsWith("5.300"));
        Assert.DoesNotContain("Thuế 0%", text);
        Assert.Contains(lines, l => l.Text.StartsWith("Điểm hiện có:") && l.Text.EndsWith("66"));
        Assert.DoesNotContain("BẢN IN LẠI", text);
    }

    [Fact]
    public void Long_product_names_wrap_under_the_item_number()
    {
        var lines = ReceiptFormatter.Format(Sample(), ReceiptFormatter.Columns58Mm).Select(l => l.Text).ToList();

        var first = lines.FindIndex(l => l.StartsWith("2. Dầu ăn"));
        Assert.True(first >= 0);
        Assert.StartsWith("   ", lines[first + 1]);
        Assert.False(string.IsNullOrWhiteSpace(lines[first + 1]));
        // Words are never cut in the middle.
        var joined = string.Join(" ", lines.Skip(first).TakeWhile(l => !l.StartsWith("   1 chai")).Select(l => l.Trim()));
        Assert.Equal("2. Dầu ăn Simply đậu nành nguyên chất chai 1 lít loại đặc biệt", joined);
    }

    [Fact]
    public void Reprint_shows_banner_and_print_time_and_multi_line_footer()
    {
        var store = new StoreProfile { Name = "Cô Ba", ReceiptFooter = "Cảm ơn quý khách!\r\nĐổi trả trong 7 ngày" };
        var text = ReceiptFormatter.ToText(ReceiptFormatter.Format(Sample(store, reprint: true)));

        Assert.Contains("** BẢN IN LẠI **", text);
        Assert.Contains("In lại lúc 09/10/2026 08:00", text);
        Assert.Contains("Cảm ơn quý khách!", text);
        Assert.Contains("Đổi trả trong 7 ngày", text);
        Assert.DoesNotContain("MST", text);
    }

    [Fact]
    public void Decomposed_vietnamese_text_is_normalized_to_one_column_per_letter()
    {
        var decomposed = "Tạp hóa".Normalize(System.Text.NormalizationForm.FormD);

        var centered = ReceiptFormatter.Center(decomposed, 11);

        Assert.Equal(11, centered.Length);
        Assert.Equal("  Tạp hóa  ", centered);
    }

    [Fact]
    public void Left_right_wraps_long_labels_and_keeps_value_on_last_row()
    {
        var rows = ReceiptFormatter.LeftRight("Giảm giá chương trình khuyến mãi tháng mười", "-12.000", 24);

        Assert.All(rows, r => Assert.Equal(24, r.Length));
        Assert.EndsWith("-12.000", rows[^1]);
        Assert.True(rows.Count >= 2);
    }

    [Fact]
    public void Wrap_splits_words_longer_than_a_row()
    {
        var rows = ReceiptFormatter.Wrap("ABCDEFGHIJKLMNOPQRSTUVWXYZ end", 10);

        Assert.Equal(new[] { "ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZ end" }, rows);
    }

    [Fact]
    public void Paper_width_maps_to_columns_and_narrow_widths_are_rejected()
    {
        Assert.Equal(48, ReceiptFormatter.ColumnsFor(80));
        Assert.Equal(32, ReceiptFormatter.ColumnsFor(58));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptFormatter.Format(Sample(), 20));
    }
}
