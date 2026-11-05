using FluentValidation;
using GroceryPOS.Application.Settings;

namespace GroceryPOS.Application.Validation;

public sealed class StoreSettingsValidator : AbstractValidator<StoreSettings>
{
    public const int MaxFooterLines = 6;
    public const int MaxFooterLength = 400;
    public const decimal MaxVndPerPoint = 10_000_000m;
    public const int MaxReturnWindowDays = 90;
    public const decimal MaxVarianceThreshold = 1_000_000m;

    public StoreSettingsValidator()
    {
        // Store identity
        RuleFor(s => s.StoreName).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Tên cửa hàng không được để trống.")
            .MaximumLength(100).WithMessage("Tên cửa hàng tối đa 100 ký tự.");
        RuleFor(s => s.Address).OptionalText("Địa chỉ", 200);
        RuleFor(s => s.Phone).Must(PartnerRules.IsValidPhone).WithMessage("Số điện thoại cửa hàng không hợp lệ.");
        RuleFor(s => s.TaxCode).Must(PartnerRules.IsValidTaxCode).WithMessage("Mã số thuế gồm 10 hoặc 13 chữ số (dạng 0123456789-001).");

        // Receipt
        RuleFor(s => s.ReceiptFooter).MaximumLength(MaxFooterLength).WithMessage($"Lời cảm ơn tối đa {MaxFooterLength} ký tự.")
            .Must(f => StoreSettings.FooterLineCount(f) <= MaxFooterLines).WithMessage($"Lời cảm ơn tối đa {MaxFooterLines} dòng.");
        RuleFor(s => s.ReceiptPaperWidthMm).Must(w => w is 58 or 80).WithMessage("Khổ giấy chỉ hỗ trợ 58mm hoặc 80mm.");

        // Loyalty
        RuleFor(s => s.VndPerPoint).Money("Số tiền cho 1 điểm")
            .Must(v => v == 0 || v >= 1_000).WithMessage("Số tiền cho 1 điểm tối thiểu 1.000 ₫ (hoặc 0 để tắt tích điểm).")
            .LessThanOrEqualTo(MaxVndPerPoint).WithMessage("Số tiền cho 1 điểm tối đa 10.000.000 ₫.");
        RuleFor(s => s.PointValueVnd).Money("Giá trị 1 điểm");
        RuleFor(s => s.PointValueVnd)
            .Must((s, v) => s.VndPerPoint <= 0 || v <= s.VndPerPoint)
            .WithMessage("Giá trị 1 điểm không được lớn hơn số tiền mua để được 1 điểm (hoàn lại quá 100%).");

        // Sales rules
        RuleFor(s => s.ReturnWindowDays).InclusiveBetween(0, MaxReturnWindowDays)
            .WithMessage($"Hạn đổi trả từ 0 đến {MaxReturnWindowDays} ngày.");
        RuleFor(s => s.ShiftVarianceNoteThreshold).Money("Mức lệch két")
            .LessThanOrEqualTo(MaxVarianceThreshold).WithMessage("Mức lệch két cần ghi chú tối đa 1.000.000 ₫.");
    }
}
