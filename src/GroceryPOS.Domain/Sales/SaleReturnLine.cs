using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Sales;

/// <summary>One returned product, snapshotted from the invoice line it comes from.</summary>
public sealed class SaleReturnLine : Entity
{
    private SaleReturnLine()
    {
    }

    internal SaleReturnLine(SaleLine line, decimal quantity, decimal refundAmount, bool restock)
    {
        SaleLineId = line.Id;
        ProductId = line.ProductId;
        ProductCode = line.ProductCode;
        ProductName = line.ProductName;
        Unit = line.Unit;
        VatRate = line.VatRate;
        UnitCost = line.UnitCost;
        Quantity = Math.Round(quantity, SaleLine.QuantityDecimals, MidpointRounding.AwayFromZero);
        RefundAmount = refundAmount;
        Restock = restock;
    }

    public int SaleReturnId { get; private set; }
    public int SaleLineId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal VatRate { get; private set; }

    /// <summary>Cost price recorded on the invoice line, used to value restocked or written-off goods.</summary>
    public decimal UnitCost { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Amount refunded for this line, VAT included.</summary>
    public decimal RefundAmount { get; private set; }

    /// <summary>False when the goods are damaged/expired and are written off instead of going back on the shelf.</summary>
    public bool Restock { get; private set; }

    public decimal VatAmount => Money.VatPortionOf(RefundAmount, VatRate);

    public decimal CostAmount => Money.Round(UnitCost * Quantity);
}
