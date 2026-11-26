using GroceryPOS.UI.Controls.Grids.Core;

namespace GroceryPOS.UI.Controls.Tests;

public sealed class ToolbarFitTests
{
    [Fact]
    public void One_row_of_buttons_keeps_the_single_row_height() =>
        Assert.Equal(60, ToolbarFit.Height(60, [39, 39, 39], 20));

    [Fact]
    public void Wrapped_buttons_grow_the_toolbar_to_the_lowest_button() =>
        Assert.Equal(98, ToolbarFit.Height(60, [39, 39, 78], 20));

    [Fact]
    public void No_visible_button_keeps_the_minimum()
    {
        Assert.Equal(60, ToolbarFit.Height(60, [], 20));
        Assert.Equal(0, ToolbarFit.Height(-5, [], 20));
    }

    [Fact]
    public void Negative_padding_is_ignored() =>
        Assert.Equal(78, ToolbarFit.Height(60, [78], -4));
}
