using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Shifts;

/// <summary>
/// UI-free state of the "Thu / chi tiền mặt" dialog: direction, amount, reason (with one-click common reasons), the
/// drawer balance after the movement and the blockers the cashier sees before saving – a pay-out can never exceed
/// the cash currently in the drawer.
/// </summary>
public sealed class CashMovementDraft
{
    public static readonly IReadOnlyList<string> PayInReasons = ["Đổi tiền lẻ", "Bổ sung tiền thối", "Chủ cửa hàng nộp thêm"];

    public static readonly IReadOnlyList<string> PayOutReasons = ["Mua đá / nước", "Trả tiền giao hàng", "Rút tiền nộp chủ", "Chi vặt"];

    public CashMovementDraft(decimal cashInDrawer, CashMovementType type)
    {
        CashInDrawer = cashInDrawer;
        Type = type;
    }

    /// <summary>Cash the system expects in the drawer right now.</summary>
    public decimal CashInDrawer { get; }

    public CashMovementType Type { get; private set; }

    public decimal Amount { get; private set; }

    public string? Reason { get; private set; }

    public bool IsPayOut => Type == CashMovementType.PayOut;

    public IReadOnlyList<string> QuickReasons => IsPayOut ? PayOutReasons : PayInReasons;

    public decimal DrawerAfter => IsPayOut ? CashInDrawer - Amount : CashInDrawer + Amount;

    /// <summary>Text of the save button for the current direction.</summary>
    public string SaveText => IsPayOut ? "Chi tiền" : "Nộp vào két";

    public string? AmountError => Amount <= 0
        ? "Số tiền phải lớn hơn 0."
        : IsPayOut && Amount > CashInDrawer
            ? $"Trong két chỉ còn {Vnd.Format(Math.Max(0, CashInDrawer))}."
            : null;

    public string? ReasonError => Reason is null ? "Vui lòng nhập lý do." : null;

    public string? Blocker => AmountError ?? ReasonError;

    public bool CanSave => Blocker is null;

    /// <summary>"Tiền mặt trong két: 1.200.000 ₫ → 1.000.000 ₫", or the current balance while no amount is entered.</summary>
    public string Preview => Amount <= 0
        ? $"Tiền mặt trong két hiện tại: {Vnd.Format(CashInDrawer)}"
        : $"Tiền mặt trong két: {Vnd.Format(CashInDrawer)} → {Vnd.Format(DrawerAfter)}";

    public void SetType(CashMovementType type) => Type = type;

    public void SetAmount(decimal amount) => Amount = Math.Max(0, Math.Round(amount, 0, MidpointRounding.AwayFromZero));

    public void SetReason(string? reason) => Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    public Result<CashMovementRequest> BuildRequest() =>
        Blocker is { } blocker
            ? Error.Validation(blocker)
            : new CashMovementRequest(Type, Amount, Reason!);
}
