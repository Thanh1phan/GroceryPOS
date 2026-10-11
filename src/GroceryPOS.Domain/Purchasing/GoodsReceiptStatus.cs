namespace GroceryPOS.Domain.Purchasing;

/// <summary>Lifecycle of a goods receipt. Values are persisted; do not renumber.</summary>
public enum GoodsReceiptStatus
{
    /// <summary>Being entered (phiếu tạm); stock and cost are not affected yet.</summary>
    Draft = 0,

    /// <summary>Posted: stock increased, cost price updated, supplier debt recorded.</summary>
    Completed = 1,

    /// <summary>Draft discarded without effect.</summary>
    Cancelled = 2,
}

public static class GoodsReceiptStatusExtensions
{
    public static string DisplayName(this GoodsReceiptStatus status) => status switch
    {
        GoodsReceiptStatus.Draft => "Phiếu tạm",
        GoodsReceiptStatus.Completed => "Đã nhập hàng",
        GoodsReceiptStatus.Cancelled => "Đã hủy",
        _ => status.ToString(),
    };
}
