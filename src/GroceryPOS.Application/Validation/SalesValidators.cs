using FluentValidation;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Validation;

public sealed class DiscountInputValidator : AbstractValidator<DiscountInput>
{
    public DiscountInputValidator()
    {
        RuleFor(d => d.Kind).IsInEnum().WithMessage("Loại giảm giá không hợp lệ.");
        RuleFor(d => d.Value)
            .InclusiveBetween(0, 100).WithMessage("Giảm giá theo % phải từ 0 đến 100.")
            .When(d => d.Kind == DiscountKind.Percent);
        RuleFor(d => d.Value).Money("Số tiền giảm").When(d => d.Kind == DiscountKind.Amount);
    }
}

/// <summary>Pre-flight checks for the POS "Thanh toán" button; stock, prices and loyalty balance are checked by the service.</summary>
public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.Lines).NotEmpty().WithMessage(SaleErrors.EmptyCart.Message);
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).Id("Sản phẩm không hợp lệ.");
            line.RuleFor(l => l.Quantity).PositiveQuantity();
            line.RuleFor(l => l.Discount!).SetValidator(new DiscountInputValidator()).When(l => l.Discount is not null);
        });

        RuleFor(r => r.Payments).NotEmpty().WithMessage(SaleErrors.NoPayment.Message);
        RuleForEach(r => r.Payments).ChildRules(payment =>
        {
            payment.RuleFor(p => p.Method).IsInEnum().WithMessage("Hình thức thanh toán không hợp lệ.");
            payment.RuleFor(p => p.Amount).PositiveMoney("Số tiền thanh toán");
            payment.RuleFor(p => p.Reference).OptionalText("Mã giao dịch", RuleExtensions.ReferenceMaxLength);
        });

        RuleFor(r => r.InvoiceDiscount!).SetValidator(new DiscountInputValidator()).When(r => r.InvoiceDiscount is not null);
        RuleFor(r => r.PointsToRedeem).GreaterThanOrEqualTo(0).WithMessage("Số điểm sử dụng không được âm.");
        RuleFor(r => r.CustomerId)
            .NotNull().WithMessage("Vui lòng chọn khách hàng thành viên để dùng điểm.")
            .When(r => r.PointsToRedeem > 0);
        RuleFor(r => r.CustomerId!.Value).Id("Khách hàng không hợp lệ.").When(r => r.CustomerId is not null);
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}

public sealed class CreateReturnRequestValidator : AbstractValidator<CreateReturnRequest>
{
    public CreateReturnRequestValidator()
    {
        RuleFor(r => r.SaleId).Id(SaleErrors.SaleNotFound.Message);
        RuleFor(r => r.Lines)
            .NotEmpty().WithMessage(ReturnErrors.EmptyReturn.Message)
            .UniqueBy(l => l.SaleLineId, "Một sản phẩm bị chọn trả nhiều lần.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.SaleLineId).Id("Dòng hàng không hợp lệ.");
            line.RuleFor(l => l.Quantity).PositiveQuantity();
        });
        RuleFor(r => r.Reason).RequiredText("Vui lòng nhập lý do trả hàng.", "Lý do");
        RuleFor(r => r.RefundMethod).IsInEnum().WithMessage("Hình thức hoàn tiền không hợp lệ.");
    }
}
