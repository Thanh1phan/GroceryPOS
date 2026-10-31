using System.Drawing;
using GroceryPOS.UI.Controls.Display.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class ChartScaleTests
{
    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(-5, 4, 1)]
    [InlineData(7, 8, 2)]
    [InlineData(1_850_000, 2_000_000, 500_000)]
    [InlineData(12_300_000, 15_000_000, 5_000_000)]
    [InlineData(9_000_000, 10_000_000, 2_500_000)]
    [InlineData(100, 100, 25)]
    [InlineData(0.3, 0.3, 0.1)]
    public void Nice_axis_covers_the_maximum_with_round_steps(decimal max, decimal expectedMax, decimal expectedStep)
    {
        var axis = ChartScale.NiceAxis(max);

        Assert.Equal(expectedMax, axis.Max);
        Assert.Equal(expectedStep, axis.Step);
        Assert.Equal(0, axis.Ticks[0]);
        Assert.Equal(axis.Max, axis.Ticks[^1]);
        Assert.InRange(axis.Ticks.Count, 2, 6);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(500, "500")]
    [InlineData(850_000, "850k")]
    [InlineData(1_250, "1,3k")]
    [InlineData(1_200_000, "1,2tr")]
    [InlineData(15_000_000, "15tr")]
    [InlineData(2_500_000_000, "2,5 tỷ")]
    [InlineData(-300_000, "-300k")]
    [InlineData(123_456_789, "123tr")]
    public void Compact_vnd_labels(decimal amount, string expected) =>
        Assert.Equal(expected, ChartScale.CompactVnd(amount));

    [Fact]
    public void Bars_are_centred_in_equal_slots_and_scaled_to_the_axis()
    {
        var plot = new RectangleF(10, 0, 300, 100);

        var bars = ChartScale.Bars(plot, [50m, 100m, 0m], 100m, barRatio: 0.5f);

        Assert.Equal(3, bars.Count);
        Assert.Equal(new RectangleF(35, 50, 50, 50), bars[0]);
        Assert.Equal(new RectangleF(135, 0, 50, 100), bars[1]);
        Assert.Equal(0, bars[2].Height);
        Assert.Equal(100, bars[2].Bottom);
    }

    [Fact]
    public void Bars_respect_the_maximum_width_and_ignore_negative_values()
    {
        var bars = ChartScale.Bars(new RectangleF(0, 0, 700, 100), [-10m, 20m], 40m, barRatio: 0.8f, maxBarWidth: 40);

        Assert.All(bars, b => Assert.Equal(40, b.Width));
        Assert.Equal(0, bars[0].Height);
        Assert.Equal(50, bars[1].Height);
        Assert.Empty(ChartScale.Bars(new RectangleF(0, 0, 100, 100), [], 10m));
    }

    [Theory]
    [InlineData(50, 30, 1)]
    [InlineData(20, 30, 2)]
    [InlineData(10, 34, 4)]
    public void Labels_are_thinned_out_when_they_would_overlap(float slot, float label, int expected) =>
        Assert.Equal(expected, ChartScale.LabelEvery(slot, label));

    [Fact]
    public void Hit_test_maps_x_to_a_slot()
    {
        var plot = new RectangleF(10, 0, 100, 50);

        Assert.Equal(-1, ChartScale.HitTest(plot, 4, 5));
        Assert.Equal(0, ChartScale.HitTest(plot, 4, 10));
        Assert.Equal(2, ChartScale.HitTest(plot, 4, 60));
        Assert.Equal(3, ChartScale.HitTest(plot, 4, 109.9f));
        Assert.Equal(-1, ChartScale.HitTest(plot, 4, 110));
        Assert.Equal(-1, ChartScale.HitTest(plot, 0, 50));
    }
}
