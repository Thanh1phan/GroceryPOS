using FluentValidation;
using GroceryPOS.Application.Authentication;

namespace GroceryPOS.Application.Validation;

public sealed class LoginInputValidator : AbstractValidator<LoginInput>
{
    public LoginInputValidator()
    {
        RuleFor(i => i.Username).Must(u => !string.IsNullOrWhiteSpace(u)).WithMessage("Vui lòng nhập tên đăng nhập.");
        RuleFor(i => i.Password).NotEmpty().WithMessage("Vui lòng nhập mật khẩu.");
    }
}

public sealed class ChangePasswordInputValidator : AbstractValidator<ChangePasswordInput>
{
    public ChangePasswordInputValidator()
    {
        RuleFor(i => i.CurrentPassword).NotEmpty().WithMessage("Vui lòng nhập mật khẩu hiện tại.");
        RuleFor(i => i.NewPassword).Custom((password, context) =>
        {
            if (PasswordPolicy.Validate(password) is { } error)
            {
                context.AddFailure(error);
            }
        });
        RuleFor(i => i.NewPassword)
            .NotEqual(i => i.CurrentPassword).WithMessage(AuthErrors.SamePassword.Message)
            .When(i => !string.IsNullOrEmpty(i.CurrentPassword));
        RuleFor(i => i.ConfirmPassword).Equal(i => i.NewPassword).WithMessage("Mật khẩu xác nhận không khớp.");
    }
}
