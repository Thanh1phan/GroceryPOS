using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Tests;

/// <summary>Return screen state (<see cref="ReturnSheet"/>), the open-shift rule for cash refunds and the refund slip.</summary>
public class ReturnSheetTests
{
    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemorySaleReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly InMemoryShiftRepository _shifts = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 09:00 in Vietnam
    private readonly Product _milk;
    private readonly Product _noodles;
    private readonly Product _rice;
    private readonly Customer _member;

    public ReturnSheetTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _products.Add(_milk);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        _rice = new Product("GAO01", "Gạo ST25", 1, "kg", costPrice: 28_000, sellPrice: 37_000, VatRate.Five);
        _rice.AdjustStock(50);
        _products.Add(_rice);

        _member = new Customer("KH001", "Nguyễn Văn A", "0901234567");
        _member.RecordPurchase(500_000, LoyaltyPolicy.Default); // 50 points to redeem
        _customers.Add(_member);
    }

    private ReturnService CreateSut(bool withShifts = false) =>
        new(_sales, _returns, _products, _movements, _customers, _uow, _session, _audit, _clock, shifts: withShifts ? _shifts : null);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(7, "thungan", "Trần Thị B", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    /// <summary>
    /// Member basket with every kind of discount: milk 3 × 35.000 −10%, noodles 7 × 5.000, rice 1,5 kg × 37.000,
    /// invoice −5% and 20 points redeemed, so the line refunds are uneven shares.
    /// </summary>
    private async Task<string> SellDiscountedAsync()
    {
        SignIn(Permissions.SalesCreate, Permissions.SalesDiscount);
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var result = await checkout.CheckoutAsync(new CheckoutRequest(
            new[]
            {
                new CheckoutLine(_milk.Id, 3, DiscountInput.Percent(10)),
                new CheckoutLine(_noodles.Id, 7),
                new CheckoutLine(_rice.Id, 1.5m),
            },
            new[] { new PaymentInput(PaymentMethod.Cash, 500_000) },
            CustomerId: _member.Id,
            InvoiceDiscount: DiscountInput.Percent(5),
            PointsToRedeem: 20));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        SignIn(Permissions.ReturnsCreate);
        return result.Value.Code;
    }

    private async Task<ReturnSheet> LoadSheetAsync(string code)
    {
        var loaded = await CreateSut().GetReturnableAsync(code);
        Assert.True(loaded.IsSuccess);
        return new ReturnSheet(loaded.Value);
    }

    private static int LineId(ReturnSheet sheet, string productCode) =>
        sheet.Lines.Single(l => l.Line.ProductCode == productCode).SaleLineId;

    [Fact]
    public async Task Preview_matches_what_the_service_refunds_across_partial_and_final_returns()
    {
        var code = await SellDiscountedAsync();

        // First return: part of two lines.
        var sheet = await LoadSheetAsync(code);
        sheet.SetQuantity(LineId(sheet, "SUA01"), 1);
        sheet.SetQuantity(LineId(sheet, "MI01"), 3);
        sheet.SetReason("Khách đổi ý");
        var preview = (Refund: sheet.RefundTotal, Reversed: sheet.PointsToReverse, Restored: sheet.PointsToRestore,
            Lines: sheet.SelectedLines.Select(l => l.Refund).ToList());

        var first = await CreateSut().CreateAsync(sheet.BuildRequest().Value);
        Assert.True(first.IsSuccess);
        Assert.Equal(preview.Refund, first.Value.RefundTotal);
        Assert.Equal(preview.Reversed, first.Value.PointsReversed);
        Assert.Equal(preview.Restored, first.Value.PointsRestored);
        Assert.Equal(preview.Lines, first.Value.Lines.Select(l => l.RefundAmount).ToList());
        Assert.Equal(0, preview.Restored);

        // Second return: everything left – redeemed points come back, refunds add up to the invoice total.
        sheet = await LoadSheetAsync(code);
        sheet.ReturnAll();
        sheet.SetReason("Trả nốt");
        Assert.True(sheet.ReturnsEverythingLeft);
        preview = (sheet.RefundTotal, sheet.PointsToReverse, sheet.PointsToRestore, sheet.SelectedLines.Select(l => l.Refund).ToList());

        var second = await CreateSut().CreateAsync(sheet.BuildRequest().Value with { RefundMethod = PaymentMethod.BankTransfer });
        Assert.True(second.IsSuccess);
        Assert.Equal(preview.Refund, second.Value.RefundTotal);
        Assert.Equal(preview.Reversed, second.Value.PointsReversed);
        Assert.Equal(20, second.Value.PointsRestored);
        Assert.Equal(preview.Restored, second.Value.PointsRestored);
        Assert.True(second.Value.SaleFullyReturned);

        var sale = _sales.All.Single(s => s.Code == code);
        Assert.Equal(sale.Total, first.Value.RefundTotal + second.Value.RefundTotal);
    }

    [Fact]
    public async Task Quantities_are_rounded_and_clamped_to_what_is_left()
    {
        var sheet = await LoadSheetAsync(await SellDiscountedAsync());
        var rice = LineId(sheet, "GAO01");

        sheet.SetQuantity(rice, 5);
        Assert.Equal(1.5m, sheet.LineOf(rice).Quantity);

        sheet.SetQuantity(rice, 0.12345m);
        Assert.Equal(0.123m, sheet.LineOf(rice).Quantity);

        sheet.Step(rice, -1);
        Assert.Equal(0m, sheet.LineOf(rice).Quantity);
        Assert.False(sheet.LineOf(rice).IsSelected);

        sheet.ToggleLine(rice);
        Assert.Equal(1.5m, sheet.LineOf(rice).Quantity);
        sheet.ToggleLine(rice);
        Assert.Equal(0m, sheet.LineOf(rice).Quantity);
    }

    [Fact]
    public async Task Blocker_walks_through_selection_then_reason()
    {
        var sheet = await LoadSheetAsync(await SellDiscountedAsync());
        var changes = 0;
        sheet.Changed += (_, _) => changes++;

        Assert.Equal("Chọn sản phẩm và số lượng khách trả.", sheet.Blocker);
        Assert.True(sheet.BuildRequest().IsFailure);

        sheet.ToggleLine(LineId(sheet, "MI01"));
        Assert.Equal("Vui lòng nhập lý do trả hàng.", sheet.Blocker);

        sheet.SetReason("   ");
        Assert.False(sheet.CanSubmit);

        sheet.SetReason("  Hàng lỗi / hư hỏng ");
        sheet.SetRestock(LineId(sheet, "MI01"), false);
        sheet.SetRefundMethod(PaymentMethod.BankTransfer);
        Assert.True(sheet.CanSubmit);
        Assert.True(changes >= 5);

        var request = sheet.BuildRequest().Value;
        Assert.Equal("Hàng lỗi / hư hỏng", request.Reason);
        Assert.Equal(PaymentMethod.BankTransfer, request.RefundMethod);
        var line = Assert.Single(request.Lines);
        Assert.Equal(7m, line.Quantity);
        Assert.False(line.Restock);
        Assert.Equal(sheet.RefundTotal, sheet.WrittenOffRefund);
        Assert.Equal("Hủy hàng (hỏng)", sheet.LineOf(LineId(sheet, "MI01")).HandlingText);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SetRefundMethod(PaymentMethod.Card));
    }

    [Fact]
    public async Task Expired_or_fully_returned_invoices_cannot_be_submitted()
    {
        var code = await SellDiscountedAsync();
        _clock.Advance(TimeSpan.FromDays(10));

        var sheet = await LoadSheetAsync(code);
        Assert.False(sheet.CanReturnAnything);
        sheet.ReturnAll();
        sheet.SetReason("Khách đổi ý");
        Assert.Equal("Hóa đơn đã quá hạn đổi trả. Vui lòng nhờ quản lý thực hiện.", sheet.Blocker);

        // A manager may still take it; once everything is back the invoice shows as fully returned.
        SignIn(Permissions.ReturnsCreate, Permissions.SalesVoid);
        sheet = await LoadSheetAsync(code);
        Assert.True(sheet.CanReturnAnything);
        sheet.ReturnAll();
        sheet.SetReason("Quản lý duyệt");
        Assert.True((await CreateSut().CreateAsync(sheet.BuildRequest().Value)).IsSuccess);

        sheet = await LoadSheetAsync(code);
        Assert.True(sheet.Sale.IsFullyReturned);
        Assert.All(sheet.Lines, l => Assert.True(l.IsExhausted));
        Assert.Equal("Hóa đơn đã được trả hết hàng.", sheet.Blocker);
    }

    [Fact]
    public async Task Cash_refund_needs_an_open_shift_but_transfer_does_not()
    {
        var code = await SellDiscountedAsync();
        var sheet = await LoadSheetAsync(code);
        sheet.ToggleLine(LineId(sheet, "MI01"));
        sheet.SetReason("Khách đổi ý");
        var request = sheet.BuildRequest().Value;

        var noShift = await CreateSut(withShifts: true).CreateAsync(request);
        Assert.True(noShift.IsFailure);
        Assert.Equal(ReturnErrors.NoOpenShiftForCashRefund, noShift.Error);
        Assert.Empty(_returns.All);

        var transfer = await CreateSut(withShifts: true).CreateAsync(request with { RefundMethod = PaymentMethod.BankTransfer });
        Assert.True(transfer.IsSuccess);

        _shifts.Add(new Shift("CA261008-0001", 7, _clock.Now, 500_000));
        sheet = await LoadSheetAsync(code);
        sheet.ToggleLine(LineId(sheet, "SUA01"));
        sheet.SetReason("Khách đổi ý");
        Assert.True((await CreateSut(withShifts: true).CreateAsync(sheet.BuildRequest().Value)).IsSuccess);
    }

    [Fact]
    public async Task Refund_slip_lists_items_points_reason_and_fits_the_paper()
    {
        var code = await SellDiscountedAsync();
        var sheet = await LoadSheetAsync(code);
        sheet.SetQuantity(LineId(sheet, "SUA01"), 1);
        sheet.ToggleLine(LineId(sheet, "MI01"));
        sheet.SetRestock(LineId(sheet, "MI01"), false);
        sheet.SetReason("Hàng hết hạn sử dụng, khách phát hiện khi về nhà mở ra dùng");
        var receipt = (await CreateSut().CreateAsync(sheet.BuildRequest().Value)).Value;

        Assert.Equal("Hàng hết hạn sử dụng, khách phát hiện khi về nhà mở ra dùng", receipt.Reason);
        Assert.Equal("Nguyễn Văn A", receipt.CustomerName);
        Assert.Equal(2, receipt.Lines.Count);

        var store = new StoreProfile { Name = "Tạp hóa Cô Ba", Address = "12 Nguyễn Trãi, Q.1", Phone = "0909 123 456" };
        foreach (var columns in new[] { ReceiptFormatter.Columns80Mm, ReceiptFormatter.Columns58Mm })
        {
            var lines = ReturnSlipFormatter.Format(receipt, store, "Trần Thị B", columns, TimeSpan.FromHours(7));
            Assert.All(lines, l => Assert.Equal(columns, l.Text.Length));

            var text = ReceiptFormatter.ToText(lines);
            Assert.Contains("PHIẾU TRẢ HÀNG", text);
            Assert.Contains(receipt.Code, text);
            Assert.Contains(code, text);
            Assert.Contains("08/10/2026 09:00", text);
            Assert.Contains("Trần Thị B", text);
            Assert.Contains("1. Sữa tươi Vinamilk 1L", text);
            Assert.Contains("(hàng hỏng, không nhập kho)", text);
            Assert.Contains("TỔNG TIỀN HOÀN", text);
            Assert.Contains("Tiền mặt", text);
            Assert.Contains("Điểm tích lũy bị trừ", text);
            Assert.Contains("Lý do: Hàng hết hạn", text);
            Assert.Single(lines, l => l.Text.Contains("TỔNG TIỀN HOÀN") && l.IsBold);
        }
    }
}
