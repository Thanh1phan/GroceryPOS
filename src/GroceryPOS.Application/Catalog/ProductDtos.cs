namespace GroceryPOS.Application.Catalog;

public sealed record ProductListItem(
    int Id,
    string Code,
    string? Barcode,
    string Name,
    int CategoryId,
    string CategoryName,
    string Unit,
    decimal CostPrice,
    decimal SellPrice,
    decimal VatRate,
    decimal StockQuantity,
    decimal MinStockLevel,
    bool IsActive)
{
    public bool IsLowStock => StockQuantity <= MinStockLevel;
}

public sealed record ProductDetail(
    int Id,
    string Code,
    string? Barcode,
    string Name,
    int CategoryId,
    string CategoryName,
    string Unit,
    decimal CostPrice,
    decimal SellPrice,
    decimal VatRate,
    decimal StockQuantity,
    decimal MinStockLevel,
    bool IsActive);

/// <summary>
/// What the product edit form submits. Deliberately has no stock-quantity field: stock is owned by the
/// warehouse/inventory ledger (stock movements) and can never be set from here, by manual edit or by import.
/// </summary>
public sealed record SaveProductRequest(
    string Code,
    string? Barcode,
    string Name,
    int CategoryId,
    string Unit,
    decimal CostPrice,
    decimal SellPrice,
    decimal VatRate,
    decimal MinStockLevel);
