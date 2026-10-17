using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Tests;

public sealed class ValidatorTests
{
    private static CheckoutRequest ValidCheckout() => new(
        new[] { new CheckoutLine(1, 2), new CheckoutLine(2, 0.5m, DiscountInput.Percent(10)) },
        new[] { new PaymentInput(PaymentMethod.Cash, 100_000) });

    [Fact]
    public void Valid_checkout_passes()
    {
        var result = new CheckoutRequestValidator().Validate(ValidCheckout());

        Assert.True(result.IsValid);
        Assert.True(result.ToResult().IsSuccess);
        Assert.Empty(result.ToFieldErrors());
    }

    [Fact]
    public void Empty_cart_and_missing_payment_reuse_service_messages()
    {
        var result = new CheckoutRequestValidator().Validate(new CheckoutRequest(
            Array.Empty<CheckoutLine>(), Array.Empty<PaymentInput>()));

        var fields = result.ToFieldErrors();
        Assert.Equal(SaleErrors.EmptyCart.Message, Assert.Single(fields["Lines"]));
        Assert.Equal(SaleErrors.NoPayment.Message, Assert.Single(fields["Payments"]));

        var error = result.ToError();
        Assert.Equal("validation", error.Code);
        Assert.Equal(SaleErrors.EmptyCart.Message, error.Message);
    }

    [Fact]
    public void Checkout_line_rules_report_indexed_property_paths()
    {
        var request = ValidCheckout() with
        {
            Lines = new[]
            {
                new CheckoutLine(1, 1),
                new CheckoutLine(2, 0, DiscountInput.Percent(120)),
            },
            Payments = new[] { new PaymentInput(PaymentMethod.BankTransfer, 10_000.5m) },
        };

        var fields = new CheckoutRequestValidator().Validate(request).ToFieldErrors();

        Assert.Contains("Số lượng phải lớn hơn 0.", fields["Lines[1].Quantity"]);
        Assert.Contains("Giảm giá theo % phải từ 0 đến 100.", fields["Lines[1].Discount.Value"]);
        Assert.Contains("Số tiền thanh toán phải là số tiền chẵn đồng.", fields["Payments[0].Amount"]);
    }

    [Fact]
    public void Redeeming_points_needs_a_member()
    {
        var withoutMember = new CheckoutRequestValidator().Validate(ValidCheckout() with { PointsToRedeem = 50 });
        var withMember = new CheckoutRequestValidator().Validate(ValidCheckout() with { PointsToRedeem = 50, CustomerId = 7 });
        var negativeAmountDiscount = new CheckoutRequestValidator().Validate(ValidCheckout() with { InvoiceDiscount = DiscountInput.Amount(-1) });

        Assert.Equal("Vui lòng chọn khách hàng thành viên để dùng điểm.", withoutMember.ToError().Message);
        Assert.True(withMember.IsValid);
        Assert.Contains("InvoiceDiscount.Value", negativeAmountDiscount.ToFieldErrors().Keys);
    }

    [Theory]
    [InlineData(StockAdjustmentReason.Expired, 3, "Lý do \"Hết hạn sử dụng\" chỉ dùng để giảm tồn kho.")]
    [InlineData(StockAdjustmentReason.Found, -3, "Lý do \"Phát hiện thừa\" chỉ dùng để tăng tồn kho.")]
    [InlineData(StockAdjustmentReason.Correction, 0, "Số lượng điều chỉnh phải khác 0.")]
    [InlineData(StockAdjustmentReason.Other, -1, "Vui lòng ghi chú lý do điều chỉnh.")]
    public void Stock_adjustment_follows_reason_direction_and_note_rules(StockAdjustmentReason reason, int quantity, string expected)
    {
        var result = new StockAdjustmentRequestValidator().Validate(new StockAdjustmentRequest(5, quantity, reason));

        Assert.Equal(expected, result.ToError().Message);
    }

    [Fact]
    public void Stock_adjustment_with_matching_direction_is_valid()
    {
        var validator = new StockAdjustmentRequestValidator();

        Assert.True(validator.Validate(new StockAdjustmentRequest(5, -2, StockAdjustmentReason.Damaged)).IsValid);
        Assert.True(validator.Validate(new StockAdjustmentRequest(5, 2, StockAdjustmentReason.Correction)).IsValid);
        Assert.True(validator.Validate(new StockAdjustmentRequest(5, 1, StockAdjustmentReason.Other, "Hàng khuyến mãi NCC tặng")).IsValid);
    }

    [Fact]
    public void Stock_count_rejects_duplicates_and_negative_counts()
    {
        var result = new StockCountRequestValidator().Validate(new StockCountRequest(new[]
        {
            new StockCountLine(1, 10), new StockCountLine(1, -1),
        }));

        var fields = result.ToFieldErrors();
        Assert.Contains("Sản phẩm bị trùng trong phiếu kiểm kê.", fields["Lines"]);
        Assert.Contains("Số lượng thực tế không được âm.", fields["Lines[1].CountedQuantity"]);
    }

    [Fact]
    public void Goods_receipt_checks_supplier_lines_discounts_and_payment()
    {
        var validator = new SaveGoodsReceiptRequestValidator();
        var lines = new[] { new GoodsReceiptLineInput(1, 10, 20_000, 5_000), new GoodsReceiptLineInput(2, 5, 10_000) };

        Assert.True(validator.Validate(new SaveGoodsReceiptRequest(null, 3, lines, DiscountAmount: 5_000, PaidAmount: 240_000)).IsValid);

        var fields = validator.Validate(new SaveGoodsReceiptRequest(
            null,
            0,
            new[] { new GoodsReceiptLineInput(1, 2, 1_000, 3_000), new GoodsReceiptLineInput(1, 1, 1_000, NewSellPrice: 0) },
            PaidAmount: 999_999)).ToFieldErrors();

        Assert.Contains("Vui lòng chọn nhà cung cấp.", fields["SupplierId"]);
        Assert.Contains("Sản phẩm bị trùng trong phiếu nhập.", fields["Lines"]);
        Assert.Contains("Chiết khấu dòng không được vượt quá thành tiền.", fields["Lines[0].DiscountAmount"]);
        Assert.Contains("Giá bán mới phải lớn hơn 0.", fields["Lines[1].NewSellPrice.Value"]);
        Assert.Contains("Số tiền trả không được vượt quá tổng phiếu nhập.", fields["PaidAmount"]);
    }

    [Fact]
    public void Supplier_payment_and_return_rules()
    {
        Assert.Equal("Số tiền chi phải lớn hơn 0.",
            new SupplierPaymentRequestValidator().Validate(new SupplierPaymentRequest(1, 0)).ToError().Message);

        var supplierReturn = new CreateSupplierReturnRequestValidator().Validate(new CreateSupplierReturnRequest(
            1, new[] { new SupplierReturnLineInput(4, 2) }, " ", RefundAmount: 50_000)).ToFieldErrors();

        Assert.Contains("Vui lòng nhập lý do trả hàng.", supplierReturn["Reason"]);
        Assert.Contains("Vui lòng chọn hình thức nhận tiền hoàn.", supplierReturn["RefundMethod"]);
    }

    [Fact]
    public void Customer_return_needs_lines_and_reason()
    {
        var empty = new CreateReturnRequestValidator().Validate(new CreateReturnRequest(1, Array.Empty<ReturnLineInput>(), "Hàng lỗi"));
        var noReason = new CreateReturnRequestValidator().Validate(new CreateReturnRequest(1, new[] { new ReturnLineInput(9, 1) }, ""));
        var ok = new CreateReturnRequestValidator().Validate(new CreateReturnRequest(1, new[] { new ReturnLineInput(9, 1, Restock: false) }, "Hàng lỗi"));

        Assert.Equal(ReturnErrors.EmptyReturn.Message, empty.ToError().Message);
        Assert.Equal("Vui lòng nhập lý do trả hàng.", noReason.ToError().Message);
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void Shift_open_and_cash_movement_rules()
    {
        Assert.Equal("Tiền đầu ca không được âm.",
            new OpenShiftRequestValidator().Validate(new OpenShiftRequest(-1)).ToError().Message);
        Assert.True(new OpenShiftRequestValidator().Validate(new OpenShiftRequest(500_000)).IsValid);
        Assert.Equal("Vui lòng nhập lý do thu/chi.",
            new CashMovementRequestValidator().Validate(new CashMovementRequest(CashMovementType.PayOut, 20_000, "")).ToError().Message);
    }

    [Fact]
    public void Close_shift_requires_a_count_and_matching_totals()
    {
        var validator = new CloseShiftRequestValidator();
        var notes = new Dictionary<decimal, int> { [500_000] = 1, [10_000] = 3 };

        Assert.Equal("Vui lòng nhập số tiền kiểm đếm.", validator.Validate(new CloseShiftRequest(1)).ToError().Message);
        Assert.True(validator.Validate(new CloseShiftRequest(1, Notes: notes, CashLeftInDrawer: 30_000)).IsValid);
        Assert.True(validator.Validate(new CloseShiftRequest(1, 530_000, notes)).IsValid);

        var mismatch = validator.Validate(new CloseShiftRequest(1, 520_000, notes)).ToFieldErrors();
        Assert.StartsWith("Tổng tiền đếm theo mệnh giá (530.000", Assert.Single(mismatch["Notes"]));

        var badNote = validator.Validate(new CloseShiftRequest(1, Notes: new Dictionary<decimal, int> { [300_000] = 1 }));
        Assert.Equal("Mệnh giá 300.000 không hợp lệ.", badNote.ToError().Message);

        Assert.Equal("Tiền để lại két không được vượt quá tiền thực đếm.",
            validator.Validate(new CloseShiftRequest(1, 100_000, CashLeftInDrawer: 200_000)).ToError().Message);
    }

    [Fact]
    public void Login_and_change_password_inputs()
    {
        Assert.Equal("Vui lòng nhập tên đăng nhập.", new LoginInputValidator().Validate(new LoginInput(" ", "x")).ToError().Message);
        Assert.True(new LoginInputValidator().Validate(new LoginInput("admin", "admin123")).IsValid);

        var validator = new ChangePasswordInputValidator();
        Assert.True(validator.Validate(new ChangePasswordInput("admin123", "matkhau2026", "matkhau2026")).IsValid);
        Assert.Equal("Mật khẩu phải chứa cả chữ và số.",
            validator.Validate(new ChangePasswordInput("admin123", "abcdefgh", "abcdefgh")).ToError().Message);
        Assert.Equal(AuthErrors.SamePassword.Message,
            validator.Validate(new ChangePasswordInput("admin123", "admin123", "admin123")).ToError().Message);
        Assert.Equal("Mật khẩu xác nhận không khớp.",
            validator.Validate(new ChangePasswordInput("admin123", "matkhau2026", "matkhau2025")).ToError().Message);
    }

    [Fact]
    public void Check_extension_returns_validation_result()
    {
        var failure = new OpenShiftRequestValidator().Check(new OpenShiftRequest(1.5m));

        Assert.True(failure.IsFailure);
        Assert.Equal("Tiền đầu ca phải là số tiền chẵn đồng.", failure.Error.Message);
    }
}
