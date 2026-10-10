using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Inventory;

/// <summary>
/// One line of the stock ledger (thẻ kho). Every change to <see cref="Product.StockQuantity"/> goes
/// through <see cref="Apply"/>, so the ledger always explains the current balance:
/// the last movement's <see cref="BalanceAfter"/> equals the product's stock.
/// </summary>
public sealed class StockMovement : Entity
{
    /// <summary>Quantities support weighed goods (kg) with up to 3 decimals.</summary>
    public const int QuantityDecimals = 3;

    private StockMovement()
    {
    }

    private StockMovement(
        Product product,
        StockMovementType type,
        decimal quantity,
        DateTimeOffset timestamp,
        int? userId,
        string? referenceCode,
        StockAdjustmentReason? reason,
        string? note)
    {
        ProductId = product.Id;
        Product = product;
        Type = type;
        Quantity = quantity;
        BalanceAfter = product.StockQuantity;
        UnitCost = product.CostPrice;
        Timestamp = timestamp;
        UserId = userId;
        ReferenceCode = Guard.Optional(referenceCode, 32);
        Reason = reason;
        Note = Guard.Optional(note, 500);
    }

    public int ProductId { get; private set; }
    public Product? Product { get; private set; }
    public StockMovementType Type { get; private set; }

    /// <summary>Signed quantity: positive = into stock, negative = out of stock.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Product stock right after this movement.</summary>
    public decimal BalanceAfter { get; private set; }

    public decimal BalanceBefore => BalanceAfter - Quantity;

    /// <summary>Cost price at the time of the movement (VND), for valuing losses and receipts.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Value of the movement at cost (signed, VND).</summary>
    public decimal CostValue => Math.Round(Quantity * UnitCost, 0, MidpointRounding.AwayFromZero);

    public DateTimeOffset Timestamp { get; private set; }
    public int? UserId { get; private set; }

    /// <summary>Source document code: invoice (HD...), goods receipt (PN...), stock count (KK...).</summary>
    public string? ReferenceCode { get; private set; }

    /// <summary>Set for manual adjustments.</summary>
    public StockAdjustmentReason? Reason { get; private set; }

    public string? Note { get; private set; }

    public bool IsInbound => Quantity > 0;

    /// <summary>
    /// Changes the product's stock by <paramref name="quantity"/> and returns the ledger entry describing it.
    /// The caller adds the movement to the unit of work.
    /// </summary>
    public static StockMovement Apply(
        Product product,
        StockMovementType type,
        decimal quantity,
        DateTimeOffset timestamp,
        int? userId,
        string? referenceCode = null,
        StockAdjustmentReason? reason = null,
        string? note = null,
        bool allowNegative = false)
    {
        ArgumentNullException.ThrowIfNull(product);
        quantity = Math.Round(quantity, QuantityDecimals, MidpointRounding.AwayFromZero);
        if (quantity == 0)
        {
            throw new DomainException("stock.quantity", "Số lượng điều chỉnh phải khác 0.");
        }

        EnsureDirection(type, quantity);

        if (type == StockMovementType.Adjustment)
        {
            if (reason is not { } r)
            {
                throw new DomainException("stock.reason", "Vui lòng chọn lý do điều chỉnh.");
            }

            var direction = r.Direction();
            if (direction != 0 && Math.Sign(quantity) != direction)
            {
                throw new DomainException(
                    "stock.reason",
                    direction < 0
                        ? $"Lý do \"{r.DisplayName()}\" chỉ dùng để giảm tồn kho."
                        : $"Lý do \"{r.DisplayName()}\" chỉ dùng để tăng tồn kho.");
            }

            if (r.RequiresNote() && string.IsNullOrWhiteSpace(note))
            {
                throw new DomainException("stock.note", "Vui lòng ghi chú lý do điều chỉnh.");
            }
        }

        product.AdjustStock(quantity, allowNegative);
        return new StockMovement(product, type, quantity, timestamp, userId, referenceCode, reason, note);
    }

    private static void EnsureDirection(StockMovementType type, decimal quantity)
    {
        var mustBe = type switch
        {
            StockMovementType.Sale or StockMovementType.SupplierReturn => -1,
            StockMovementType.SaleVoid or StockMovementType.CustomerReturn
                or StockMovementType.GoodsReceipt or StockMovementType.Opening => 1,
            _ => 0,
        };

        if (mustBe != 0 && Math.Sign(quantity) != mustBe)
        {
            throw new DomainException(
                "stock.direction",
                $"Số lượng không hợp lệ cho nghiệp vụ \"{type.DisplayName()}\".");
        }
    }
}
