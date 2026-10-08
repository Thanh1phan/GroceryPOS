using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Sales;

/// <summary>A tender received for a sale. A sale may be split across several payments.</summary>
public sealed class Payment : Entity
{
    private Payment()
    {
    }

    internal Payment(PaymentMethod method, decimal amount, string? reference)
    {
        if (!Enum.IsDefined(method))
        {
            throw new DomainException("payment.method", "Phương thức thanh toán không hợp lệ.");
        }

        if (amount <= 0)
        {
            throw new DomainException("payment.amount", "Số tiền thanh toán phải lớn hơn 0.");
        }

        Method = method;
        Amount = Money.Round(amount);
        Reference = Guard.Optional(reference, 100);
    }

    public int SaleId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Bank transaction code / card slip number, if any.</summary>
    public string? Reference { get; private set; }
}
