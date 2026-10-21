using FluentValidation;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Domain.Catalog;

namespace GroceryPOS.Application.Validation;

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().WithMessage("Mã sản phẩm không được để trống.").MaximumLength(32);
        RuleFor(r => r.Name).NotEmpty().WithMessage("Tên sản phẩm không được để trống.").MaximumLength(200);
        RuleFor(r => r.CategoryId).Id("Vui lòng chọn danh mục.");
        RuleFor(r => r.Unit).NotEmpty().WithMessage("Đơn vị tính không được để trống.").MaximumLength(20);
        RuleFor(r => r.CostPrice).Money("Giá vốn");
        RuleFor(r => r.SellPrice).Money("Giá bán");
        RuleFor(r => r.VatRate).Must(VatRate.IsAllowed).WithMessage("Thuế suất VAT không hợp lệ (0, 5, 8 hoặc 10%).");
        RuleFor(r => r.MinStockLevel).GreaterThanOrEqualTo(0).WithMessage("Mức tồn tối thiểu không được âm.");
        RuleFor(r => r.Barcode).Must(b => b is null || Barcode.IsValid(b)).WithMessage("Mã vạch không hợp lệ.");
    }
}
