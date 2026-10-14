using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Purchasing;

/// <summary>One product to send back to the supplier. <see cref="UnitPrice"/> null = default price.</summary>
public sealed record SupplierReturnItem(int ProductId, decimal Quantity, decimal? UnitPrice = null);

/// <summary>
/// Goods sent back to a supplier (phiếu trả hàng nhà cung cấp, <c>TN{yyMMdd}-{0001}</c>): expired, damaged or
/// slow-moving goods. Created already posted by <see cref="Create"/>, which takes stock out through the ledger
/// and settles the value: <see cref="RefundAmount"/> is money the supplier pays back now, the rest
/// (<see cref="DebtOffset"/>) is deducted from the store's debt to that supplier.
/// <para>
/// A return can reference a completed goods receipt: then only products of that receipt can be returned, at most
/// the received quantity minus earlier returns, priced at most at the landed cost of the receipt line (default).
/// Without a receipt the default price is the product's current cost price.
/// Stock leaves at the current average cost, so the product's cost price does not change; the difference
/// between the return value and the cost value is reported as <see cref="CostDifference"/>.
/// </para>
/// </summary>
public sealed class SupplierReturn : AuditableEntity
{
    private readonly List<SupplierReturnLine> _lines = new();

    private SupplierReturn()
    {
    }

    private SupplierReturn(string code, int supplierId, GoodsReceipt? receipt, DateTimeOffset returnDate, int userId, string reason, string? note)
    {
        Code = code;
        SupplierId = supplierId;
        GoodsReceiptId = receipt?.Id;
        GoodsReceiptCode = receipt?.Code;
        ReturnDate = returnDate;
        UserId = userId;
        Reason = reason;
        Note = note;
    }

    public string Code { get; private set; } = string.Empty;
    public int SupplierId { get; private set; }
    public int? GoodsReceiptId { get; private set; }
    public string? GoodsReceiptCode { get; private set; }
    public DateTimeOffset ReturnDate { get; private set; }
    public int UserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Note { get; private set; }

    /// <summary>Money received back from the supplier now (VND).</summary>
    public decimal RefundAmount { get; private set; }

    /// <summary>How <see cref="RefundAmount"/> was received; null when nothing was refunded.</summary>
    public PaymentMethod? RefundMethod { get; private set; }

    public IReadOnlyList<SupplierReturnLine> Lines => _lines;

    /// <summary>Value of the returned goods at the agreed return prices.</summary>
    public decimal Total => _lines.Sum(l => l.Amount);

    /// <summary>Part of <see cref="Total"/> deducted from the supplier debt (cấn trừ công nợ).</summary>
    public decimal DebtOffset => Total - RefundAmount;

    /// <summary>Value of the returned goods at the average cost they left stock with.</summary>
    public decimal CostValue => _lines.Sum(l => l.CostValue);

    /// <summary>Return value minus cost value: positive = the supplier credits more than the stock was worth.</summary>
    public decimal CostDifference => Total - CostValue;

    /// <summary>
    /// Validates every rule first, then takes the goods out of stock (<see cref="StockMovementType.SupplierReturn"/>
    /// entries, returned for the caller to add to the unit of work) and offsets the supplier debt.
    /// </summary>
    /// <param name="alreadyReturned">Quantities per product already returned against <paramref name="receipt"/>.</param>
    /// <param name="products">Must contain every item's product.</param>
    public static SupplierReturnPosting Create(
        string code,
        Supplier supplier,
        GoodsReceipt? receipt,
        IReadOnlyDictionary<int, decimal> alreadyReturned,
        IReadOnlyList<SupplierReturnItem> items,
        IReadOnlyDictionary<int, Product> products,
        decimal refundAmount,
        PaymentMethod? refundMethod,
        string? reason,
        string? note,
        DateTimeOffset returnDate,
        int userId)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(alreadyReturned);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(products);
        code = Guard.NotEmpty(code, "supplier_return.code", "Số phiếu trả hàng không được để trống", 32).ToUpperInvariant();
        var trimmedReason = Guard.NotEmpty(reason, "supplier_return.reason", "Vui lòng nhập lý do trả hàng.", 200);
        if (supplier.IsTransient)
        {
            throw new DomainException("supplier_return.supplier", "Nhà cung cấp chưa được lưu.");
        }

        if (userId <= 0)
        {
            throw new DomainException("supplier_return.user", "Thiếu thông tin người lập phiếu.");
        }

        if (receipt is not null)
        {
            if (receipt.Status != GoodsReceiptStatus.Completed)
            {
                throw new DomainException("supplier_return.receipt", $"Phiếu nhập {receipt.Code} chưa hoàn thành, không thể trả hàng theo phiếu này.");
            }

            if (receipt.SupplierId != supplier.Id)
            {
                throw new DomainException("supplier_return.receipt", $"Phiếu nhập {receipt.Code} không thuộc nhà cung cấp \"{supplier.Name}\".");
            }
        }

        if (items.Count == 0)
        {
            throw new DomainException("supplier_return.empty", "Vui lòng chọn sản phẩm cần trả.");
        }

        var result = new SupplierReturn(code, supplier.Id, receipt, returnDate, userId, trimmedReason, Guard.Optional(note));

        // 1. Build and check every line before any product is touched.
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                throw new DomainException("supplier_return.product", $"Không tìm thấy sản phẩm (mã #{item.ProductId}).");
            }

            if (result._lines.Any(l => l.ProductId == product.Id))
            {
                throw new DomainException("supplier_return.duplicate", $"Sản phẩm \"{product.Name}\" bị lặp trong phiếu trả hàng.");
            }

            var quantity = Math.Round(item.Quantity, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero);
            if (quantity <= 0)
            {
                throw new DomainException("supplier_return.quantity", $"Số lượng trả của \"{product.Name}\" phải lớn hơn 0.");
            }

            decimal unitPrice;
            if (receipt is not null)
            {
                var receiptLine = receipt.Lines.FirstOrDefault(l => l.ProductId == product.Id)
                    ?? throw new DomainException("supplier_return.product", $"Sản phẩm \"{product.Name}\" không có trong phiếu nhập {receipt.Code}.");
                var remaining = receiptLine.Quantity - alreadyReturned.GetValueOrDefault(product.Id);
                if (quantity > remaining)
                {
                    throw new DomainException(
                        "supplier_return.quantity",
                        $"\"{product.Name}\" chỉ còn có thể trả tối đa {remaining:0.###} {receiptLine.Unit} theo phiếu nhập {receipt.Code}.");
                }

                unitPrice = item.UnitPrice ?? receiptLine.EffectiveUnitCost;
                if (unitPrice > receiptLine.EffectiveUnitCost)
                {
                    throw new DomainException(
                        "supplier_return.price",
                        $"Giá trả của \"{product.Name}\" không được cao hơn giá nhập {Money.Format(receiptLine.EffectiveUnitCost)}.");
                }
            }
            else
            {
                unitPrice = item.UnitPrice ?? product.CostPrice;
            }

            if (unitPrice < 0 || unitPrice != Money.Round(unitPrice))
            {
                throw new DomainException("supplier_return.price", $"Giá trả của \"{product.Name}\" không hợp lệ.");
            }

            if (quantity > product.StockQuantity)
            {
                throw new DomainException(
                    "supplier_return.stock",
                    $"Không đủ tồn kho để trả \"{product.Name}\" (tồn {product.StockQuantity:0.###} {product.Unit}).");
            }

            result._lines.Add(new SupplierReturnLine(product, quantity, unitPrice));
        }

        // 2. Settlement: refund now and/or deduct from debt.
        refundAmount = Money.Round(refundAmount);
        if (refundAmount < 0 || refundAmount > result.Total)
        {
            throw new DomainException("supplier_return.refund", "Số tiền nhà cung cấp hoàn lại không được âm hoặc vượt quá giá trị hàng trả.");
        }

        if (refundAmount > 0)
        {
            if (refundMethod is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
            {
                throw new DomainException("supplier_return.method", "Vui lòng chọn hình thức nhận tiền (tiền mặt hoặc chuyển khoản).");
            }

            result.RefundAmount = refundAmount;
            result.RefundMethod = refundMethod;
        }

        if (result.DebtOffset > supplier.DebtBalance)
        {
            throw new DomainException(
                "supplier_return.offset",
                $"Công nợ hiện tại với \"{supplier.Name}\" chỉ còn {Money.Format(supplier.DebtBalance)}. "
                + $"Phần còn lại ({Money.Format(result.DebtOffset - supplier.DebtBalance)}) cần nhận lại bằng tiền.");
        }

        // 3. Apply.
        var movements = new List<StockMovement>(result._lines.Count);
        foreach (var line in result._lines)
        {
            var product = products[line.ProductId];
            movements.Add(StockMovement.Apply(
                product,
                StockMovementType.SupplierReturn,
                -line.Quantity,
                returnDate,
                userId,
                code,
                note: trimmedReason,
                unitCost: line.CostPrice));
        }

        if (result.DebtOffset > 0)
        {
            supplier.OffsetDebt(result.DebtOffset);
        }

        return new SupplierReturnPosting(result, movements);
    }
}

/// <summary>A returned product with the price agreed with the supplier and the cost it left stock with.</summary>
public sealed class SupplierReturnLine : Entity
{
    private SupplierReturnLine()
    {
    }

    internal SupplierReturnLine(Product product, decimal quantity, decimal unitPrice)
    {
        ProductId = product.Id;
        ProductCode = product.Code;
        ProductName = product.Name;
        Unit = product.Unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CostPrice = product.CostPrice;
    }

    public int SupplierReturnId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    /// <summary>Price per unit the supplier credits (VND).</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Average cost per unit when the goods left stock (VND).</summary>
    public decimal CostPrice { get; private set; }

    public decimal Amount => Money.Round(Quantity * UnitPrice);

    public decimal CostValue => Money.Round(Quantity * CostPrice);
}

/// <summary>Outcome of <see cref="SupplierReturn.Create"/>: the return and its ledger entries.</summary>
public sealed record SupplierReturnPosting(SupplierReturn Return, IReadOnlyList<StockMovement> Movements);
