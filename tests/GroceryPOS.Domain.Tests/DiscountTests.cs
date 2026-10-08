using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Tests;

public class DiscountTests
{
    [Fact]
    public void Percent_discount_is_rounded_to_whole_dong()
    {
        Assert.Equal(1_235m, Discount.Percent(10).AmountOn(12_345m));
    }

    [Fact]
    public void Amount_discount_is_capped_at_base()
    {
        Assert.Equal(5_000m, Discount.Amount(20_000).AmountOn(5_000m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percent_outside_0_100_is_rejected(decimal percent)
    {
        Assert.Throws<DomainException>(() => Discount.Percent(percent));
    }

    [Fact]
    public void Negative_amount_is_rejected()
    {
        Assert.Throws<DomainException>(() => Discount.Amount(-1));
    }

    [Fact]
    public void Zero_discounts_are_none()
    {
        Assert.True(Discount.Percent(0).IsNone);
        Assert.True(Discount.Amount(0).IsNone);
        Assert.Equal(0m, Discount.None.AmountOn(100_000m));
    }
}
