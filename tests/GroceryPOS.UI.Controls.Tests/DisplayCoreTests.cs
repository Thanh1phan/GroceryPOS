using System.Drawing;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class DisplayCoreTests
{
    public static TheoryData<ThemeMode, BadgeKind, BadgeStyle> AllBadges()
    {
        var data = new TheoryData<ThemeMode, BadgeKind, BadgeStyle>();
        foreach (var mode in Enum.GetValues<ThemeMode>())
        {
            foreach (var kind in Enum.GetValues<BadgeKind>())
            {
                foreach (var style in Enum.GetValues<BadgeStyle>())
                {
                    data.Add(mode, kind, style);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllBadges))]
    public void Badge_text_is_readable_in_every_theme(ThemeMode mode, BadgeKind kind, BadgeStyle style)
    {
        var theme = Themes.For(mode);
        var colors = BadgePalette.Resolve(theme, kind, style);

        Assert.True(ColorMath.ContrastRatio(colors.Foreground, colors.Background) >= 4.5, $"{mode}/{kind}/{style}");
    }

    [Fact]
    public void Soft_badge_is_a_tint_of_its_accent_and_solid_badge_uses_the_accent()
    {
        var theme = Themes.Light;
        var soft = BadgePalette.Resolve(theme, BadgeKind.Danger, BadgeStyle.Soft);
        var solid = BadgePalette.Resolve(theme, BadgeKind.Danger, BadgeStyle.Solid);

        Assert.Equal(theme.Colors.Danger, solid.Background);
        Assert.Equal(theme.Colors.OnDanger, solid.Foreground);
        Assert.NotEqual(theme.Colors.Danger, soft.Background);
        Assert.True(ColorMath.RelativeLuminance(soft.Background) > ColorMath.RelativeLuminance(theme.Colors.Danger));
    }

    [Fact]
    public void Soft_badge_blends_from_the_surface_it_sits_on()
    {
        var theme = Themes.Light;
        var onSurface = BadgePalette.Resolve(theme, BadgeKind.Success, BadgeStyle.Soft);
        var onBackground = BadgePalette.Resolve(theme, BadgeKind.Success, BadgeStyle.Soft, theme.Colors.Background);

        Assert.NotEqual(onSurface.Background, onBackground.Background);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-3, "")]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void Badge_count_text(int count, string expected) => Assert.Equal(expected, BadgeText.Count(count));

    [Fact]
    public void Badge_count_honours_a_custom_maximum()
    {
        Assert.Equal("9+", BadgeText.Count(12, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => BadgeText.Count(1, 0));
    }

    [Theory]
    [InlineData("Nguyễn Văn An", "NA")]
    [InlineData("  trần   thị   bích  ", "TB")]
    [InlineData("Đỗ Hương", "ĐH")]
    [InlineData("Ông Ích Khiêm", "ÔK")]
    [InlineData("admin", "AD")]
    [InlineData("A", "A")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    [InlineData("(Thu ngân) Lê Minh", "TM")]
    public void Initials_follow_vietnamese_names(string? name, string expected) => Assert.Equal(expected, Avatar.Initials(name));

    [Fact]
    public void Initials_compose_decomposed_accents()
    {
        var decomposed = "Ến Lý".Normalize(System.Text.NormalizationForm.FormD);

        Assert.Equal("ẾL", Avatar.Initials(decomposed));
    }

    [Fact]
    public void Avatar_color_is_stable_and_case_insensitive()
    {
        Assert.Equal(Avatar.ColorFor("thungan01"), Avatar.ColorFor("THUNGAN01"));
        Assert.Equal(Avatar.ColorFor("admin"), Avatar.ColorFor(" admin "));
        Assert.Contains(Avatar.ColorFor(null), Avatar.Palette);

        var spread = new[] { "admin", "quanly", "thungan01", "thungan02", "thukho", "an", "binh", "chi" }
            .Select(Avatar.ColorFor)
            .Distinct()
            .Count();
        Assert.True(spread >= 3);
    }

    [Fact]
    public void Avatar_palette_keeps_white_initials_readable()
    {
        foreach (var color in Avatar.Palette)
        {
            Assert.True(ColorMath.ContrastRatio(Avatar.Foreground, color) >= 4.5, ColorMath.ToHex(color));
        }
    }

    [Fact]
    public void Trend_up_is_green_when_higher_is_better()
    {
        var trend = Trend.Of(12.5m, comparedTo: "hôm qua");

        Assert.Equal("▲ 12,5%", trend.Text);
        Assert.Equal(BadgeKind.Success, trend.Kind);
        Assert.Equal("Tăng 12,5% so với hôm qua", trend.Tooltip);
    }

    [Fact]
    public void Trend_down_is_red_and_rounds_to_one_decimal()
    {
        var trend = Trend.Of(-3.25m);

        Assert.Equal("▼ 3,3%", trend.Text);
        Assert.Equal(BadgeKind.Danger, trend.Kind);
    }

    [Fact]
    public void Trend_colors_flip_when_growth_is_bad()
    {
        Assert.Equal(BadgeKind.Danger, Trend.Of(40m, higherIsBetter: false).Kind);
        Assert.Equal(BadgeKind.Success, Trend.Of(-40m, higherIsBetter: false).Kind);
    }

    [Fact]
    public void Trend_groups_thousands_and_handles_flat_or_missing_values()
    {
        Assert.Equal("▲ 1.250%", Trend.Of(1250m).Text);
        Assert.Equal("● 0%", Trend.Of(0.04m).Text);
        Assert.Equal(BadgeKind.Neutral, Trend.Of(0m).Kind);

        var missing = Trend.Of(null, comparedTo: "hôm qua");
        Assert.Equal("—", missing.Text);
        Assert.Equal(BadgeKind.Neutral, missing.Kind);
        Assert.Contains("hôm qua", missing.Tooltip);
    }

    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void Card_and_top_bar_text_is_readable(ThemeMode mode)
    {
        var theme = Themes.For(mode);
        var card = SurfacePalette.Card(theme);
        var bar = SurfacePalette.TopBar(theme);

        Assert.True(ColorMath.ContrastRatio(card.Title, card.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(card.Subtitle, card.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(bar.Title, bar.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(bar.Subtitle, bar.ChipHover) >= 4.5);
        Assert.True(card.Shadow.A is > 0 and < 128);
        Assert.True(ColorMath.ContrastRatio(card.Border, card.Background) > 1.1);
    }

    [Theory]
    [InlineData(ThemeMode.Light, false)]
    [InlineData(ThemeMode.Light, true)]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Dark, true)]
    public void Tab_text_is_readable_on_surface_and_alternate_backgrounds(ThemeMode mode, bool alternate)
    {
        var theme = Themes.For(mode);
        var background = alternate ? theme.Colors.SurfaceAlt : theme.Colors.Surface;
        var tabs = SurfacePalette.Tabs(theme, background);

        Assert.True(ColorMath.ContrastRatio(tabs.Text, tabs.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(tabs.Text, tabs.HoverBackground) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(tabs.SelectedText, tabs.Background) >= 4.5);
        Assert.NotEqual(tabs.Background.ToArgb(), tabs.HoverBackground.ToArgb());
    }

    [Fact]
    public void EnsureContrast_keeps_good_colors_and_fixes_weak_ones()
    {
        var white = Color.White;
        var dark = ColorMath.FromHex("#111827");
        Assert.Equal(dark, ColorMath.EnsureContrast(dark, white));

        var weak = ColorMath.FromHex("#FBBF24");
        var fixedOnWhite = ColorMath.EnsureContrast(weak, white);
        Assert.True(ColorMath.ContrastRatio(fixedOnWhite, white) >= 4.5);
        Assert.True(fixedOnWhite.R >= fixedOnWhite.B, "hue is kept (still amber)");

        var weakOnDark = ColorMath.FromHex("#334155");
        var fixedOnDark = ColorMath.EnsureContrast(weakOnDark, dark);
        Assert.True(ColorMath.ContrastRatio(fixedOnDark, dark) >= 4.5);
    }
}
