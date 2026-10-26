using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Tests;

/// <summary>Close-shift count sheet, shift report layout and the "open shift before checkout" rule.</summary>
public class ShiftScreenSupportTests
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero); // 08:00 VN

    private static ShiftSummaryDto Summary(
        decimal expected = 1_250_000,
        ShiftStatus status = ShiftStatus.Open,
        decimal? counted = null,
        decimal? left = null,
        string? closeNote = null,
        IReadOnlyList<CashMovementDto>? movements = null)
    {
        var totals = new ShiftTotals(
            SalesCount: 42, SalesTotal: 3_200_000, DiscountTotal: 45_000, CashSales: 800_000, TransferSales: 2_100_000, CardSales: 300_000,
            VoidCount: 1, VoidTotal: 50_000, VoidCash: 50_000, ReturnCount: 1, CashRefunds: 20_000, TransferRefunds: 0);
        return new ShiftSummaryDto(
            5, "CA261008-0001", 7, "Trần Thị B", status, ShiftLabels.Of(status), Opened,
            status == ShiftStatus.Closed ? Opened.AddHours(8) : null,
            500_000, totals, 100_000, 80_000, expected,
            counted, counted - expected, left, counted - left, "Nhận két từ ca sáng", closeNote,
            movements ?? Array.Empty<CashMovementDto>());
    }

    // ---- CashCountSheet ---------------------------------------------------------------------------

    [Fact]
    public void Sheet_sums_notes_by_denomination_and_computes_variance()
    {
        var sheet = new CashCountSheet(Summary(expected: 1_250_000));
        sheet.SetCount(500_000, 2);
        sheet.SetCount(100_000, 2);
        sheet.SetCount(20_000, 2);
        sheet.SetCount(5_000, 1);

        Assert.Equal(1_245_000m, sheet.Counted);
        Assert.Equal(7, sheet.NoteCount);
        Assert.Equal(200_000m, sheet.AmountOf(100_000));
        Assert.Equal(-5_000m, sheet.Variance);
        Assert.Equal("Thiếu tiền", sheet.VarianceLabel);
        Assert.True(sheet.NoteRequired); // |−5.000| ≥ 5.000 threshold
        Assert.Contains("ghi chú", sheet.Blocker);
    }

    [Fact]
    public void Sheet_small_variance_needs_no_note_and_builds_request_with_counted_notes_only()
    {
        var sheet = new CashCountSheet(Summary(expected: 1_000_000));
        sheet.SetCount(500_000, 2);
        sheet.SetCount(1_000, 2); // +2.000 thừa – below threshold
        sheet.SetCashLeftInDrawer(500_000);

        Assert.False(sheet.NoteRequired);
        Assert.True(sheet.CanClose);
        Assert.Equal(502_000m, sheet.CashHandedOver);

        var request = sheet.BuildRequest().Value;
        Assert.Equal(5, request.ShiftId);
        Assert.Null(request.CountedCash);
        Assert.Equal(2, request.Notes!.Count);
        Assert.Equal(1_002_000m, CashCount.Total(request.Notes));
        Assert.Equal(500_000m, request.CashLeftInDrawer);
    }

    [Fact]
    public void Sheet_blocks_untouched_count_and_cash_left_above_counted()
    {
        var sheet = new CashCountSheet(Summary(expected: 300_000));
        Assert.Equal("Vui lòng đếm tiền mặt trong két.", sheet.Blocker);
        Assert.True(sheet.BuildRequest().IsFailure);

        sheet.SetCount(100_000, 3);
        sheet.SetCashLeftInDrawer(400_000);
        Assert.Equal("Tiền để lại két không được lớn hơn tiền mặt thực đếm.", sheet.Blocker);

        sheet.LeaveAll();
        Assert.Equal(300_000m, sheet.CashLeftInDrawer);
        Assert.Equal(0m, sheet.CashHandedOver);
        Assert.True(sheet.CanClose);
    }

    [Fact]
    public void Sheet_allows_closing_an_empty_drawer_without_count()
    {
        var sheet = new CashCountSheet(Summary(expected: 0));

        Assert.True(sheet.CanClose);
        Assert.Equal("Khớp", sheet.VarianceLabel);
        Assert.Empty(sheet.BuildRequest().Value.Notes!);
    }

    [Fact]
    public void Sheet_typed_mode_carries_over_denomination_total_and_note_unblocks()
    {
        var sheet = new CashCountSheet(Summary(expected: 1_250_000));
        sheet.SetCount(500_000, 2);
        var changes = 0;
        sheet.Changed += (_, _) => changes++;

        sheet.SetMode(CashCountMode.TypedTotal);
        Assert.Equal(1_000_000m, sheet.Counted);

        sheet.SetTypedTotal(1_200_000);
        Assert.True(sheet.NoteRequired);
        Assert.False(sheet.CanClose);
        sheet.SetNote("  Thối nhầm cho khách  ");
        Assert.True(sheet.CanClose);

        var request = sheet.BuildRequest().Value;
        Assert.Equal(1_200_000m, request.CountedCash);
        Assert.Null(request.Notes);
        Assert.Equal("Thối nhầm cho khách", request.Note);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Sheet_rejects_unknown_denomination_and_clamps_negative_counts()
    {
        var sheet = new CashCountSheet(Summary(), SalesOptions.Default);

        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SetCount(300_000, 1));
        sheet.SetCount(50_000, -3);
        Assert.Equal(0, sheet.CountOf(50_000));

        sheet.SetCount(50_000, 4);
        sheet.Clear();
        Assert.Equal(0m, sheet.Counted);
    }

    // ---- ShiftReportFormatter ---------------------------------------------------------------------

    [Fact]
    public void Live_report_is_marked_as_mid_shift_and_shows_expected_cash_only()
    {
        var printedAt = Opened.AddHours(3);
        var lines = ShiftReportFormatter.Format(Summary(), StoreProfile.Default, ReceiptFormatter.Columns80Mm, TimeSpan.FromHours(7), printedAt);
        var text = ReceiptFormatter.ToText(lines);

        Assert.All(lines, l => Assert.Equal(48, l.Text.Length));
        Assert.Contains("BÁO CÁO GIỮA CA", text);
        Assert.Contains("CA261008-0001", text);
        Assert.Contains("08/10/2026 08:00", text);     // opened, store time
        Assert.Contains("Đến lúc:", text);
        Assert.Contains("1.250.000 ₫", text);           // expected cash
        Assert.DoesNotContain("Tiền mặt thực đếm", text);
        Assert.DoesNotContain("Người nhận", text);
        Assert.Contains(lines, l => l.IsBold && l.Text.Contains("DOANH THU THUẦN"));
    }

    [Fact]
    public void Closed_report_shows_count_variance_handover_movements_and_signatures()
    {
        var movements = new[]
        {
            new CashMovementDto(1, CashMovementType.PayIn, "Nộp tiền vào két", 100_000, "Đổi tiền lẻ", Opened.AddHours(1)),
            new CashMovementDto(2, CashMovementType.PayOut, "Chi tiền từ két", 80_000, "Mua đá lạnh", Opened.AddHours(2)),
        };
        var summary = Summary(status: ShiftStatus.Closed, counted: 1_240_000, left: 500_000, closeNote: "Thiếu do thối nhầm", movements: movements);

        var lines = ShiftReportFormatter.Format(summary, StoreProfile.Default, ReceiptFormatter.Columns58Mm, TimeSpan.FromHours(7), Opened.AddHours(8));
        var text = ReceiptFormatter.ToText(lines);

        Assert.All(lines, l => Assert.Equal(32, l.Text.Length));
        Assert.Contains("BÁO CÁO KẾT CA", text);
        Assert.Contains("1.240.000 ₫", text);
        Assert.Contains("-10.000", text);           // variance
        Assert.Contains("Thiếu tiền", text);
        Assert.Contains("740.000", text);           // handed over
        Assert.Contains("09:00 Đổi tiền lẻ", text);
        Assert.Contains("-80.000", text);
        Assert.Contains("Ghi chú kết ca: Thiếu do thối", text); // close note (wrapped at 32 columns)
        Assert.Contains("Người nhận", text);
        Assert.Contains("16:00", text);             // closed at, store time
    }

    // ---- Checkout requires an open shift ----------------------------------------------------------

    [Fact]
    public async Task Checkout_requires_open_shift_when_shift_repository_is_wired()
    {
        var sales = new InMemorySaleRepository();
        var products = new InMemoryProductRepository();
        var shifts = new InMemoryShiftRepository();
        var clock = new FakeClock();
        var session = new UserSessionStore();
        session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, new HashSet<string> { Permissions.SalesCreate }, clock.Now));
        var noodles = new Product("MI01", "Mì Hảo Hảo", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        noodles.AdjustStock(10);
        products.Add(noodles);

        CheckoutService Sut(SalesOptions? options = null) => new(
            sales, products, new InMemoryStockMovementRepository(), new InMemoryCustomerRepository(), new InvoiceNumberGenerator(sales),
            new FakeUnitOfWork(), session, new RecordingAuditService(), clock, options, shifts);
        var request = new CheckoutRequest(new[] { new CheckoutLine(noodles.Id, 1) }, new[] { new PaymentInput(PaymentMethod.Cash, 5_000) });

        var refused = await Sut().CheckoutAsync(request);
        Assert.Equal(SaleErrors.NoOpenShift, refused.Error);
        Assert.Empty(sales.All);
        Assert.Equal(10m, noodles.StockQuantity);

        // Stores that do not use shifts can switch the rule off.
        Assert.True((await Sut(SalesOptions.Default with { RequireOpenShift = false }).CheckoutAsync(request)).IsSuccess);

        shifts.Add(new Shift("CA261008-0001", 7, clock.Now, 500_000));
        Assert.True((await Sut().CheckoutAsync(request)).IsSuccess);
        Assert.Equal(2, sales.All.Count);
    }
}
