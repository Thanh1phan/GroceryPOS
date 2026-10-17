using FluentValidation;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Validation;

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(r => r.ProductId).Id("Vui lòng chọn sản phẩm.");
        RuleFor(r => r.Reason).IsInEnum().WithMessage("Lý do điều chỉnh không hợp lệ.");
        RuleFor(r => r.Quantity).NotEqual(0).WithMessage("Số lượng điều chỉnh phải khác 0.");
        RuleFor(r => r.Quantity)
            .LessThan(0).WithMessage(r => $"Lý do \"{r.Reason.DisplayName()}\" chỉ dùng để giảm tồn kho.")
            .When(r => r.Reason.Direction() < 0 && r.Quantity != 0);
        RuleFor(r => r.Quantity)
            .GreaterThan(0).WithMessage(r => $"Lý do \"{r.Reason.DisplayName()}\" chỉ dùng để tăng tồn kho.")
            .When(r => r.Reason.Direction() > 0 && r.Quantity != 0);
        RuleFor(r => r.Note)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng ghi chú lý do điều chỉnh.")
            .When(r => r.Reason.RequiresNote());
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}

public sealed class StockCountRequestValidator : AbstractValidator<StockCountRequest>
{
    public StockCountRequestValidator()
    {
        RuleFor(r => r.Lines)
            .NotEmpty().WithMessage("Phiếu kiểm kê chưa có sản phẩm.")
            .UniqueBy(l => l.ProductId, "Sản phẩm bị trùng trong phiếu kiểm kê.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).Id("Sản phẩm không hợp lệ.");
            line.RuleFor(l => l.CountedQuantity).GreaterThanOrEqualTo(0).WithMessage("Số lượng thực tế không được âm.");
        });
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}
