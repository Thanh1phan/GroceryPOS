using FluentValidation;
using GroceryPOS.Application.Catalog;

namespace GroceryPOS.Application.Validation;

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Tên danh mục không được để trống.").MaximumLength(100);
        RuleFor(r => r.Description).OptionalText("Mô tả");
    }
}
