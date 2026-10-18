using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class ThemeTests
{
    public static TheoryData<string> ThemeNames => new() { "Light", "Dark" };

    private static Theme ByName(string name) => name == "Dark" ? Themes.Dark : Themes.Light;

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Text_tokens_meet_wcag_aa_on_surfaces(string name)
    {
        var c = ByName(name).Colors;

        foreach (var surface in new[] { c.Background, c.Surface, c.SurfaceAlt })
        {
            AssertContrast(c.TextPrimary, surface, 4.5);
            AssertContrast(c.TextSecondary, surface, 4.5);
        }

        // Placeholders and icons may be lighter, but must stay readable (AA large / UI components).
        AssertContrast(c.TextMuted, c.Surface, 3.0);
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Filled_button_text_meets_wcag_aa_in_every_enabled_state(string name)
    {
        var theme = ByName(name);
        foreach (var variant in new[] { ButtonVariant.Primary, ButtonVariant.Danger, ButtonVariant.Secondary })
        {
            foreach (var state in new[] { InteractionState.Normal, InteractionState.Hover, InteractionState.Pressed })
            {
                var colors = ButtonPalette.Resolve(theme, variant, state);
                AssertContrast(colors.Foreground, colors.Background, 4.5, $"{variant}/{state}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Validation_and_focus_accents_are_visible_against_the_surface(string name)
    {
        var c = ByName(name).Colors;

        foreach (var accent in new[] { c.Danger, c.Warning, c.Success, c.Focus, c.Primary })
        {
            AssertContrast(accent, c.Surface, 3.0);
        }
    }

    [Fact]
    public void Light_and_dark_share_typography_and_spacing_but_not_colors()
    {
        Assert.Equal(Themes.Light.Typography, Themes.Dark.Typography);
        Assert.Equal(Themes.Light.Spacing, Themes.Dark.Spacing);
        Assert.NotEqual(Themes.Light.Colors.Background, Themes.Dark.Colors.Background);
        Assert.False(Themes.Light.IsDark);
        Assert.True(Themes.Dark.IsDark);
    }

    [Fact]
    public void Hover_and_pressed_darken_on_light_and_lighten_on_dark()
    {
        var fill = ColorMath.FromHex("#15803D");
        var l = ColorMath.RelativeLuminance(fill);

        Assert.True(ColorMath.RelativeLuminance(Themes.Light.Hover(fill)) < l);
        Assert.True(ColorMath.RelativeLuminance(Themes.Light.Pressed(fill)) < ColorMath.RelativeLuminance(Themes.Light.Hover(fill)));
        Assert.True(ColorMath.RelativeLuminance(Themes.Dark.Hover(fill)) > l);
        Assert.True(ColorMath.RelativeLuminance(Themes.Dark.Pressed(fill)) > ColorMath.RelativeLuminance(Themes.Dark.Hover(fill)));
    }

    [Theory]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData(" dark ", ThemeMode.Dark)]
    [InlineData("LIGHT", ThemeMode.Light)]
    [InlineData("", ThemeMode.Light)]
    [InlineData(null, ThemeMode.Light)]
    [InlineData("blue", ThemeMode.Light)]
    [InlineData("7", ThemeMode.Light)]
    public void ParseMode_defaults_to_light(string? value, ThemeMode expected) =>
        Assert.Equal(expected, Themes.ParseMode(value));

    [Fact]
    public void For_returns_the_built_in_theme()
    {
        Assert.Same(Themes.Dark, Themes.For(ThemeMode.Dark));
        Assert.Same(Themes.Light, Themes.For(ThemeMode.Light));
    }

    [Fact]
    public void Typography_sizes_grow_with_the_role()
    {
        var t = Themes.Light.Typography;
        var sizes = Enum.GetValues<FontRole>().Select(t.SizeOf).ToArray();

        Assert.Equal(sizes.OrderBy(s => s), sizes);
    }

    private static void AssertContrast(Color foreground, Color background, double minimum, string? context = null)
    {
        var ratio = ColorMath.ContrastRatio(foreground, background);
        Assert.True(
            ratio >= minimum,
            $"{context} {ColorMath.ToHex(foreground)} on {ColorMath.ToHex(background)} = {ratio:0.00}:1 (< {minimum}:1)");
    }
}
