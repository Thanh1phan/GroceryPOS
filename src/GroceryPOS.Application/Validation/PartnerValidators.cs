using FluentValidation;
using GroceryPOS.Application.Partners;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Validation;

public sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator()
    {
        RuleFor(r => r.Code).MaximumLength(32).WithMessage("Mã nhà cung cấp tối đa 32 ký tự.");
        RuleFor(r => r.Name).NotEmpty().WithMessage("Tên nhà cung cấp không được để trống.").MaximumLength(200);
        RuleFor(r => r.ContactPerson).OptionalText("Người liên hệ", 100);
        RuleFor(r => r.Phone).Must(PartnerRules.IsValidPhone).WithMessage("Số điện thoại không hợp lệ.");
        RuleFor(r => r.Email).Must(PartnerRules.IsValidEmail).WithMessage("Email không hợp lệ.");
        RuleFor(r => r.Address).OptionalText("Địa chỉ");
        RuleFor(r => r.TaxCode).Must(PartnerRules.IsValidTaxCode).WithMessage("Mã số thuế gồm 10 hoặc 13 chữ số (dạng 0123456789-001).");
    }
}

public sealed class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().WithMessage("Tên khách hàng không được để trống.").MaximumLength(200);
        RuleFor(r => r.Phone).Must(PartnerRules.IsValidPhone).WithMessage("Số điện thoại không hợp lệ.");
        RuleFor(r => r.Email).Must(PartnerRules.IsValidEmail).WithMessage("Email không hợp lệ.");
        RuleFor(r => r.Address).OptionalText("Địa chỉ");
    }
}

internal static class PartnerRules
{
    public static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return true;
        }

        try
        {
            PhoneNumber.NormalizeOptional(phone);
            return true;
        }
        catch (Domain.Common.DomainException)
        {
            return false;
        }
    }

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        return trimmed.Length <= 100
               && at > 0
               && at == trimmed.LastIndexOf('@')
               && trimmed.IndexOf('.', at) > at + 1
               && !trimmed.EndsWith('.')
               && !trimmed.Any(char.IsWhiteSpace);
    }

    /// <summary>Vietnamese tax code (MST): 10 digits, or 10 digits + "-" + 3 digits for a branch (13 digits without the dash).</summary>
    public static bool IsValidTaxCode(string? taxCode)
    {
        if (string.IsNullOrWhiteSpace(taxCode))
        {
            return true;
        }

        var value = taxCode.Trim();
        if (value.Length == 14 && value[10] == '-')
        {
            value = value.Remove(10, 1);
        }

        return value.Length is 10 or 13 && value.All(char.IsAsciiDigit);
    }
}
