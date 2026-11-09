using GroceryPOS.Application.Catalog;
using Xunit;

namespace GroceryPOS.Application.Tests;

public sealed class PriceMarginTests
{
    [Fact]
    public void No_sell_price_is_neutral()
    {
        var margin = PriceMargin.Of(10_000, 0);

        Assert.Equal(PriceMarginTone.Neutral, margin.Tone);
        Assert.Null(margin.PercentOfSellPrice);
    }

    [Fact]
    public void No_cost_price_is_neutral_and_explains_why()
    {
        var margin = PriceMargin.Of(0, 12_000);

        Assert.Equal(PriceMarginTone.Neutral, margin.Tone);
        Assert.Contains("Chưa có giá vốn", margin.Text);
    }

    [Fact]
    public void Healthy_margin_shows_amount_and_share_of_sell_price()
    {
        var margin = PriceMargin.Of(8_000, 10_000);

        Assert.Equal(PriceMarginTone.Good, margin.Tone);
        Assert.Equal(2_000m, margin.Amount);
        Assert.Equal(20.0m, margin.PercentOfSellPrice);
        Assert.Equal("Lãi gộp 2.000 ₫ mỗi đơn vị (20,0 % giá bán).", margin.Text);
    }

    [Fact]
    public void Margin_under_five_percent_is_thin()
    {
        var margin = PriceMargin.Of(9_700, 10_000);

        Assert.Equal(PriceMarginTone.Thin, margin.Tone);
        Assert.Equal(3.0m, margin.PercentOfSellPrice);
        Assert.StartsWith("Lãi mỏng 300 ₫", margin.Text);
    }

    [Fact]
    public void Exactly_five_percent_is_not_thin()
    {
        Assert.Equal(PriceMarginTone.Good, PriceMargin.Of(9_500, 10_000).Tone);
    }

    [Fact]
    public void Selling_below_cost_is_a_loss()
    {
        var margin = PriceMargin.Of(12_000, 10_000);

        Assert.Equal(PriceMarginTone.Loss, margin.Tone);
        Assert.Equal(-2_000m, margin.Amount);
        Assert.Equal("Bán lỗ 2.000 ₫ mỗi đơn vị (-20,0 %).", margin.Text);
    }

    [Fact]
    public void Selling_at_cost_is_a_loss_without_profit()
    {
        var margin = PriceMargin.Of(10_000, 10_000);

        Assert.Equal(PriceMarginTone.Loss, margin.Tone);
        Assert.Contains("bằng giá vốn", margin.Text);
    }

    [Fact]
    public void Fractional_average_cost_is_rounded_to_whole_dong()
    {
        var margin = PriceMargin.Of(8_333.33m, 10_000);

        Assert.Equal(1_667m, margin.Amount);
        Assert.Equal(16.7m, margin.PercentOfSellPrice);
    }
}
