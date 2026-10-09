using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0901 234 567", "0901234567")]
    [InlineData("+84 901.234.567", "0901234567")]
    [InlineData("84901234567", "0901234567")]
    public void Normalizes_vietnamese_numbers(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.NormalizeOptional(input));

    [Fact]
    public void Blank_is_null_and_garbage_is_rejected()
    {
        Assert.Null(PhoneNumber.NormalizeOptional("  "));
        Assert.Throws<DomainException>(() => PhoneNumber.NormalizeOptional("12345"));
    }
}

public class CustomerTests
{
    [Fact]
    public void Purchase_earns_one_point_per_10k()
    {
        var customer = new Customer("kh001", "Lê Văn C");

        var earned = customer.RecordPurchase(125_000m, LoyaltyPolicy.Default);

        Assert.Equal(12, earned);
        Assert.Equal(12, customer.LoyaltyPoints);
        Assert.Equal(125_000m, customer.TotalSpent);
    }

    [Fact]
    public void Redeem_returns_value_and_cannot_exceed_balance()
    {
        var customer = new Customer("kh001", "Lê Văn C");
        customer.RecordPurchase(200_000m, LoyaltyPolicy.Default);

        Assert.Equal(1_000m, customer.RedeemPoints(10, LoyaltyPolicy.Default));
        Assert.Equal(10, customer.LoyaltyPoints);
        Assert.Throws<DomainException>(() => customer.RedeemPoints(11, LoyaltyPolicy.Default));
    }

    [Fact]
    public void Reversal_never_goes_below_zero()
    {
        var customer = new Customer("kh001", "Lê Văn C");
        customer.RecordPurchase(50_000m, LoyaltyPolicy.Default);

        customer.ReversePurchase(100_000m, LoyaltyPolicy.Default);

        Assert.Equal(0, customer.LoyaltyPoints);
        Assert.Equal(0m, customer.TotalSpent);
    }

    [Fact]
    public void ReverseSale_removes_earned_points_and_refunds_redeemed_points()
    {
        var customer = new Customer("kh002", "Phạm Thị D");
        customer.RecordPurchase(300_000m, LoyaltyPolicy.Default); // 30 points
        customer.RedeemPoints(20, LoyaltyPolicy.Default);         // 10 left
        customer.RecordPurchase(120_000m, LoyaltyPolicy.Default); // +12 → 22

        customer.ReverseSale(120_000m, pointsEarned: 12, pointsRedeemed: 20);

        Assert.Equal(30, customer.LoyaltyPoints);
        Assert.Equal(300_000m, customer.TotalSpent);
        Assert.Throws<DomainException>(() => customer.ReverseSale(0, -1, 0));
    }
}
