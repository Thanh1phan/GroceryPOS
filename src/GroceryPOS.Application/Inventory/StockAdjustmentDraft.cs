using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Inventory;

/// <summary>
/// UI-free state of the stock adjustment dialog (điều chỉnh tồn kho): reason, direction (fixed by reasons such as
/// "Hư hỏng" = giảm, "Phát hiện thừa" = tăng), quantity, note, stock after and its value, plus the blocker that
/// mirrors <see cref="StockMovement.Apply"/> so the storekeeper sees the problem before saving.
/// </summary>
public sealed class StockAdjustmentDraft
{
    /// <summary>Reasons in the order shown in the dialog: decreases first, then increases, then either way.</summary>
    public static readonly IReadOnlyList<StockAdjustmentReason> Reasons =
    [
        StockAdjustmentReason.Damaged,
        StockAdjustmentReason.Expired,
        StockAdjustmentReason.Lost,
        StockAdjustmentReason.InternalUse,
        StockAdjustmentReason.Found,
        StockAdjustmentReason.Correction,
        StockAdjustmentReason.Other,
    ];

    private bool _increase;

    public StockAdjustmentDraft(StockItem product)
    {
        Product = product ?? throw new ArgumentNullException(nameof(product));
        Reason = StockAdjustmentReason.Damaged;
    }

    public StockItem Product { get; }

    public StockAdjustmentReason Reason { get; private set; }

    /// <summary>True = nhập thêm (tăng), false = xuất bớt (giảm). Fixed by the reason when it has a direction.</summary>
    public bool Increase => Reason.Direction() switch
    {
        > 0 => true,
        < 0 => false,
        _ => _increase,
    };

    /// <summary>Whether the user may pick the direction (only for reasons without one).</summary>
    public bool CanChooseDirection => Reason.Direction() == 0;

    /// <summary>Quantity entered (always ≥ 0); the sign comes from <see cref="Increase"/>.</summary>
    public decimal Quantity { get; private set; }

    public string? Note { get; private set; }

    public bool NoteRequired => Reason.RequiresNote();

    public decimal SignedQuantity => Increase ? Quantity : -Quantity;

    public decimal BalanceAfter => Product.StockQuantity + SignedQuantity;

    /// <summary>Same rounding as <see cref="StockMovement.CostValue"/>.</summary>
    public decimal CostValue => Math.Round(SignedQuantity * Product.CostPrice, 0, MidpointRounding.AwayFromZero);

    public bool WillBeLowStock => BalanceAfter <= Product.MinStockLevel;

    public string? Blocker
    {
        get
        {
            if (Quantity <= 0)
            {
                return "Nhập số lượng điều chỉnh lớn hơn 0.";
            }

            if (BalanceAfter < 0)
            {
                return $"Tồn kho hiện chỉ còn {FormatQty(Math.Max(0, Product.StockQuantity))} {Product.Unit}, không thể giảm {FormatQty(Quantity)}.";
            }

            if (NoteRequired && Note is null)
            {
                return "Vui lòng ghi chú lý do điều chỉnh.";
            }

            return null;
        }
    }

    public bool CanSave => Blocker is null;

    public void SetReason(StockAdjustmentReason reason)
    {
        // Keep the effective direction, so switching to a free reason does not flip it.
        _increase = Increase;
        Reason = reason;
    }

    /// <summary>Sets the direction for reasons without one; ignored otherwise.</summary>
    public void SetIncrease(bool increase)
    {
        if (CanChooseDirection)
        {
            _increase = increase;
        }
    }

    public void SetQuantity(decimal quantity) =>
        Quantity = Math.Max(0, Math.Round(quantity, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Sets the quantity so that stock ends at <paramref name="target"/> (and the direction for free reasons).
    /// Returns false, changing nothing, when the reason's direction cannot reach the target.
    /// </summary>
    public bool SetTargetBalance(decimal target)
    {
        var delta = Math.Round(target, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero) - Product.StockQuantity;
        if (delta != 0)
        {
            var direction = Reason.Direction();
            if (direction != 0 && Math.Sign(delta) != direction)
            {
                return false;
            }

            _increase = delta > 0;
        }

        SetQuantity(Math.Abs(delta));
        return true;
    }

    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public Result<StockAdjustmentRequest> BuildRequest() =>
        Blocker is { } blocker
            ? Error.Validation(blocker)
            : new StockAdjustmentRequest(Product.ProductId, SignedQuantity, Reason, Note);

    private static string FormatQty(decimal quantity) => quantity.ToString("#,0.###", Vnd.Culture);
}
