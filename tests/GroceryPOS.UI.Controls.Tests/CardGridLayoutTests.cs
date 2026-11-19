using GroceryPOS.UI.Controls.Buttons.Core;
using GroceryPOS.UI.Controls.Display.Core;

namespace GroceryPOS.UI.Controls.Tests;

public sealed class CardGridLayoutTests
{
    [Fact]
    public void Wide_enough_row_shows_all_cards_side_by_side_filling_the_width()
    {
        var a = CardGridLayout.Arrange(count: 4, available: 1086, minCardWidth: 200, cardHeight: 128, gap: 16);

        Assert.Equal(4, a.Columns);
        Assert.Equal(1, a.Rows);
        Assert.Equal(128, a.Height);
        Assert.Equal(0, a.Bounds[0].X);
        Assert.Equal(1086, a.Bounds[3].X + a.Bounds[3].Width); // ends exactly at the right edge
        Assert.All(a.Bounds, b => Assert.InRange(b.Width, 259, 260));
    }

    [Fact]
    public void Narrow_row_wraps_instead_of_cutting_cards_off()
    {
        // 4 cards × 200 + gaps = 848 > 600: two rows of two, the row grows taller.
        var a = CardGridLayout.Arrange(4, 600, 200, 128, 16);

        Assert.Equal(2, a.Columns);
        Assert.Equal(2, a.Rows);
        Assert.Equal((2 * 128) + 16, a.Height);
        Assert.Equal(144, a.Bounds[2].Y);
        Assert.Equal(0, a.Bounds[2].X);
        Assert.All(a.Bounds, b => Assert.True(b.Width >= 200));
    }

    [Fact]
    public void Wrapped_rows_are_balanced()
    {
        // 5 cards, room for 4: 3 + 2 rather than 4 + 1.
        var a = CardGridLayout.Arrange(5, 900, 200, 120, 16);

        Assert.Equal(3, a.Columns);
        Assert.Equal(2, a.Rows);
        Assert.Equal(new[] { 0, 0, 0, 136, 136 }, a.Bounds.Select(b => b.Y));
    }

    [Fact]
    public void Too_narrow_for_one_card_still_shows_every_card_one_per_row()
    {
        var a = CardGridLayout.Arrange(3, 150, 200, 100, 10);

        Assert.Equal(1, a.Columns);
        Assert.Equal(3, a.Rows);
        Assert.All(a.Bounds, b => Assert.Equal(150, b.Width));
        Assert.Equal(320, a.Height);
    }

    [Fact]
    public void No_cards_and_negative_input_are_safe()
    {
        Assert.Same(CardGridArrangement.Empty, CardGridLayout.Arrange(0, 800, 200, 128, 16));
        var a = CardGridLayout.Arrange(2, -50, 0, -5, -3);
        Assert.Equal(2, a.Bounds.Count);
        Assert.All(a.Bounds, b => Assert.True(b.Width >= 0 && b.Height >= 0));
    }

    [Theory]
    [InlineData(4, 1000, 16)]
    [InlineData(3, 777, 12)]
    [InlineData(5, 1234, 16)]
    public void Cards_never_overlap_and_keep_the_gap(int count, int available, int gap)
    {
        var a = CardGridLayout.Arrange(count, available, 180, 100, gap);
        foreach (var row in a.Bounds.GroupBy(b => b.Y))
        {
            var cards = row.OrderBy(b => b.X).ToList();
            for (var i = 1; i < cards.Count; i++)
            {
                Assert.Equal(gap, cards[i].X - (cards[i - 1].X + cards[i - 1].Width));
            }

            Assert.True(cards[^1].X + cards[^1].Width <= available);
        }
    }

    [Fact]
    public void Fit_text_picks_the_largest_size_that_fits()
    {
        int Width(float size) => (int)(size * 10);

        Assert.Equal(20f, FitText.Choose([20f, 14f, 11.25f], Width, 250));
        Assert.Equal(14f, FitText.Choose([20f, 14f, 11.25f], Width, 150));
        Assert.Equal(11.25f, FitText.Choose([20f, 14f, 11.25f], Width, 50)); // nothing fits: smallest
        Assert.Throws<ArgumentException>(() => FitText.Choose([], Width, 10));
    }

    [Theory]
    [InlineData(16, 100, 12, 8, 2, 150)] // icon + gap + text + padding both sides + slack
    [InlineData(0, 100, 12, 8, 2, 126)]  // no icon: no gap
    [InlineData(16, 0, 12, 8, 2, 42)]    // icon only: no gap
    [InlineData(-5, -5, -1, -1, -1, 0)]
    public void Button_content_width_matches_how_it_is_painted(int icon, int text, int padding, int gap, int slack, int expected) =>
        Assert.Equal(expected, ButtonMetrics.ContentWidth(icon, text, padding, gap, slack));
}
