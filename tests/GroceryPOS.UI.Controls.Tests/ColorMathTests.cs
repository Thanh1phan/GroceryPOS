using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class ColorMathTests
{
    [Theory]
    [InlineData("#15803D", 255, 0x15, 0x80, 0x3D)]
    [InlineData("15803d", 255, 0x15, 0x80, 0x3D)]
    [InlineData("#8015803D", 0x80, 0x15, 0x80, 0x3D)]
    public void FromHex_parses_rgb_and_argb(string hex, int a, int r, int g, int b)
    {
        var color = ColorMath.FromHex(hex);

        Assert.Equal(Color.FromArgb(a, r, g, b).ToArgb(), color.ToArgb());
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void FromHex_rejects_invalid_values(string hex) =>
        Assert.Throws<FormatException>(() => ColorMath.FromHex(hex));

    [Fact]
    public void ToHex_round_trips() =>
        Assert.Equal("#1D4ED8", ColorMath.ToHex(ColorMath.FromHex("#1d4ed8")));

    [Fact]
    public void Blend_interpolates_and_clamps()
    {
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);

        Assert.Equal("#808080", ColorMath.ToHex(ColorMath.Blend(black, white, 0.5)));
        Assert.Equal("#000000", ColorMath.ToHex(ColorMath.Blend(black, white, -1)));
        Assert.Equal("#FFFFFF", ColorMath.ToHex(ColorMath.Blend(black, white, 2)));
    }

    [Fact]
    public void Lighten_and_darken_move_luminance_and_keep_alpha()
    {
        var color = Color.FromArgb(200, 21, 128, 61);

        var lighter = ColorMath.Lighten(color, 0.2);
        var darker = ColorMath.Darken(color, 0.2);

        Assert.True(ColorMath.RelativeLuminance(lighter) > ColorMath.RelativeLuminance(color));
        Assert.True(ColorMath.RelativeLuminance(darker) < ColorMath.RelativeLuminance(color));
        Assert.Equal(200, lighter.A);
        Assert.Equal(200, darker.A);
    }

    [Fact]
    public void ContrastRatio_matches_wcag_reference_values()
    {
        Assert.Equal(21d, ColorMath.ContrastRatio(Color.Black, Color.White), 2);
        Assert.Equal(1d, ColorMath.ContrastRatio(Color.Red, Color.Red), 6);
        // #767676 on white is the classic "just passes AA" grey (4.54:1).
        Assert.Equal(4.54, ColorMath.ContrastRatio(ColorMath.FromHex("#767676"), Color.White), 2);
        Assert.Equal(ColorMath.ContrastRatio(Color.Navy, Color.White), ColorMath.ContrastRatio(Color.White, Color.Navy), 10);
    }

    [Fact]
    public void BestContrast_picks_the_more_readable_color()
    {
        Assert.Equal(Color.White, ColorMath.BestContrast(ColorMath.FromHex("#111827"), Color.White, Color.Black));
        Assert.Equal(Color.Black, ColorMath.BestContrast(ColorMath.FromHex("#FBBF24"), Color.White, Color.Black));
    }
}
