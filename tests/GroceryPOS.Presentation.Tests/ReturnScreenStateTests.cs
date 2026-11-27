using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Sales;

namespace GroceryPOS.Presentation.Tests;

public sealed class ReturnScreenStateTests
{
    private static ReturnableSaleDto Sale(
        bool withinWindow = true,
        bool canReturn = true,
        bool fullyReturned = false,
        int? customerId = null,
        int pointsEarned = 0,
        int pointsRedeemed = 0) => new(
        1, "HD261127-0001", new DateTimeOffset(2026, 11, 27, 1, 15, 0, TimeSpan.Zero), customerId, customerId is null ? null : "Nguyễn Văn A",
        40_000, fullyReturned ? 40_000 : 0, pointsEarned, pointsRedeemed, withinWindow, canReturn,
        [new ReturnableLineDto(11, 1, "SUA01", "Sữa tươi", "hộp", 4, fullyReturned ? 4 : 0, fullyReturned ? 0 : 4, 40_000, fullyReturned ? 0 : 40_000)]);

    private static string Plain(string text) => text.Replace(' ', ' ');

    [Fact]
    public void No_invoice_shows_the_scan_prompt_and_nothing_can_be_done()
    {
        var state = ReturnScreenState.From(null, SalesOptions.Default, hasOpenShift: true, busy: false);

        Assert.Same(ReturnScreenState.Empty, state);
        Assert.False(state.CanSubmit);
        Assert.Null(state.BadgeText);
        Assert.Equal(ReturnScreenState.NoInvoiceMessage, state.Message);
    }

    [Theory]
    [InlineData(true, true, false, "Trong hạn đổi trả", Tone.Success)]
    [InlineData(false, true, false, "Quá hạn đổi trả", Tone.Warning)]
    [InlineData(false, false, false, "Quá hạn đổi trả", Tone.Danger)]
    [InlineData(true, true, true, "Đã trả hết", Tone.Muted)]
    public void Badge_follows_the_return_window(bool within, bool canReturn, bool fullyReturned, string text, Tone tone)
    {
        var state = ReturnScreenState.From(new ReturnSheet(Sale(within, canReturn, fullyReturned)), SalesOptions.Default, true, false);

        Assert.Equal(text, state.BadgeText);
        Assert.Equal(tone, state.BadgeTone);
        Assert.Equal(!fullyReturned && canReturn, state.CanReturnAll);
    }

    [Fact]
    public void Header_is_in_store_time_and_summary_replaces_the_blocker_when_ready()
    {
        var sheet = new ReturnSheet(Sale());
        sheet.SetQuantity(11, 1);
        sheet.SetReason("Hàng lỗi");

        var state = ReturnScreenState.From(sheet, SalesOptions.Default, hasOpenShift: true, busy: false);

        Assert.Equal("HD261127-0001 · 27/11/2026 08:15 · Khách lẻ · Tổng 40.000 ₫", Plain(state.InvoiceInfo));
        Assert.Equal("1 dòng · SL 1", state.LineCount);
        Assert.Equal("10.000 ₫", Plain(state.Refund));
        Assert.Equal("Hoàn 10.000 ₫ tiền mặt cho 1 sản phẩm.", Plain(state.Message));
        Assert.Equal(Tone.Muted, state.MessageTone);
        Assert.True(state.CanSubmit);
        Assert.False(ReturnScreenState.From(sheet, SalesOptions.Default, true, busy: true).CanSubmit);
    }

    [Fact]
    public void Cash_refund_needs_an_open_shift_unless_the_store_does_not_use_shifts()
    {
        var sheet = new ReturnSheet(Sale());
        sheet.SetQuantity(11, 1);
        sheet.SetReason("Hàng lỗi");

        var blocked = ReturnScreenState.From(sheet, SalesOptions.Default, hasOpenShift: false, busy: false);
        Assert.False(blocked.CanSubmit);
        Assert.Equal(Tone.Warning, blocked.MessageTone);
        Assert.StartsWith("Bạn chưa mở ca", blocked.Message);

        Assert.True(ReturnScreenState.From(sheet, SalesOptions.Default with { RequireOpenShift = false }, false, false).CanSubmit);
        Assert.True(ReturnScreenState.From(sheet, SalesOptions.Default, hasOpenShift: null, busy: false).CanSubmit); // unknown: the server decides

        sheet.SetRefundMethod(PaymentMethod.BankTransfer);
        Assert.True(ReturnScreenState.From(sheet, SalesOptions.Default, false, false).CanSubmit);
    }

    [Fact]
    public void Points_line_describes_reversed_and_restored_points_for_members_only()
    {
        var guest = new ReturnSheet(Sale());
        guest.ReturnAll();
        Assert.Equal(string.Empty, ReturnScreenState.From(guest, SalesOptions.Default, true, false).Points);

        var member = new ReturnSheet(Sale(customerId: 5, pointsEarned: 4, pointsRedeemed: 10));
        Assert.Equal("Không thay đổi điểm tích lũy.", ReturnScreenState.From(member, SalesOptions.Default, true, false).Points);

        member.ReturnAll();
        Assert.Equal("Trừ 4 điểm đã tích, hoàn lại 10 điểm đã dùng.", ReturnScreenState.From(member, SalesOptions.Default, true, false).Points);
    }

    [Fact]
    public void Line_actions_need_a_returnable_line_and_no_refund_in_progress()
    {
        var sheet = new ReturnSheet(Sale());
        var line = sheet.Lines[0];

        Assert.True(ReturnLineActions.For(sheet, line).CanEdit);
        Assert.False(ReturnLineActions.For(sheet, line, busy: true).CanEdit);
        Assert.False(ReturnLineActions.For(sheet, null).CanEdit);
        Assert.False(ReturnLineActions.For(new ReturnSheet(Sale(fullyReturned: true)), line).CanEdit);
    }

    [Fact]
    public void Done_text_mentions_points_and_write_off()
    {
        var slip = new ReturnReceipt(1, "TH261127-0001", "HD261127-0001", DateTimeOffset.UnixEpoch, 35_000, PaymentMethod.BankTransfer,
            PointsReversed: 3, PointsRestored: 10, CustomerPointsBalance: 40, SaleFullyReturned: true, WrittenOffCost: 28_000, Lines: [],
            Reason: "Hàng lỗi", CustomerName: null);

        Assert.Equal(
            "✔ TH261127-0001: hoàn 35.000 ₫ (Chuyển khoản) cho HĐ HD261127-0001 · trừ 3 điểm · hoàn lại 10 điểm đã dùng · hủy hàng giá vốn 28.000 ₫",
            Plain(ReturnTexts.Done(slip)));
    }
}
