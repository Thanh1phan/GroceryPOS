using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Catalog;

/// <summary>
/// A sellable item. Prices are VND and VAT-inclusive at the shelf (<see cref="SellPrice"/>);
/// <see cref="VatRate"/> is used to split out the tax portion on receipts and reports.
/// </summary>
public sealed class Product : AuditableEntity
{
    private Product()
    {
    }

    public Product(string code, string name, int categoryId, string unit, decimal costPrice, decimal sellPrice, decimal vatRate = Catalog.VatRate.None)
    {
        Code = Guard.NotEmpty(code, "product.code", "Mã sản phẩm không được để trống", 32).ToUpperInvariant();
        UpdateInfo(name, categoryId, unit);
        ChangePrices(costPrice, sellPrice);
        ChangeVatRate(vatRate);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }

    /// <summary>Selling unit, e.g. "cái", "chai", "hộp", "kg".</summary>
    public string Unit { get; private set; } = string.Empty;

    public decimal CostPrice { get; private set; }
    public decimal SellPrice { get; private set; }
    public decimal VatRate { get; private set; }

    public decimal StockQuantity { get; private set; }
    public decimal MinStockLevel { get; private set; }
    public bool IsActive { get; private set; }

    public bool IsLowStock => StockQuantity <= MinStockLevel;

    /// <summary>Gross margin per unit in VND (sell price minus cost).</summary>
    public decimal UnitMargin => SellPrice - CostPrice;

    /// <summary>VAT amount contained in a VAT-inclusive price.</summary>
    public decimal VatPortionOf(decimal grossAmount) =>
        VatRate == 0 ? 0 : Math.Round(grossAmount * VatRate / (100 + VatRate), 0, MidpointRounding.AwayFromZero);

    public void UpdateInfo(string name, int categoryId, string unit)
    {
        Name = Guard.NotEmpty(name, "product.name", "Tên sản phẩm không được để trống", 200);
        if (categoryId <= 0)
        {
            throw new DomainException("product.category", "Vui lòng chọn danh mục.");
        }

        CategoryId = categoryId;
        Unit = Guard.NotEmpty(unit, "product.unit", "Đơn vị tính không được để trống", 20);
    }

    public void SetBarcode(string? barcode)
    {
        var normalized = Catalog.Barcode.Normalize(barcode);
        if (normalized is not null && !Catalog.Barcode.IsValid(normalized))
        {
            throw new DomainException("product.barcode", "Mã vạch không hợp lệ.");
        }

        Barcode = normalized;
    }

    /// <summary>Changes prices; returns the previous values so callers can audit the change.</summary>
    public PriceChange ChangePrices(decimal costPrice, decimal sellPrice)
    {
        Guard.NotNegative(costPrice, "product.cost", "Giá nhập không được âm.");
        Guard.NotNegative(sellPrice, "product.price", "Giá bán không được âm.");

        var change = new PriceChange(CostPrice, SellPrice, costPrice, sellPrice);
        CostPrice = costPrice;
        SellPrice = sellPrice;
        return change;
    }

    public void ChangeVatRate(decimal vatRate)
    {
        if (!Catalog.VatRate.IsAllowed(vatRate))
        {
            throw new DomainException("product.vat", "Thuế suất VAT không hợp lệ (0, 5, 8 hoặc 10%).");
        }

        VatRate = vatRate;
    }

    public void SetMinStockLevel(decimal level) =>
        MinStockLevel = Guard.NotNegative(level, "product.minstock", "Mức tồn tối thiểu không được âm.");

    /// <summary>
    /// Applies a stock movement (positive = in, negative = out). Stock may not go negative
    /// unless <paramref name="allowNegative"/> is set (e.g. selling before goods receipt is entered).
    /// </summary>
    public void AdjustStock(decimal delta, bool allowNegative = false)
    {
        var next = StockQuantity + delta;
        if (next < 0 && !allowNegative)
        {
            throw new DomainException("product.stock", $"Không đủ tồn kho cho \"{Name}\" (còn {StockQuantity:0.##}).");
        }

        StockQuantity = next;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}

public sealed record PriceChange(decimal OldCostPrice, decimal OldSellPrice, decimal NewCostPrice, decimal NewSellPrice)
{
    public bool HasChanged => OldCostPrice != NewCostPrice || OldSellPrice != NewSellPrice;
}
