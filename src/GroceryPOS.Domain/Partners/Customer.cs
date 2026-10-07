using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Partners;

public sealed class Customer : AuditableEntity
{
    private Customer()
    {
    }

    public Customer(string code, string name, string? phone = null)
    {
        Code = Guard.NotEmpty(code, "customer.code", "Mã khách hàng không được để trống", 32).ToUpperInvariant();
        Update(name, phone, email: null, address: null);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public int LoyaltyPoints { get; private set; }
    public decimal TotalSpent { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? phone, string? email, string? address)
    {
        Name = Guard.NotEmpty(name, "customer.name", "Tên khách hàng không được để trống", 200);
        Phone = PhoneNumber.NormalizeOptional(phone);
        Email = Guard.Optional(email, 100);
        Address = Guard.Optional(address);
    }

    /// <summary>Records a completed purchase and awards points; returns the points earned.</summary>
    public int RecordPurchase(decimal paidAmount, LoyaltyPolicy policy)
    {
        Guard.NotNegative(paidAmount, "customer.purchase", "Số tiền mua hàng không hợp lệ.");
        TotalSpent += paidAmount;
        var earned = policy.PointsFor(paidAmount);
        LoyaltyPoints += earned;
        return earned;
    }

    /// <summary>Redeems points for a discount; returns the VND value.</summary>
    public decimal RedeemPoints(int points, LoyaltyPolicy policy)
    {
        if (points <= 0)
        {
            throw new DomainException("customer.points", "Số điểm đổi phải lớn hơn 0.");
        }

        if (points > LoyaltyPoints)
        {
            throw new DomainException("customer.points", $"Khách hàng chỉ có {LoyaltyPoints} điểm.");
        }

        LoyaltyPoints -= points;
        return policy.ValueOf(points);
    }

    /// <summary>Reverses points for a returned purchase (never below zero).</summary>
    public void ReversePurchase(decimal refundedAmount, LoyaltyPolicy policy)
    {
        TotalSpent = Math.Max(0, TotalSpent - refundedAmount);
        LoyaltyPoints = Math.Max(0, LoyaltyPoints - policy.PointsFor(refundedAmount));
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
