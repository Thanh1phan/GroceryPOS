using GroceryPOS.UI.Controls.Inputs.Core;
using Xunit;

namespace GroceryPOS.UI.Controls.Tests;

public class SegmentLayoutTests
{
    [Fact]
    public void Short_labels_get_equal_segments_that_fill_the_width()
    {
        var segments = SegmentLayout.Arrange([40, 60], 300, 12);

        Assert.Equal([(0, 150), (150, 150)], segments);
    }

    [Fact]
    public void Rounding_is_absorbed_so_segments_never_leave_a_gap()
    {
        var segments = SegmentLayout.Arrange([10, 10, 10], 100, 4);

        Assert.Equal(0, segments[0].X);
        Assert.Equal(100, segments[^1].X + segments[^1].Width);
        for (var i = 1; i < segments.Count; i++)
        {
            Assert.Equal(segments[i - 1].X + segments[i - 1].Width, segments[i].X);
        }
    }

    [Fact]
    public void A_long_label_gets_a_wider_segment_when_equal_widths_would_cut_it()
    {
        var segments = SegmentLayout.Arrange([40, 200], 300, 10);

        Assert.True(segments[1].Width > segments[0].Width);
        Assert.Equal(300, segments[0].Width + segments[1].Width);
        Assert.Equal(60 * 300 / 280, segments[0].Width, tolerance: 1);
    }

    [Fact]
    public void Empty_or_invalid_input()
    {
        Assert.Empty(SegmentLayout.Arrange([], 200, 8));
        Assert.Throws<ArgumentException>(() => SegmentLayout.Arrange([-1], 200, 8));
        Assert.Equal([(0, 0)], SegmentLayout.Arrange([30], -5, 8));
    }

    [Fact]
    public void Preferred_width_shows_every_label_at_equal_width()
    {
        Assert.Equal(2 * (120 + 24), SegmentLayout.PreferredWidth([80, 120], 12));
        Assert.Equal(0, SegmentLayout.PreferredWidth([], 12));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(149, 0)]
    [InlineData(150, 1)]
    [InlineData(299, 1)]
    [InlineData(300, -1)]
    [InlineData(-1, -1)]
    public void Hit_test_finds_the_segment_under_the_pointer(int x, int expected)
    {
        var segments = SegmentLayout.Arrange([40, 60], 300, 12);

        Assert.Equal(expected, SegmentLayout.HitTest(segments, x));
    }

    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 2)]
    [InlineData(0, -1, 3, 0)]
    [InlineData(-1, 1, 3, 0)]
    [InlineData(-1, -1, 3, 2)]
    [InlineData(1, 1, 0, -1)]
    public void Keyboard_steps_stop_at_the_ends(int selected, int delta, int count, int expected) =>
        Assert.Equal(expected, SegmentLayout.Step(selected, delta, count));
}
