using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Purchasing;

/// <summary>
/// A payment of supplier debt (phiếu chi trả nợ nhà cung cấp, <c>PC{yyMMdd}-{0001}</c>).
/// Created with <see cref="Pay"/>, which reduces <see cref="Supplier.DebtBalance"/>; a mistaken payment is
/// reversed with <see cref="Cancel"/> (the debt comes back) – payments are never edited or deleted.
/// </summary>
public sealed class SupplierPayment : Entity
{
    private SupplierPayment()
    {
    }

    private SupplierPayment(
        string code,
        Supplier supplier,
        decimal amount,
        PaymentMethod method,
        DateTimeOffset paidAt,
        int userId,
        string? reference,
        string? note)
    {
        Code = code;
        SupplierId = supplier.Id;
        Amount = amount;
        Method = method;
        PaidAt = paidAt;
        UserId = userId;
        Reference = Guard.Optional(reference, 50);
        Note = Guard.Optional(note);
        DebtBefore = supplier.DebtBalance + amount;
        DebtAfter = supplier.DebtBalance;
    }

    public string Code { get; private set; } = string.Empty;
    public int SupplierId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public int UserId { get; private set; }

    /// <summary>Bank transaction number or the supplier's receipt number.</summary>
    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Supplier balance just before / after this payment (for the printed slip).</summary>
    public decimal DebtBefore { get; private set; }

    public decimal DebtAfter { get; private set; }

    public bool IsCancelled { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int? CancelledById { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Pays <paramref name="amount"/> of the supplier's debt in cash or by bank transfer.</summary>
    public static SupplierPayment Pay(
        string code,
        Supplier supplier,
        decimal amount,
        PaymentMethod method,
        DateTimeOffset paidAt,
        int userId,
        string? reference = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        code = Guard.NotEmpty(code, "supplier_payment.code", "Số phiếu chi không được để trống", 32).ToUpperInvariant();
        if (supplier.IsTransient)
        {
            throw new DomainException("supplier_payment.supplier", "Nhà cung cấp chưa được lưu.");
        }

        if (userId <= 0)
        {
            throw new DomainException("supplier_payment.user", "Thiếu thông tin người lập phiếu.");
        }

        if (method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
        {
            throw new DomainException("supplier_payment.method", "Chỉ hỗ trợ trả nợ nhà cung cấp bằng tiền mặt hoặc chuyển khoản.");
        }

        if (amount != Money.Round(amount))
        {
            throw new DomainException("supplier_payment.amount", "Số tiền thanh toán phải là số nguyên đồng.");
        }

        supplier.PayDebt(amount);
        return new SupplierPayment(code, supplier, amount, method, paidAt, userId, reference, note);
    }

    /// <summary>Reverses a mistaken payment: the amount is added back to the supplier's debt.</summary>
    public void Cancel(Supplier supplier, DateTimeOffset cancelledAt, int userId, string? reason)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        if (supplier.Id != SupplierId)
        {
            throw new DomainException("supplier_payment.supplier", "Phiếu chi không thuộc nhà cung cấp này.");
        }

        if (IsCancelled)
        {
            throw new DomainException("supplier_payment.cancelled", $"Phiếu chi {Code} đã bị hủy trước đó.");
        }

        if (cancelledAt < PaidAt)
        {
            throw new DomainException("supplier_payment.time", "Thời điểm hủy không hợp lệ.");
        }

        var trimmedReason = Guard.NotEmpty(reason, "supplier_payment.reason", "Vui lòng nhập lý do hủy phiếu chi.", 500);
        supplier.AddDebt(Amount);
        IsCancelled = true;
        CancelledAt = cancelledAt;
        CancelledById = userId;
        CancelReason = trimmedReason;
    }
}
