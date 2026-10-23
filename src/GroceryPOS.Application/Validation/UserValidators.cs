using FluentValidation;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Users;

namespace GroceryPOS.Application.Validation;

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.Username).Must(UserRules.IsValidUsername).WithMessage(UserErrors.InvalidUsername.Message);
        RuleFor(r => r.FullName).NotEmpty().WithMessage("Họ tên không được để trống.")
            .MaximumLength(100).WithMessage("Họ tên tối đa 100 ký tự.");
        RuleFor(r => r.RoleId).GreaterThan(0).WithMessage("Vui lòng chọn vai trò.");
        RuleFor(r => r.Password).Custom(UserValidation.CheckPassword);
        RuleFor(r => r.ConfirmPassword).Equal(r => r.Password).WithMessage("Mật khẩu xác nhận không khớp.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().WithMessage("Họ tên không được để trống.")
            .MaximumLength(100).WithMessage("Họ tên tối đa 100 ký tự.");
        RuleFor(r => r.RoleId).GreaterThan(0).WithMessage("Vui lòng chọn vai trò.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.NewPassword).Custom(UserValidation.CheckPassword);
        RuleFor(r => r.ConfirmPassword).Equal(r => r.NewPassword).WithMessage("Mật khẩu xác nhận không khớp.");
    }
}

public sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Tên vai trò không được để trống.")
            .MaximumLength(50).WithMessage("Tên vai trò tối đa 50 ký tự.");
        RuleFor(r => r.Description).OptionalText("Mô tả", 200);
        RuleFor(r => r.Permissions).NotNull();
        RuleForEach(r => r.Permissions)
            .Must(Domain.Identity.Permissions.IsKnown).WithMessage((_, key) => $"Quyền không hợp lệ: {key}");
    }
}

internal static class UserValidation
{
    public static void CheckPassword(string? password, ValidationContext<CreateUserRequest> context) => Check(password, context);

    public static void CheckPassword(string? password, ValidationContext<ResetPasswordRequest> context) => Check(password, context);

    private static void Check<T>(string? password, ValidationContext<T> context)
    {
        if (PasswordPolicy.Validate(password) is { } error)
        {
            context.AddFailure(error);
        }
    }
}
