using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales;

/// <summary>Member attached to the cart at the counter (looked up by phone).</summary>
public sealed record PosCustomer(int Id, string Code, string Name, string? Phone, int LoyaltyPoints)
{
    public static PosCustomer From(Customer customer) =>
        new(customer.Id, customer.Code, customer.Name, customer.Phone, customer.LoyaltyPoints);
}

/// <summary>One cart row. Amounts follow exactly the same rules as <see cref="SaleLine"/>.</summary>
public sealed class PosCartLine
{
    internal PosCartLine(PosProductDto product, decimal quantity)
    {
        Product = product;
        Quantity = quantity;
    }

    public PosProductDto Product { get; }

    public decimal Quantity { get; internal set; }

    public DiscountInput? Discount { get; internal set; }

    // Flat properties so the cart grid can bind columns by name.
    public string Code => Product.Code;
    public string Name => Product.Name;
    public string Unit => Product.Unit;
    public decimal UnitPrice => Product.SellPrice;

    /// <summary>Price × quantity in whole đồng.</summary>
    public decimal GrossAmount => Money.Round(UnitPrice * Quantity);

    public decimal DiscountAmount => Discount is { IsNone: false } d ? d.ToDomain().AmountOn(GrossAmount) : 0;

    /// <summary>Amount after the line discount, before invoice discount and points.</summary>
    public decimal NetAmount => GrossAmount - DiscountAmount;

    /// <summary>"10%" / "5.000 ₫" / empty, for the grid.</summary>
    public string DiscountText => Discount is { IsNone: false } d ? d.ToDomain().ToString() : string.Empty;

    /// <summary>True when the cart takes the product below zero (shown as a warning; selling is still allowed by default).</summary>
    public bool ExceedsStock { get; internal set; }
}

/// <summary>
/// UI-free state of the POS counter: cart lines, discounts, member, points redemption and tender.
/// The screen only renders this object and forwards keystrokes; <see cref="BuildRequest"/> turns it into the
/// <see cref="CheckoutRequest"/> the checkout use case validates again server-side. Totals mirror
/// <see cref="Sale"/> so what the cashier sees is what gets charged.
/// </summary>
public sealed class PosCart
{
    /// <summary>Denominations used to suggest "khách đưa" amounts (VND notes).</summary>
    private static readonly decimal[] Notes = { 10_000, 20_000, 50_000, 100_000, 200_000, 500_000 };

    private readonly List<PosCartLine> _lines = new();
    private readonly LoyaltyPolicy _loyalty;

    public PosCart(LoyaltyPolicy? loyalty = null)
    {
        _loyalty = loyalty ?? LoyaltyPolicy.Default;
    }

    /// <summary>Raised after any change so the screen can re-render.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<PosCartLine> Lines => _lines;

    public bool IsEmpty => _lines.Count == 0;

    public PosCustomer? Customer { get; private set; }

    public DiscountInput? InvoiceDiscount { get; private set; }

    public int PointsToRedeem { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; } = PaymentMethod.Cash;

    /// <summary>Cash handed over by the customer. Null means "exact amount" (khách đưa đủ).</summary>
    public decimal? CashTendered { get; private set; }

    /// <summary>Transfer/card reference (mã giao dịch), optional.</summary>
    public string? PaymentReference { get; private set; }

    public string? Note { get; private set; }

    public decimal TotalQuantity => _lines.Sum(l => l.Quantity);

    public decimal GrossAmount => _lines.Sum(l => l.GrossAmount);

    public decimal LineDiscountTotal => _lines.Sum(l => l.DiscountAmount);

    public decimal Subtotal => _lines.Sum(l => l.NetAmount);

    public decimal InvoiceDiscountAmount =>
        InvoiceDiscount is { IsNone: false } d ? d.ToDomain().AmountOn(Subtotal) : 0;

    public decimal PointsDiscountAmount => Money.Round(_loyalty.ValueOf(PointsToRedeem));

    public decimal Total => Math.Max(0, Subtotal - InvoiceDiscountAmount - PointsDiscountAmount);

    /// <summary>Everything taken off the gross amount (line + invoice discounts + points).</summary>
    public decimal TotalDiscount => GrossAmount - Total;

    /// <summary>Points the member would earn on this sale.</summary>
    public int PointsToEarn => Customer is null ? 0 : _loyalty.PointsFor(Total);

    /// <summary>Most points the member can redeem now: their balance, capped so points never exceed the amount due.</summary>
    public int MaxRedeemablePoints
    {
        get
        {
            if (Customer is null || _loyalty.PointValueVnd <= 0)
            {
                return 0;
            }

            var cap = (int)Math.Floor((Subtotal - InvoiceDiscountAmount) / _loyalty.PointValueVnd);
            return Math.Max(0, Math.Min(Customer.LoyaltyPoints, cap));
        }
    }

    /// <summary>Amount the customer pays with the selected method (cash: what was handed over).</summary>
    public decimal AmountPaid => PaymentMethod == PaymentMethod.Cash ? CashTendered ?? Total : Total;

    public decimal AmountDue => Math.Max(0, Total - AmountPaid);

    /// <summary>Tiền thừa trả khách – only cash produces change.</summary>
    public decimal Change => PaymentMethod == PaymentMethod.Cash ? Math.Max(0, AmountPaid - Total) : 0;

    public bool HasDiscounts =>
        InvoiceDiscount is { IsNone: false } || _lines.Any(l => l.Discount is { IsNone: false });

    /// <summary>Why the cart cannot be paid yet, or null when <see cref="BuildRequest"/> will succeed.</summary>
    public string? CheckoutBlocker
    {
        get
        {
            if (IsEmpty)
            {
                return "Giỏ hàng đang trống.";
            }

            if (Total <= 0)
            {
                return "Tổng tiền phải lớn hơn 0 ₫.";
            }

            if (AmountDue > 0)
            {
                return $"Khách còn thiếu {Vnd.Format(AmountDue)}.";
            }

            return null;
        }
    }

    public bool CanCheckout => CheckoutBlocker is null;

    // ---- Lines -------------------------------------------------------------------------------

    /// <summary>
    /// Adds a scanned/searched product. Scanning the same product again increases the existing undiscounted
    /// line (like <see cref="Sale.AddItem"/>); a discounted line is never merged into. Returns the affected line.
    /// </summary>
    public PosCartLine Add(PosProductDto product, decimal quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(product);
        var rounded = ValidateQuantity(quantity);

        var line = _lines.FirstOrDefault(l => l.Product.Id == product.Id && l.Discount is not { IsNone: false });
        if (line is null)
        {
            line = new PosCartLine(product, rounded);
            _lines.Add(line);
        }
        else
        {
            line.Quantity = ValidateQuantity(line.Quantity + rounded);
        }

        OnChanged();
        return line;
    }

    public void SetQuantity(PosCartLine line, decimal quantity)
    {
        EnsureOwned(line);
        line.Quantity = ValidateQuantity(quantity);
        OnChanged();
    }

    /// <summary>+1 / −1 from the keypad; going below one unit removes the line.</summary>
    public void Increment(PosCartLine line, decimal step = 1)
    {
        EnsureOwned(line);
        var next = line.Quantity + step;
        if (next <= 0)
        {
            Remove(line);
            return;
        }

        SetQuantity(line, next);
    }

    public void Remove(PosCartLine line)
    {
        EnsureOwned(line);
        _lines.Remove(line);
        OnChanged();
    }

    public void SetLineDiscount(PosCartLine line, DiscountInput? discount)
    {
        EnsureOwned(line);
        line.Discount = Normalize(discount);
        OnChanged();
    }

    // ---- Invoice level -----------------------------------------------------------------------

    public void SetInvoiceDiscount(DiscountInput? discount)
    {
        InvoiceDiscount = Normalize(discount);
        OnChanged();
    }

    public void AttachCustomer(PosCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (Customer?.Id != customer.Id)
        {
            PointsToRedeem = 0;
        }

        Customer = customer;
        OnChanged();
    }

    public void DetachCustomer()
    {
        Customer = null;
        PointsToRedeem = 0;
        OnChanged();
    }

    /// <summary>Uses <paramref name="points"/> of the member's balance as a discount (0 cancels).</summary>
    public void RedeemPoints(int points)
    {
        if (points < 0)
        {
            throw new DomainException("sale.points", "Số điểm đổi không hợp lệ.");
        }

        if (points > 0)
        {
            if (Customer is null)
            {
                throw new DomainException("sale.points", "Vui lòng chọn khách hàng trước khi đổi điểm.");
            }

            if (points > Customer.LoyaltyPoints)
            {
                throw new DomainException("sale.points", $"Khách hàng chỉ có {Customer.LoyaltyPoints} điểm.");
            }

            if (points > MaxRedeemablePoints)
            {
                throw new DomainException(
                    "sale.points", $"Giá trị điểm đổi vượt quá số tiền hóa đơn (tối đa {MaxRedeemablePoints} điểm).");
            }
        }

        PointsToRedeem = points;
        OnChanged();
    }

    public void SetPaymentMethod(PaymentMethod method)
    {
        if (!Enum.IsDefined(method))
        {
            throw new DomainException("payment.method", "Phương thức thanh toán không hợp lệ.");
        }

        PaymentMethod = method;
        OnChanged();
    }

    /// <summary>Cash handed over; null or 0 means "exact amount".</summary>
    public void SetCashTendered(decimal? amount)
    {
        if (amount is < 0)
        {
            throw new DomainException("payment.amount", "Số tiền khách đưa không được âm.");
        }

        CashTendered = amount is null or 0 ? null : Money.Round(amount.Value);
        OnChanged();
    }

    public void SetPaymentReference(string? reference)
    {
        PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        OnChanged();
    }

    public void SetNote(string? note)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        OnChanged();
    }

    /// <summary>Starts a new sale (after checkout or "Hủy đơn").</summary>
    public void Clear()
    {
        _lines.Clear();
        Customer = null;
        InvoiceDiscount = null;
        PointsToRedeem = 0;
        PaymentMethod = PaymentMethod.Cash;
        CashTendered = null;
        PaymentReference = null;
        Note = null;
        OnChanged();
    }

    /// <summary>
    /// Quick "khách đưa" buttons: the exact total followed by the next amounts a customer can hand over with
    /// common notes (e.g. 87.000 → 87.000, 90.000, 100.000, 200.000, 500.000).
    /// </summary>
    public IReadOnlyList<decimal> SuggestCashAmounts(int max = 5) => SuggestCashAmounts(Total, max);

    public static IReadOnlyList<decimal> SuggestCashAmounts(decimal total, int max = 5)
    {
        if (total <= 0 || max <= 0)
        {
            return Array.Empty<decimal>();
        }

        var suggestions = new SortedSet<decimal> { total };
        foreach (var note in Notes)
        {
            suggestions.Add(Math.Ceiling(total / note) * note);
        }

        // Large baskets: next round million too.
        suggestions.Add(Math.Ceiling(total / 1_000_000m) * 1_000_000m);
        return suggestions.Take(max).ToList();
    }

    /// <summary>
    /// Splits the cashier's quantity shortcut off a scan: <c>3*8934673000011</c> → (3, barcode),
    /// <c>1,5*GAO01</c> or <c>1.5*GAO01</c> → (1.5, code). Without a valid prefix the quantity is 1.
    /// </summary>
    public static (decimal Quantity, string Text) ParseScan(string? input)
    {
        var text = input?.Trim() ?? string.Empty;
        var star = text.IndexOf('*');
        if (star > 0 && star < text.Length - 1)
        {
            var number = text[..star].Trim().Replace(',', '.');
            if (decimal.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture, out var quantity) && quantity > 0)
            {
                return (quantity, text[(star + 1)..].Trim());
            }
        }

        return (1, text);
    }

    /// <summary>The request for <see cref="ICheckoutService.CheckoutAsync"/>; fails with the blocker when not payable.</summary>
    public Result<CheckoutRequest> BuildRequest()
    {
        if (CheckoutBlocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        var lines = _lines
            .Select(l => new CheckoutLine(l.Product.Id, l.Quantity, l.Discount))
            .ToList();

        var payment = PaymentMethod == PaymentMethod.Cash
            ? new PaymentInput(PaymentMethod.Cash, AmountPaid)
            : new PaymentInput(PaymentMethod, Total, PaymentReference);

        return new CheckoutRequest(lines, new[] { payment }, Customer?.Id, InvoiceDiscount, PointsToRedeem, Note);
    }

    private void OnChanged()
    {
        // Points can never be worth more than what is left to pay (e.g. after removing a line).
        if (PointsToRedeem > MaxRedeemablePoints)
        {
            PointsToRedeem = MaxRedeemablePoints;
        }

        foreach (var group in _lines.GroupBy(l => l.Product.Id))
        {
            var exceeds = group.Sum(l => l.Quantity) > group.First().Product.StockQuantity;
            foreach (var line in group)
            {
                line.ExceedsStock = exceeds;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureOwned(PosCartLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_lines.Contains(line))
        {
            throw new InvalidOperationException("The line does not belong to this cart.");
        }
    }

    private static decimal ValidateQuantity(decimal quantity)
    {
        var rounded = Math.Round(quantity, SaleLine.QuantityDecimals, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new DomainException("sale.quantity", "Số lượng phải lớn hơn 0.");
        }

        if (rounded > 100_000)
        {
            throw new DomainException("sale.quantity", "Số lượng quá lớn.");
        }

        return rounded;
    }

    private static DiscountInput? Normalize(DiscountInput? discount)
    {
        if (discount is null or { IsNone: true })
        {
            return null;
        }

        // Validates the range (0–100%, not negative) with the domain's own rules and messages.
        var domain = discount.ToDomain();
        return new DiscountInput(domain.Kind, domain.Value);
    }
}
