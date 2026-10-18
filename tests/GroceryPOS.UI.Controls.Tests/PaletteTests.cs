using System.Drawing;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class PaletteTests
{
    private static readonly Theme Light = Themes.Light;

    [Theory]
    [InlineData(false, false, false, InteractionState.Disabled)]
    [InlineData(false, true, true, InteractionState.Disabled)]
    [InlineData(true, true, true, InteractionState.Pressed)]
    [InlineData(true, false, true, InteractionState.Pressed)]
    [InlineData(true, true, false, InteractionState.Hover)]
    [InlineData(true, false, false, InteractionState.Normal)]
    public void Interaction_state_precedence(bool enabled, bool hovered, bool pressed, InteractionState expected) =>
        Assert.Equal(expected, InteractionStates.From(enabled, hovered, pressed));

    [Fact]
    public void Primary_button_uses_brand_color_and_shades_on_interaction()
    {
        var normal = ButtonPalette.Resolve(Light, ButtonVariant.Primary, InteractionState.Normal);
        var hover = ButtonPalette.Resolve(Light, ButtonVariant.Primary, InteractionState.Hover);
        var pressed = ButtonPalette.Resolve(Light, ButtonVariant.Primary, InteractionState.Pressed);

        Assert.Equal(Light.Colors.Primary, normal.Background);
        Assert.Equal(Light.Colors.OnPrimary, normal.Foreground);
        Assert.Equal(Light.Hover(Light.Colors.Primary), hover.Background);
        Assert.Equal(Light.Pressed(Light.Colors.Primary), pressed.Background);
        Assert.Equal(hover.Background, hover.Border);
    }

    [Fact]
    public void Danger_button_uses_danger_tokens()
    {
        var colors = ButtonPalette.Resolve(Light, ButtonVariant.Danger, InteractionState.Normal);

        Assert.Equal(Light.Colors.Danger, colors.Background);
        Assert.Equal(Light.Colors.OnDanger, colors.Foreground);
    }

    [Fact]
    public void Secondary_button_is_outlined_on_the_surface()
    {
        var normal = ButtonPalette.Resolve(Light, ButtonVariant.Secondary, InteractionState.Normal);
        var hover = ButtonPalette.Resolve(Light, ButtonVariant.Secondary, InteractionState.Hover);

        Assert.Equal(Light.Colors.Surface, normal.Background);
        Assert.Equal(Light.Colors.Border, normal.Border);
        Assert.Equal(Light.Colors.TextPrimary, normal.Foreground);
        Assert.Equal(Light.Colors.SurfaceAlt, hover.Background);
    }

    [Fact]
    public void Ghost_button_is_transparent_until_hovered()
    {
        var normal = ButtonPalette.Resolve(Light, ButtonVariant.Ghost, InteractionState.Normal);
        var hover = ButtonPalette.Resolve(Light, ButtonVariant.Ghost, InteractionState.Hover);

        Assert.Equal(0, normal.Background.A);
        Assert.Equal(0, normal.Border.A);
        Assert.Equal(Light.Colors.Primary, normal.Foreground);
        Assert.Equal(Light.Colors.SurfaceAlt, hover.Background);
    }

    [Theory]
    [InlineData(ButtonVariant.Primary)]
    [InlineData(ButtonVariant.Secondary)]
    [InlineData(ButtonVariant.Danger)]
    public void Disabled_buttons_look_the_same_whatever_the_variant(ButtonVariant variant)
    {
        var colors = ButtonPalette.Resolve(Light, variant, InteractionState.Disabled);

        Assert.Equal(Light.Colors.DisabledBackground, colors.Background);
        Assert.Equal(Light.Colors.DisabledText, colors.Foreground);
    }

    [Fact]
    public void Unknown_variant_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ButtonPalette.Resolve(Light, (ButtonVariant)99, InteractionState.Normal));

    [Fact]
    public void Input_border_follows_focus_when_there_is_no_validation_state()
    {
        var idle = InputPalette.Resolve(Light, ValidationState.None, focused: false, enabled: true);
        var focused = InputPalette.Resolve(Light, ValidationState.None, focused: true, enabled: true);

        Assert.Equal(Light.Colors.Border, idle.Border);
        Assert.Equal(Light.Colors.TextMuted, idle.Icon);
        Assert.Equal(Light.Colors.Focus, focused.Border);
        Assert.Equal(Light.Colors.Focus, focused.Icon);
        Assert.Equal(Light.Colors.Surface, focused.Background);
    }

    [Theory]
    [InlineData(ValidationState.Error, false)]
    [InlineData(ValidationState.Error, true)]
    [InlineData(ValidationState.Warning, true)]
    [InlineData(ValidationState.Valid, false)]
    public void Validation_state_overrides_focus(ValidationState state, bool focused)
    {
        var expected = InputPalette.AccentOf(Light, state);

        var colors = InputPalette.Resolve(Light, state, focused, enabled: true);

        Assert.NotNull(expected);
        Assert.Equal(expected.Value, colors.Border);
        Assert.Equal(expected.Value, colors.Message);
        Assert.Equal(expected.Value, colors.Icon);
    }

    [Fact]
    public void Validation_accents_map_to_semantic_tokens()
    {
        Assert.Equal(Light.Colors.Danger, InputPalette.AccentOf(Light, ValidationState.Error));
        Assert.Equal(Light.Colors.Warning, InputPalette.AccentOf(Light, ValidationState.Warning));
        Assert.Equal(Light.Colors.Success, InputPalette.AccentOf(Light, ValidationState.Valid));
        Assert.Null(InputPalette.AccentOf(Light, ValidationState.None));
    }

    [Fact]
    public void Disabled_input_ignores_validation_and_focus()
    {
        var colors = InputPalette.Resolve(Light, ValidationState.Error, focused: true, enabled: false);

        Assert.Equal(Light.Colors.DisabledBackground, colors.Background);
        Assert.Equal(Light.Colors.DisabledText, colors.Foreground);
        Assert.Equal(Light.Colors.Border, colors.Border);
    }

    [Fact]
    public void Read_only_input_uses_alternate_surface_and_no_focus_border()
    {
        var colors = InputPalette.Resolve(Light, ValidationState.None, focused: true, enabled: true, readOnly: true);

        Assert.Equal(Light.Colors.SurfaceAlt, colors.Background);
        Assert.Equal(Light.Colors.Border, colors.Border);
        Assert.Equal(Light.Colors.TextPrimary, colors.Foreground);
    }

    [Fact]
    public void Message_uses_secondary_text_without_validation()
    {
        var colors = InputPalette.Resolve(Light, ValidationState.None, focused: false, enabled: true);

        Assert.Equal(Light.Colors.TextSecondary, colors.Message);
        Assert.NotEqual(Color.Empty, colors.Message);
    }
}
