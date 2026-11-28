using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Sales;

namespace GroceryPOS.Presentation.Tests;

public sealed class PosBoardTests
{
    private static readonly PosProductDto Milk = new(1, "SUA01", null, "Sữa tươi 1L", "hộp", 32_000, 8, 1, false);

    [Fact]
    public void An_empty_cart_cannot_be_paid_but_shows_no_blocker()
    {
        var board = PosBoard.From(new PosCart(), canDiscount: true, shiftMissing: false, busy: false);

        Assert.False(board.CanPay);
        Assert.Equal(string.Empty, board.Blocker);
        Assert.False(board.CanEditLines);
        Assert.False(board.CanDiscountInvoice);
        Assert.Empty(board.QuickCash);
        Assert.Equal("Khách lẻ", board.CustomerInfo);
        Assert.Equal(Tone.Muted, board.CustomerTone);
        Assert.Equal("0 ₫", board.LineDiscounts);
    }

    [Fact]
    public void A_missing_shift_blocks_payment_before_anything_else()
    {
        var cart = new PosCart();
        cart.Add(Milk);

        var board = PosBoard.From(cart, canDiscount: true, shiftMissing: true, busy: false);

        Assert.Equal(PosBoard.ShiftMissingBlocker, board.Blocker);
        Assert.False(board.CanPay);
        Assert.True(board.CanEditLines);
    }

    [Fact]
    public void Shortfall_and_busy_states_block_paying()
    {
        var cart = new PosCart();
        cart.Add(Milk);
        cart.SetCashTendered(10_000);

        var board = PosBoard.From(cart, canDiscount: false, shiftMissing: false, busy: false);
        Assert.Equal("Khách còn thiếu 22.000 ₫.", board.Blocker);
        Assert.False(board.CanPay);
        Assert.False(board.CanDiscountLines);

        cart.SetCashTendered(null);
        Assert.True(PosBoard.From(cart, true, false, busy: false).CanPay);
        var busy = PosBoard.From(cart, true, false, busy: true);
        Assert.False(busy.CanPay);
        Assert.False(busy.CanEditLines);
    }

    [Fact]
    public void Non_cash_payments_hide_quick_cash_and_change()
    {
        var cart = new PosCart();
        cart.Add(Milk);
        Assert.NotEmpty(PosBoard.From(cart, true, false, false).QuickCash);

        cart.SetPaymentMethod(PaymentMethod.BankTransfer);
        var board = PosBoard.From(cart, true, false, false);

        Assert.False(board.IsCash);
        Assert.Empty(board.QuickCash);
        Assert.Equal("0 ₫", board.Change);
    }

    [Fact]
    public void Members_show_their_points_and_what_they_earn()
    {
        var cart = new PosCart();
        cart.Add(Milk, 10);
        cart.AttachCustomer(new PosCustomer(5, "KH000005", "Anh Minh", "0901234567", 1_200));

        var board = PosBoard.From(cart, true, false, false);

        Assert.True(board.HasMember);
        Assert.StartsWith("Anh Minh · KH000005\r\nĐiểm hiện có: 1.200", board.CustomerInfo, StringComparison.Ordinal);
        Assert.StartsWith("Khách được cộng ", board.Earn, StringComparison.Ordinal);
        Assert.Equal(Tone.Normal, board.CustomerTone);
    }

    [Fact]
    public void Shift_bar_covers_open_failed_and_missing_shifts()
    {
        var options = SalesOptions.Default;
        var shift = new ShiftSummaryDto(
            1, "CA261128-0001", 1, "An", ShiftStatus.Open, "Đang mở", new DateTimeOffset(2026, 11, 28, 1, 30, 0, TimeSpan.Zero), null,
            200_000, new ShiftTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, 0, 200_000, null, null, null, null, null, null, []);

        Assert.Equal(
            new PosShiftBar("Ca CA261128-0001 · mở lúc 08:30 28/11 · tiền đầu ca 200.000 ₫", Tone.Muted, false),
            PosShiftBar.From(shift, null, options));
        Assert.Equal(new PosShiftBar("Lỗi", Tone.Muted, false), PosShiftBar.From(null, "Lỗi", options with { RequireOpenShift = false }));
        Assert.Equal(
            new PosShiftBar("Chưa mở ca – cần mở ca trước khi thanh toán.", Tone.Warning, true),
            PosShiftBar.From(null, null, options with { RequireOpenShift = true }));
    }

    [Fact]
    public void Texts_flag_overselling_and_count_parked_orders()
    {
        var cart = new PosCart();
        var line = cart.Add(Milk, 3);

        var (text, tone) = PosTexts.Added(line);

        Assert.Equal("Đã thêm Sữa tươi 1L × 3 – 96.000 ₫ (vượt tồn kho: còn 1)", text);
        Assert.Equal(Tone.Warning, tone);
        Assert.Equal("Đơn chờ (F11)", PosTexts.ParkedCaption(0));
        Assert.Equal("Đơn chờ (F11) · 2", PosTexts.ParkedCaption(2));
        Assert.Equal("−5.000 ₫", PosTexts.Minus(5_000));
    }
}
