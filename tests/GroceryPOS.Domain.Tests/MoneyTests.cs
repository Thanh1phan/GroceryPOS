using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(108000, 8, 8000)]
    [InlineData(110000, 10, 10000)]
    [InlineData(105000, 5, 5000)]
    [InlineData(50000, 0, 0)]
    [InlineData(15000, 8, 1111)]
    public void VatPortionOf_extracts_vat_from_inclusive_amount(decimal gross, decimal rate, decimal expected) =>
        Assert.Equal(expected, Money.VatPortionOf(gross, rate));

    [Fact]
    public void Allocate_parts_always_sum_to_total()
    {
        var parts = Money.Allocate(10_000m, new[] { 33_333m, 33_333m, 33_334m });

        Assert.Equal(10_000m, parts.Sum());
        Assert.Equal(new[] { 3_333m, 3_333m, 3_334m }, parts);
    }

    [Fact]
    public void Allocate_is_proportional_to_weights()
    {
        var parts = Money.Allocate(3_000m, new[] { 100_000m, 200_000m });

        Assert.Equal(new[] { 1_000m, 2_000m }, parts);
    }

    [Fact]
    public void Allocate_with_zero_weights_returns_zeros()
    {
        var parts = Money.Allocate(5_000m, new[] { 0m, 0m });

        Assert.Equal(new[] { 0m, 0m }, parts);
    }
}
