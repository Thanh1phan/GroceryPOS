using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Domain.Sales;

/// <summary>
/// A sales invoice (hóa đơn bán hàng). Built at the counter while <see cref="SaleStatus.Draft"/>,
/// then completed once fully paid. All amounts are VND, VAT-inclusive.
/// <para>
/// Amount pipeline: Σ line gross → line discounts → <see cref="Subtotal"/> → invoice discount →
/// loyalty points → <see cref="Total"/>. Invoice-level discounts are allocated back to lines
/// (largest remainder) so per-line VAT and profit stay exact.
/// </para>
/// Stock and customer points are applied by the application layer when the sale completes.
/// </summary>
public sealed class Sale : AuditableEntity
{
    private readonly List<SaleLine> _lines = new();
    private readonly List<Payment> _payments = new();

    private Sale()
    {
    }

    public Sale(string code, int cashierId, DateTimeOffset createdAt)
    {
        Code = Guard.NotEmpty(code, "sale.code", "Số hóa đơn không được để trống", 32).ToUpperInvariant();
        if (cashierId <= 0)
        {
            throw new DomainException("sale.cashier", "Thiếu thông tin thu ngân.");
        }

        CashierId = cashierId;
        SaleDate = createdAt;
        Status = SaleStatus.Draft;
        InvoiceDiscount = Discount.None;
    }

    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset SaleDate { get; private set; }
    public int CashierId { get; private set; }
    public int? CustomerId { get; private set; }
    public Customer? Customer { get; private set; }
    public SaleStatus Status { get; private set; }
    public string? Note { get; private set; }

    public Discount InvoiceDiscount { get; private set; } = Discount.None;
    public int PointsRedeemed { get; private set; }
    public decimal PointsDiscountAmount { get; private set; }
    public int PointsEarned { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public int? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    public IReadOnlyList<SaleLine> Lines => _lines;
    public IReadOnlyList<Payment> Payments => _payments;

    public bool IsDraft => Status == SaleStatus.Draft;
    public bool IsEmpty => _lines.Count == 0;
    public decimal TotalQuantity => _lines.Sum(l => l.Quantity);

    /// <summary>Σ line gross amounts (price × quantity).</summary>
    public decimal GrossAmount => _lines.Sum(l => l.GrossAmount);

    public decimal LineDiscountTotal => _lines.Sum(l => l.LineDiscountAmount);

    /// <summary>Amount after line discounts, before invoice-level discounts.</summary>
    public decimal Subtotal => _lines.Sum(l => l.NetAmount);

    public decimal InvoiceDiscountAmount => InvoiceDiscount.AmountOn(Subtotal);

    /// <summary>All discounts combined (line + invoice + points).</summary>
    public decimal TotalDiscount => GrossAmount - Total;

    /// <summary>Amount the customer must pay.</summary>
    public decimal Total => Math.Max(0, Subtotal - InvoiceDiscountAmount - PointsDiscountAmount);

    public decimal AmountPaid => _payments.Sum(p => p.Amount);
    public decimal CashPaid => _payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
    public decimal NonCashPaid => AmountPaid - CashPaid;

    /// <summary>Amount still owed (0 when fully paid).</summary>
    public decimal AmountDue => Math.Max(0, Total - AmountPaid);

    /// <summary>Tiền thừa trả khách – change is only ever given back in cash.</summary>
    public decimal ChangeDue => Math.Max(0, AmountPaid - Total);

    public decimal VatTotal => _lines.Sum(l => l.VatAmount);
    public decimal CostTotal => _lines.Sum(l => l.CostAmount);
    public decimal Profit => _lines.Sum(l => l.Profit);

    /// <summary>Adds a product, merging into an existing undiscounted line for the same product and price.</summary>
    public SaleLine AddItem(Product product, decimal quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(product);
        EnsureDraft();
        if (product.IsTransient)
        {
            throw new DomainException("sale.product", "Sản phẩm chưa được lưu.");
        }

        if (!product.IsActive)
        {
            throw new DomainException("sale.product", $"Sản phẩm \"{product.Name}\" đã ngừng kinh doanh.");
        }

        var existing = _lines.FirstOrDefault(l =>
            l.ProductId == product.Id && l.Discount.IsNone && l.UnitPrice == product.SellPrice);
        if (existing is not null)
        {
            existing.SetQuantity(existing.Quantity + quantity);
            Recalculate();
            return existing;
        }

        var line = new SaleLine(product, quantity);
        _lines.Add(line);
        Recalculate();
        return line;
    }

    public void ChangeQuantity(SaleLine line, decimal quantity)
    {
        EnsureDraft();
        EnsureOwned(line);
        line.SetQuantity(quantity);
        Recalculate();
    }

    public void RemoveLine(SaleLine line)
    {
        EnsureDraft();
        EnsureOwned(line);
        _lines.Remove(line);
        Recalculate();
    }

    public void SetLineDiscount(SaleLine line, Discount discount)
    {
        EnsureDraft();
        EnsureOwned(line);
        line.SetDiscount(discount);
        Recalculate();
    }

    public void SetInvoiceDiscount(Discount discount)
    {
        EnsureDraft();
        InvoiceDiscount = discount ?? Discount.None;
        Recalculate();
    }

    public void AttachCustomer(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        EnsureDraft();
        if (!customer.IsActive)
        {
            throw new DomainException("sale.customer", "Khách hàng đã ngừng hoạt động.");
        }

        if (CustomerId != customer.Id)
        {
            ClearPointsRedemption();
        }

        Customer = customer;
        CustomerId = customer.Id;
    }

    public void DetachCustomer()
    {
        EnsureDraft();
        ClearPointsRedemption();
        Customer = null;
        CustomerId = null;
    }

    /// <summary>
    /// Reserves loyalty points as a discount on this sale. Points are deducted from the customer
    /// by the application layer when the sale completes.
    /// </summary>
    public void RedeemPoints(int points, LoyaltyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        EnsureDraft();
        if (CustomerId is null)
        {
            throw new DomainException("sale.points", "Vui lòng chọn khách hàng trước khi đổi điểm.");
        }

        if (points < 0)
        {
            throw new DomainException("sale.points", "Số điểm đổi không hợp lệ.");
        }

        if (Customer is not null && points > Customer.LoyaltyPoints)
        {
            throw new DomainException("sale.points", $"Khách hàng chỉ có {Customer.LoyaltyPoints} điểm.");
        }

        var value = Money.Round(policy.ValueOf(points));
        if (value > Subtotal - InvoiceDiscountAmount)
        {
            throw new DomainException("sale.points", "Giá trị điểm đổi vượt quá số tiền hóa đơn.");
        }

        PointsRedeemed = points;
        PointsDiscountAmount = value;
        Recalculate();
    }

    public void ClearPointsRedemption()
    {
        EnsureDraft();
        PointsRedeemed = 0;
        PointsDiscountAmount = 0;
        Recalculate();
    }

    public void SetNote(string? note) => Note = Guard.Optional(note, 500);

    public Payment AddPayment(PaymentMethod method, decimal amount, string? reference = null)
    {
        EnsureDraft();
        var payment = new Payment(method, amount, reference);
        _payments.Add(payment);
        return payment;
    }

    public void ClearPayments()
    {
        EnsureDraft();
        _payments.Clear();
    }

    /// <summary>Finalizes the sale. Requires at least one line and full payment; non-cash tenders cannot exceed the total.</summary>
    public void Complete(DateTimeOffset now, LoyaltyPolicy loyalty)
    {
        ArgumentNullException.ThrowIfNull(loyalty);
        EnsureDraft();
        if (IsEmpty)
        {
            throw new DomainException("sale.empty", "Hóa đơn chưa có sản phẩm.");
        }

        if (PointsDiscountAmount > Subtotal - InvoiceDiscountAmount)
        {
            throw new DomainException("sale.points", "Giá trị điểm đổi vượt quá số tiền hóa đơn. Vui lòng đổi lại điểm.");
        }

        if (AmountPaid < Total)
        {
            throw new DomainException("sale.unpaid", $"Khách còn thiếu {AmountDue:#,0} ₫.");
        }

        if (NonCashPaid > Total)
        {
            throw new DomainException("sale.overpaid", "Số tiền chuyển khoản/thẻ không được vượt quá tổng tiền hóa đơn.");
        }

        Recalculate();
        PointsEarned = CustomerId is null ? 0 : loyalty.PointsFor(Total);
        Status = SaleStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>Cancels a completed sale (manager action). Stock and points are reversed by the application layer.</summary>
    public void Void(DateTimeOffset now, int userId, string reason)
    {
        if (Status != SaleStatus.Completed)
        {
            throw new DomainException("sale.void", "Chỉ có thể hủy hóa đơn đã hoàn tất.");
        }

        VoidReason = Guard.NotEmpty(reason, "sale.void_reason", "Vui lòng nhập lý do hủy hóa đơn", 500);
        VoidedBy = userId;
        VoidedAt = now;
        Status = SaleStatus.Voided;
    }

    /// <summary>VAT summary per rate for the receipt and the VAT report.</summary>
    public IReadOnlyList<VatSummary> VatBreakdown() =>
        _lines
            .GroupBy(l => l.VatRate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var amount = g.Sum(l => l.FinalAmount);
                var vat = g.Sum(l => l.VatAmount);
                return new VatSummary(g.Key, amount, vat, amount - vat);
            })
            .ToList();

    /// <summary>Spreads invoice discount + points over lines in proportion to their net amount.</summary>
    private void Recalculate()
    {
        var invoiceLevel = Math.Min(Subtotal, InvoiceDiscountAmount + PointsDiscountAmount);
        var shares = Money.Allocate(invoiceLevel, _lines.Select(l => l.NetAmount).ToList());
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].SetAllocatedDiscount(shares[i]);
        }
    }

    private void EnsureDraft()
    {
        if (!IsDraft)
        {
            throw new DomainException("sale.locked", "Hóa đơn đã hoàn tất, không thể chỉnh sửa.");
        }
    }

    private void EnsureOwned(SaleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_lines.Contains(line))
        {
            throw new DomainException("sale.line", "Dòng hàng không thuộc hóa đơn này.");
        }
    }
}

/// <summary>Per-VAT-rate totals: amount including VAT, VAT portion and amount excluding VAT.</summary>
public sealed record VatSummary(decimal VatRate, decimal AmountInclVat, decimal VatAmount, decimal AmountExclVat);
