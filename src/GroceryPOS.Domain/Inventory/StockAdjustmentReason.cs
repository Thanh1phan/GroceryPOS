namespace GroceryPOS.Domain.Inventory;

/// <summary>Reason for a manual stock adjustment. Values are persisted; do not renumber.</summary>
public enum StockAdjustmentReason
{
    Other = 0,

    /// <summary>Broken, crushed or spoiled goods (out).</summary>
    Damaged = 1,

    /// <summary>Past the expiry date (out).</summary>
    Expired = 2,

    /// <summary>Missing / shrinkage (out).</summary>
    Lost = 3,

    /// <summary>Taken for the store's own use (out).</summary>
    InternalUse = 4,

    /// <summary>Goods found that were not recorded (in).</summary>
    Found = 5,

    /// <summary>Correction of an earlier data-entry mistake (in or out).</summary>
    Correction = 6,
}

public static class StockAdjustmentReasonExtensions
{
    public static string DisplayName(this StockAdjustmentReason reason) => reason switch
    {
        StockAdjustmentReason.Damaged => "Hư hỏng",
        StockAdjustmentReason.Expired => "Hết hạn sử dụng",
        StockAdjustmentReason.Lost => "Mất mát, thất thoát",
        StockAdjustmentReason.InternalUse => "Sử dụng nội bộ",
        StockAdjustmentReason.Found => "Phát hiện thừa",
        StockAdjustmentReason.Correction => "Sửa sai số liệu",
        _ => "Lý do khác",
    };

    /// <summary>
    /// Direction a reason implies: -1 only decreases stock, +1 only increases, 0 either way.
    /// Used to reject e.g. "expired" with a positive quantity.
    /// </summary>
    public static int Direction(this StockAdjustmentReason reason) => reason switch
    {
        StockAdjustmentReason.Damaged or StockAdjustmentReason.Expired
            or StockAdjustmentReason.Lost or StockAdjustmentReason.InternalUse => -1,
        StockAdjustmentReason.Found => 1,
        _ => 0,
    };

    /// <summary>"Other" needs a written note so the audit trail stays meaningful.</summary>
    public static bool RequiresNote(this StockAdjustmentReason reason) => reason == StockAdjustmentReason.Other;
}
