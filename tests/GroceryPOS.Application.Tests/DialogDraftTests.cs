using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using Xunit;

namespace GroceryPOS.Application.Tests;

public sealed class CashMovementDraftTests
{
    [Fact]
    public void Starts_blocked_until_amount_and_reason_are_entered()
    {
        var draft = new CashMovementDraft(500_000, CashMovementType.PayIn);

        Assert.False(draft.CanSave);
        Assert.Equal("Số tiền phải lớn hơn 0.", draft.Blocker);

        draft.SetAmount(200_000);
        Assert.Equal("Vui lòng nhập lý do.", draft.Blocker);

        draft.SetReason("  Đổi tiền lẻ ");
        Assert.True(draft.CanSave);
        var request = draft.BuildRequest();
        Assert.True(request.IsSuccess);
        Assert.Equal(new CashMovementRequest(CashMovementType.PayIn, 200_000, "Đổi tiền lẻ"), request.Value);
    }

    [Fact]
    public void Pay_out_cannot_exceed_the_drawer()
    {
        var draft = new CashMovementDraft(300_000, CashMovementType.PayOut);
        draft.SetReason("Chi vặt");
        draft.SetAmount(350_000);

        Assert.Equal($"Trong két chỉ còn {Vnd.Format(300_000)}.", draft.AmountError);
        Assert.True(draft.BuildRequest().IsFailure);
        Assert.Equal(-50_000, draft.DrawerAfter);

        draft.SetAmount(300_000);
        Assert.True(draft.CanSave);
        Assert.Equal(0, draft.DrawerAfter);
    }

    [Fact]
    public void Switching_direction_changes_reasons_texts_and_the_drawer_after()
    {
        var draft = new CashMovementDraft(100_000, CashMovementType.PayIn);
        draft.SetAmount(150_000);
        Assert.Null(draft.AmountError);
        Assert.Equal("Nộp vào két", draft.SaveText);
        Assert.Contains("Đổi tiền lẻ", draft.QuickReasons);
        Assert.Equal(250_000, draft.DrawerAfter);

        draft.SetType(CashMovementType.PayOut);
        Assert.Equal("Chi tiền", draft.SaveText);
        Assert.Contains("Chi vặt", draft.QuickReasons);
        Assert.NotNull(draft.AmountError);
        Assert.Equal($"Tiền mặt trong két: {Vnd.Format(100_000)} → {Vnd.Format(-50_000)}", draft.Preview);
    }

    [Fact]
    public void Amount_is_rounded_to_the_dong_and_never_negative()
    {
        var draft = new CashMovementDraft(0, CashMovementType.PayIn);

        draft.SetAmount(-5);
        Assert.Equal(0, draft.Amount);
        Assert.Equal($"Tiền mặt trong két hiện tại: {Vnd.Format(0)}", draft.Preview);

        draft.SetAmount(1_000.5m);
        Assert.Equal(1_001, draft.Amount);
    }
}

public sealed class SupplierPaymentDraftTests
{
    private static SupplierPayableDto Supplier(decimal debt) => new(7, "NCC0007", "Công ty Sữa Miền Nam", null, debt);

    [Fact]
    public void Prefills_the_whole_debt_and_settles_it()
    {
        var draft = new SupplierPaymentDraft(Supplier(2_500_000));

        Assert.Equal(2_500_000, draft.Amount);
        Assert.True(draft.SettlesDebt);
        Assert.Equal("Sau khi trả: hết nợ nhà cung cấp.", draft.AfterText);
        Assert.Contains("hết công nợ", draft.ConfirmText);
        Assert.True(draft.CanSave);
    }

    [Fact]
    public void Partial_payment_shows_the_remaining_debt()
    {
        var draft = new SupplierPaymentDraft(Supplier(2_500_000));
        draft.SetAmount(1_000_000);

        Assert.False(draft.SettlesDebt);
        Assert.Equal($"Công nợ còn lại: {Vnd.Format(1_500_000)}", draft.AfterText);
        Assert.Contains("tiền mặt", draft.ConfirmText);
        Assert.Contains(draft.AfterText, draft.ConfirmText);
    }

    [Theory]
    [InlineData(2_500_000, 1_250_000)]
    [InlineData(1_234_567, 617_000)]
    [InlineData(1_500, 750)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Half_is_rounded_to_thousands_and_never_above_the_debt(decimal debt, decimal expected)
    {
        var draft = new SupplierPaymentDraft(Supplier(debt));

        Assert.Equal(expected, draft.HalfAmount);
        draft.PayHalf();
        Assert.Equal(expected, draft.Amount);
        draft.PayAll();
        Assert.Equal(debt, draft.Amount);
    }

    [Fact]
    public void Blocks_zero_and_overpayment()
    {
        var draft = new SupplierPaymentDraft(Supplier(800_000));

        draft.SetAmount(0);
        Assert.Equal("Số tiền phải lớn hơn 0.", draft.Blocker);

        draft.SetAmount(900_000);
        Assert.Equal($"Không được vượt quá công nợ {Vnd.Format(800_000)}.", draft.Blocker);
        Assert.True(draft.BuildRequest().IsFailure);
    }

    [Fact]
    public void Transfer_with_reference_builds_the_request()
    {
        var draft = new SupplierPaymentDraft(Supplier(800_000));
        draft.SetMethod(PaymentMethod.BankTransfer);
        draft.SetReference("  FT2611100001 ");
        draft.SetNote("   ");
        draft.SetAmount(300_000);

        Assert.Equal("Mã giao dịch / số UNC", draft.ReferencePlaceholder);
        Assert.Contains("chuyển khoản", draft.ConfirmText);
        var request = draft.BuildRequest();
        Assert.Equal(new SupplierPaymentRequest(7, 300_000, PaymentMethod.BankTransfer, "FT2611100001", null), request.Value);
    }

    [Fact]
    public void Card_is_not_a_way_to_pay_a_supplier() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupplierPaymentDraft(Supplier(1)).SetMethod(PaymentMethod.Card));
}
