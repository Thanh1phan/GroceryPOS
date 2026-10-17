using FluentValidation;
using GroceryPOS.Application.Purchasing;

namespace GroceryPOS.Application.Validation;

public sealed class GoodsReceiptLineInputValidator : AbstractValidator<GoodsReceiptLineInput>
{
    public GoodsReceiptLineInputValidator()
    {
        RuleFor(l => l.ProductId).Id("Sản phẩm không hợp lệ.");
        RuleFor(l => l.Quantity).PositiveQuantity();
        RuleFor(l => l.UnitCost).Money("Giá nhập");
        RuleFor(l => l.DiscountAmount).Money("Chiết khấu");
        RuleFor(l => l.DiscountAmount)
            .Must((l, discount) => discount <= l.Quantity * l.UnitCost)
            .WithMessage("Chiết khấu dòng không được vượt quá thành tiền.");
        RuleFor(l => l.NewSellPrice!.Value).PositiveMoney("Giá bán mới").When(l => l.NewSellPrice is not null);
    }
}

public sealed class SaveGoodsReceiptRequestValidator : AbstractValidator<SaveGoodsReceiptRequest>
{
    public SaveGoodsReceiptRequestValidator()
    {
        RuleFor(r => r.SupplierId).Id("Vui lòng chọn nhà cung cấp.");
        RuleFor(r => r.Lines)
            .NotEmpty().WithMessage("Phiếu nhập chưa có sản phẩm.")
            .UniqueBy(l => l.ProductId, "Sản phẩm bị trùng trong phiếu nhập.");
        RuleForEach(r => r.Lines).SetValidator(new GoodsReceiptLineInputValidator());
        RuleFor(r => r.DiscountAmount).Money("Chiết khấu phiếu");
        RuleFor(r => r.DiscountAmount)
            .Must((r, discount) => discount <= LinesTotal(r))
            .WithMessage("Chiết khấu phiếu không được vượt quá tổng tiền hàng.");
        RuleFor(r => r.PaidAmount).Money("Số tiền trả");
        RuleFor(r => r.PaidAmount)
            .Must((r, paid) => paid <= Math.Max(0, LinesTotal(r) - r.DiscountAmount))
            .WithMessage("Số tiền trả không được vượt quá tổng phiếu nhập.");
        RuleFor(r => r.SupplierInvoiceNo).OptionalText("Số hóa đơn NCC", 50);
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }

    private static decimal LinesTotal(SaveGoodsReceiptRequest request) =>
        request.Lines?.Sum(l => Math.Max(0, l.Quantity * l.UnitCost - l.DiscountAmount)) ?? 0;
}

public sealed class SupplierPaymentRequestValidator : AbstractValidator<SupplierPaymentRequest>
{
    public SupplierPaymentRequestValidator()
    {
        RuleFor(r => r.SupplierId).Id("Vui lòng chọn nhà cung cấp.");
        RuleFor(r => r.Amount).PositiveMoney("Số tiền chi");
        RuleFor(r => r.Method).IsInEnum().WithMessage("Hình thức thanh toán không hợp lệ.");
        RuleFor(r => r.Reference).OptionalText("Mã giao dịch", RuleExtensions.ReferenceMaxLength);
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}

public sealed class CreateSupplierReturnRequestValidator : AbstractValidator<CreateSupplierReturnRequest>
{
    public CreateSupplierReturnRequestValidator()
    {
        RuleFor(r => r.SupplierId).Id("Vui lòng chọn nhà cung cấp.");
        RuleFor(r => r.GoodsReceiptId!.Value).Id("Phiếu nhập không hợp lệ.").When(r => r.GoodsReceiptId is not null);
        RuleFor(r => r.Lines)
            .NotEmpty().WithMessage("Phiếu trả hàng chưa có sản phẩm.")
            .UniqueBy(l => l.ProductId, "Sản phẩm bị lặp trong phiếu trả hàng.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).Id("Sản phẩm không hợp lệ.");
            line.RuleFor(l => l.Quantity).PositiveQuantity();
            line.RuleFor(l => l.UnitPrice!.Value).Money("Đơn giá trả").When(l => l.UnitPrice is not null);
        });
        RuleFor(r => r.Reason).RequiredText("Vui lòng nhập lý do trả hàng.", "Lý do");
        RuleFor(r => r.RefundAmount).Money("Số tiền NCC hoàn");
        RuleFor(r => r.RefundMethod)
            .NotNull().WithMessage("Vui lòng chọn hình thức nhận tiền hoàn.")
            .When(r => r.RefundAmount > 0);
        RuleFor(r => r.RefundMethod!.Value).IsInEnum().WithMessage("Hình thức nhận tiền hoàn không hợp lệ.").When(r => r.RefundMethod is not null);
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}
