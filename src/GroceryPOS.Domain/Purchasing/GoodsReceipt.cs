using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Purchasing;

/// <summary>
/// A goods receipt from a supplier (phiếu nhập hàng). Entered as a <see cref="GoodsReceiptStatus.Draft"/>
/// and posted with <see cref="Complete"/>, which increases stock, moves each product's cost price to the
/// weighted average and writes <see cref="StockMovementType.GoodsReceipt"/> ledger entries.
/// <para>
/// Amount pipeline: Σ line (cost × quantity − line discount) → <see cref="Subtotal"/> → receipt discount →
/// <see cref="Total"/>. The receipt discount is allocated back to the lines (largest remainder) so each
/// line's landed cost is exact. <see cref="Total"/> − <see cref="PaidAmount"/> becomes supplier debt.
/// </para>
/// </summary>
public sealed class GoodsReceipt : AuditableEntity
{
    private readonly List<GoodsReceiptLine> _lines = new();

    private GoodsReceipt()
    {
    }

    public GoodsReceipt(string code, int supplierId, int createdById, DateTimeOffset createdAt)
    {
        Code = Guard.NotEmpty(code, "receipt.code", "Số phiếu nhập không được để trống", 32).ToUpperInvariant();
        if (createdById <= 0)
        {
            throw new DomainException("receipt.user", "Thiếu thông tin người lập phiếu.");
        }

        ChangeSupplier(supplierId);
        CreatedById = createdById;
        ReceiptDate = createdAt;
        Status = GoodsReceiptStatus.Draft;
    }

    public string Code { get; private set; } = string.Empty;
    public int SupplierId { get; private set; }
    public DateTimeOffset ReceiptDate { get; private set; }
    public GoodsReceiptStatus Status { get; private set; }

    /// <summary>Supplier's own invoice / delivery note number (số hóa đơn NCC).</summary>
    public string? SupplierInvoiceNo { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Receipt-level discount in VND (chiết khấu tổng phiếu).</summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>Amount paid to the supplier on receipt; the rest becomes debt.</summary>
    public decimal PaidAmount { get; private set; }

    public int CreatedById { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int? CompletedById { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int? CancelledById { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

    public bool IsDraft => Status == GoodsReceiptStatus.Draft;
    public decimal TotalQuantity => _lines.Sum(l => l.Quantity);
    public decimal GrossAmount => _lines.Sum(l => l.GrossAmount);
    public decimal LineDiscountTotal => _lines.Sum(l => l.DiscountAmount);

    /// <summary>Amount after line discounts, before the receipt discount.</summary>
    public decimal Subtotal => _lines.Sum(l => l.NetAmount);

    /// <summary>Amount owed to the supplier for this receipt.</summary>
    public decimal Total => Math.Max(0, Subtotal - DiscountAmount);

    /// <summary>Unpaid part (công nợ) – added to the supplier's balance on completion.</summary>
    public decimal DebtAmount => Math.Max(0, Total - PaidAmount);

    public void ChangeSupplier(int supplierId)
    {
        EnsureDraft();
        if (supplierId <= 0)
        {
            throw new DomainException("receipt.supplier", "Vui lòng chọn nhà cung cấp.");
        }

        SupplierId = supplierId;
    }

    public void SetReference(string? supplierInvoiceNo, string? note)
    {
        EnsureDraft();
        SupplierInvoiceNo = Guard.Optional(supplierInvoiceNo, 50);
        Note = Guard.Optional(note);
    }

    public GoodsReceiptLine AddLine(Product product, decimal quantity, decimal unitCost, decimal discountAmount = 0, decimal? newSellPrice = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        EnsureDraft();
        if (product.IsTransient)
        {
            throw new DomainException("receipt.product", "Sản phẩm chưa được lưu.");
        }

        if (!product.IsActive)
        {
            throw new DomainException("receipt.product", $"Sản phẩm \"{product.Name}\" đã ngừng kinh doanh.");
        }

        if (_lines.Any(l => l.ProductId == product.Id))
        {
            throw new DomainException("receipt.duplicate", $"Sản phẩm \"{product.Name}\" đã có trong phiếu nhập.");
        }

        var line = new GoodsReceiptLine(product, quantity, unitCost, discountAmount, newSellPrice);
        _lines.Add(line);
        Recalculate();
        return line;
    }

    public void UpdateLine(int productId, decimal quantity, decimal unitCost, decimal discountAmount = 0, decimal? newSellPrice = null)
    {
        EnsureDraft();
        FindLine(productId).Update(quantity, unitCost, discountAmount, newSellPrice);
        Recalculate();
    }

    public void RemoveLine(int productId)
    {
        EnsureDraft();
        _lines.Remove(FindLine(productId));
        Recalculate();
    }

    public void ClearLines()
    {
        EnsureDraft();
        _lines.Clear();
        Recalculate();
    }

    public void SetDiscount(decimal amount)
    {
        EnsureDraft();
        Guard.NotNegative(amount, "receipt.discount", "Chiết khấu phiếu nhập không được âm.");
        DiscountAmount = Money.Round(amount);
        Recalculate();
    }

    public void SetPaidAmount(decimal amount)
    {
        EnsureDraft();
        PaidAmount = Money.Round(Guard.NotNegative(amount, "receipt.paid", "Số tiền thanh toán không được âm."));
    }

    /// <summary>
    /// Posts the receipt: for every line the product's cost moves to the weighted average of the old stock and
    /// the landed cost, an optional new shelf price is applied, and stock increases through the ledger.
    /// All rules are checked before any product is changed. <paramref name="products"/> must contain every line's product.
    /// </summary>
    public IReadOnlyList<GoodsReceiptPosting> Complete(IReadOnlyDictionary<int, Product> products, DateTimeOffset completedAt, int userId)
    {
        ArgumentNullException.ThrowIfNull(products);
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new DomainException("receipt.empty", "Phiếu nhập chưa có sản phẩm.");
        }

        if (DiscountAmount > Subtotal)
        {
            throw new DomainException("receipt.discount", "Chiết khấu phiếu nhập vượt quá tổng tiền hàng.");
        }

        if (PaidAmount > Total)
        {
            throw new DomainException("receipt.paid", "Số tiền thanh toán vượt quá tổng tiền phiếu nhập.");
        }

        foreach (var line in _lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw new DomainException("receipt.product", $"Không tìm thấy sản phẩm \"{line.ProductName}\".");
            }

            if (!product.IsActive)
            {
                throw new DomainException("receipt.product", $"Sản phẩm \"{product.Name}\" đã ngừng kinh doanh.");
            }
        }

        Recalculate();
        var postings = new List<GoodsReceiptPosting>(_lines.Count);
        foreach (var line in _lines)
        {
            var product = products[line.ProductId];
            var landedCost = line.EffectiveUnitCost;
            var costChange = product.ApplyPurchaseCost(line.Quantity, landedCost);

            PriceChange? sellChange = null;
            if (line.NewSellPrice is { } newPrice && newPrice != product.SellPrice)
            {
                sellChange = product.ChangePrices(product.CostPrice, newPrice);
            }

            var movement = StockMovement.Apply(
                product, StockMovementType.GoodsReceipt, line.Quantity, completedAt, userId, Code, unitCost: landedCost);
            postings.Add(new GoodsReceiptPosting(line, product, movement, costChange, sellChange));
        }

        Status = GoodsReceiptStatus.Completed;
        CompletedAt = completedAt;
        CompletedById = userId;
        return postings;
    }

    /// <summary>Discards a draft. Completed receipts cannot be cancelled – use a supplier return instead.</summary>
    public void Cancel(DateTimeOffset cancelledAt, int userId, string? reason)
    {
        if (Status == GoodsReceiptStatus.Completed)
        {
            throw new DomainException("receipt.completed", "Phiếu nhập đã hoàn thành, không thể hủy. Hãy lập phiếu trả hàng nhà cung cấp.");
        }

        EnsureDraft();
        Status = GoodsReceiptStatus.Cancelled;
        CancelledAt = cancelledAt;
        CancelledById = userId;
        CancelReason = Guard.Optional(reason);
    }

    private GoodsReceiptLine FindLine(int productId) =>
        _lines.FirstOrDefault(l => l.ProductId == productId)
        ?? throw new DomainException("receipt.line", "Sản phẩm không có trong phiếu nhập.");

    private void Recalculate()
    {
        var discount = Math.Min(DiscountAmount, Subtotal);
        var shares = Money.Allocate(discount, _lines.Select(l => l.NetAmount).ToList());
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].SetAllocatedDiscount(shares[i]);
        }
    }

    private void EnsureDraft()
    {
        if (Status != GoodsReceiptStatus.Draft)
        {
            throw new DomainException("receipt.status", $"Phiếu nhập {Code} {Status.DisplayName().ToLowerInvariant()}, không thể thay đổi.");
        }
    }
}

/// <summary>What completing one receipt line did to its product (for audit and the UI summary).</summary>
public sealed record GoodsReceiptPosting(
    GoodsReceiptLine Line,
    Product Product,
    StockMovement Movement,
    PriceChange CostChange,
    PriceChange? SellPriceChange);
