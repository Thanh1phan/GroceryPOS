using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Sales;

/// <summary>
/// One product on a sale. Product details and prices are snapshotted so later catalog changes
/// do not alter historical invoices. Prices are VAT-inclusive.
/// </summary>
public sealed class SaleLine : Entity
{
    /// <summary>Quantities support weighed goods (kg) with up to 3 decimals.</summary>
    public const int QuantityDecimals = 3;

    private SaleLine()
    {
    }

    internal SaleLine(Product product, decimal quantity)
    {
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Barcode = product.Barcode;
        Unit = product.Unit;
        UnitPrice = product.SellPrice;
        UnitCost = product.CostPrice;
        VatRate = product.VatRate;
        Discount = Discount.None;
        SetQuantity(quantity);
    }

    public int SaleId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal Quantity { get; private set; }
    public Discount Discount { get; private set; } = Discount.None;

    /// <summary>Share of the invoice-level discount (and points redemption) allocated to this line at completion.</summary>
    public decimal AllocatedDiscount { get; private set; }

    /// <summary>Price × quantity, rounded to whole đồng.</summary>
    public decimal GrossAmount => Money.Round(UnitPrice * Quantity);

    public decimal LineDiscountAmount => Discount.AmountOn(GrossAmount);

    /// <summary>Amount after the line discount (before invoice-level discounts).</summary>
    public decimal NetAmount => GrossAmount - LineDiscountAmount;

    /// <summary>Final amount the customer pays for this line, VAT included.</summary>
    public decimal FinalAmount => NetAmount - AllocatedDiscount;

    public decimal VatAmount => Money.VatPortionOf(FinalAmount, VatRate);

    public decimal CostAmount => Money.Round(UnitCost * Quantity);

    /// <summary>Gross profit excluding VAT: revenue net of VAT minus cost.</summary>
    public decimal Profit => FinalAmount - VatAmount - CostAmount;

    internal void SetQuantity(decimal quantity)
    {
        var rounded = Math.Round(quantity, QuantityDecimals, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new DomainException("sale.quantity", "Số lượng phải lớn hơn 0.");
        }

        if (rounded > 100_000)
        {
            throw new DomainException("sale.quantity", "Số lượng quá lớn.");
        }

        Quantity = rounded;
    }

    internal void SetDiscount(Discount discount) => Discount = discount ?? Discount.None;

    internal void SetAllocatedDiscount(decimal amount) => AllocatedDiscount = amount;
}
