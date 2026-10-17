using FluentValidation;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Validation;

public sealed class OpenShiftRequestValidator : AbstractValidator<OpenShiftRequest>
{
    public OpenShiftRequestValidator()
    {
        RuleFor(r => r.OpeningFloat).Money("Tiền đầu ca");
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }
}

public sealed class CashMovementRequestValidator : AbstractValidator<CashMovementRequest>
{
    public CashMovementRequestValidator()
    {
        RuleFor(r => r.Type).IsInEnum().WithMessage("Loại thu/chi không hợp lệ.");
        RuleFor(r => r.Amount).PositiveMoney("Số tiền");
        RuleFor(r => r.Reason).RequiredText("Vui lòng nhập lý do thu/chi.", "Lý do");
    }
}

/// <summary>Close-shift count sheet: a typed amount and/or a count by denomination that must agree.</summary>
public sealed class CloseShiftRequestValidator : AbstractValidator<CloseShiftRequest>
{
    public CloseShiftRequestValidator()
    {
        RuleFor(r => r.ShiftId).Id("Ca làm việc không hợp lệ.");
        RuleFor(r => r)
            .Must(r => r.CountedCash is not null || r.Notes is { Count: > 0 })
            .WithName(nameof(CloseShiftRequest.CountedCash))
            .WithMessage("Vui lòng nhập số tiền kiểm đếm.");
        RuleFor(r => r.CountedCash!.Value).Money("Tiền thực đếm").When(r => r.CountedCash is not null);

        RuleForEach(r => r.Notes).ChildRules(note =>
        {
            note.RuleFor(n => n.Key)
                .Must(d => CashCount.Denominations.Contains(d))
                .WithMessage(n => $"Mệnh giá {Vnd.FormatNumber(n.Key)} không hợp lệ.");
            note.RuleFor(n => n.Value).GreaterThanOrEqualTo(0).WithMessage("Số tờ không được âm.");
        }).When(r => r.Notes is not null);

        RuleFor(r => r)
            .Must(r => NotesTotal(r) == r.CountedCash)
            .WithName(nameof(CloseShiftRequest.Notes))
            .WithMessage(r => $"Tổng tiền đếm theo mệnh giá ({Vnd.Format(NotesTotal(r) ?? 0)}) không khớp số tiền đã nhập ({Vnd.Format(r.CountedCash ?? 0)}).")
            .When(r => r.CountedCash is not null && r.Notes is { Count: > 0 } && NotesAreValid(r.Notes));

        RuleFor(r => r.CashLeftInDrawer).Money("Tiền để lại két");
        RuleFor(r => r.CashLeftInDrawer)
            .Must((r, left) => CountedTotal(r) is not { } counted || left <= counted)
            .WithMessage("Tiền để lại két không được vượt quá tiền thực đếm.");
        RuleFor(r => r.Note).OptionalText("Ghi chú");
    }

    private static bool NotesAreValid(IReadOnlyDictionary<decimal, int> notes) =>
        notes.All(n => CashCount.Denominations.Contains(n.Key) && n.Value >= 0);

    private static decimal? NotesTotal(CloseShiftRequest r) =>
        r.Notes is { Count: > 0 } notes && NotesAreValid(notes) ? CashCount.Total(notes) : null;

    private static decimal? CountedTotal(CloseShiftRequest r) => r.CountedCash ?? NotesTotal(r);
}
