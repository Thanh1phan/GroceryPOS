namespace GroceryPOS.Domain.Shifts;

/// <summary>Lifecycle of a cashier shift. Values are persisted; do not renumber.</summary>
public enum ShiftStatus
{
    /// <summary>Đang mở – the cashier is selling from this drawer.</summary>
    Open = 0,

    /// <summary>Đã kết ca – drawer counted, totals frozen.</summary>
    Closed = 1,
}

/// <summary>Manual cash movement in/out of the drawer during a shift. Values are persisted; do not renumber.</summary>
public enum CashMovementType
{
    /// <summary>Nộp tiền vào két – e.g. extra small notes for change.</summary>
    PayIn = 1,

    /// <summary>Chi tiền từ két – e.g. paying a delivery, buying ice, cash taken to the bank.</summary>
    PayOut = 2,
}

public static class ShiftLabels
{
    public static string Of(ShiftStatus status) => status switch
    {
        ShiftStatus.Open => "Đang mở",
        ShiftStatus.Closed => "Đã kết ca",
        _ => status.ToString(),
    };

    public static string Of(CashMovementType type) => type switch
    {
        CashMovementType.PayIn => "Nộp tiền vào két",
        CashMovementType.PayOut => "Chi tiền từ két",
        _ => type.ToString(),
    };
}
