using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Purchasing;

/// <summary>
/// One product on a goods receipt. Product code/name/unit are snapshotted for printing history.
/// Purchase costs are VND per selling unit.
/// </summary>
public sealed class GoodsReceiptLine : Entity
{
    public const int QuantityDecimals = 3;

    private GoodsReceiptLine()
    {
    }

    internal GoodsReceiptLine(Product product, decimal quantity, decimal unitCost, decimal discountAmount, decimal? newSellPrice)
    {
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        Update(quantity, unitCost, discountAmount, newSellPrice);
    }

    public int GoodsReceiptId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    /// <summary>Supplier's unit price before discounts.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Line discount in VND (chiết khấu dòng).</summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>Optional new shelf price applied to the product when the receipt is completed.</summary>
    public decimal? NewSellPrice { get; private set; }

    /// <summary>Share of the receipt-level discount, allocated when the receipt changes.</summary>
    public decimal AllocatedDiscount { get; private set; }

    public decimal GrossAmount => Money.Round(UnitCost * Quantity);

    public decimal NetAmount => GrossAmount - DiscountAmount;

    /// <summary>What this line finally costs the store, after all discounts.</summary>
    public decimal FinalAmount => NetAmount - AllocatedDiscount;

    /// <summary>Landed unit cost after discounts – the cost that enters the moving average.</summary>
    public decimal EffectiveUnitCost => Money.Round(FinalAmount / Quantity);

    internal void Update(decimal quantity, decimal unitCost, decimal discountAmount, decimal? newSellPrice)
    {
        var rounded = Math.Round(quantity, QuantityDecimals, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new DomainException("receipt.quantity", $"Số lượng nhập của \"{ProductName}\" phải lớn hơn 0.");
        }

        if (rounded > 1_000_000)
        {
            throw new DomainException("receipt.quantity", $"Số lượng nhập của \"{ProductName}\" quá lớn.");
        }

        Guard.NotNegative(unitCost, "receipt.cost", $"Giá nhập của \"{ProductName}\" không được âm.");
        Guard.NotNegative(discountAmount, "receipt.discount", $"Chiết khấu của \"{ProductName}\" không được âm.");
        if (newSellPrice is { } price)
        {
            Guard.NotNegative(price, "receipt.price", $"Giá bán mới của \"{ProductName}\" không được âm.");
        }

        // Validate everything before assigning so a rejected edit leaves the line unchanged.
        var cost = Money.Round(unitCost);
        var discount = Money.Round(discountAmount);
        if (discount > Money.Round(cost * rounded))
        {
            throw new DomainException("receipt.discount", $"Chiết khấu của \"{ProductName}\" vượt quá thành tiền.");
        }

        Quantity = rounded;
        UnitCost = cost;
        DiscountAmount = discount;
        NewSellPrice = newSellPrice is { } p ? Money.Round(p) : null;
    }

    internal void SetAllocatedDiscount(decimal amount) => AllocatedDiscount = amount;
}
