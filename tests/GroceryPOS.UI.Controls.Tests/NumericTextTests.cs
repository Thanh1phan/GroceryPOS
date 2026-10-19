using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class NumericTextTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.000")]
    [InlineData(1250000, "1.250.000")]
    [InlineData(1250000.6, "1.250.001")]
    [InlineData(-45000, "-45.000")]
    public void Formats_integers_with_vietnamese_grouping(decimal value, string expected) =>
        Assert.Equal(expected, NumericText.Format(value, NumericFormat.Integer with { Minimum = -1_000_000 }));

    [Theory]
    [InlineData(1.5, "1,5")]
    [InlineData(1.25, "1,25")]
    [InlineData(2, "2")]
    [InlineData(1234.5678, "1.234,568")]
    public void Formats_decimals_without_trailing_zeros(decimal value, string expected) =>
        Assert.Equal(expected, NumericText.Format(value, NumericFormat.Quantity));

    [Theory]
    [InlineData("1.250.000", 1250000)]
    [InlineData("1250000", 1250000)]
    [InlineData("1.250.000 ₫", 1250000)]
    [InlineData("1.250.000đ", 1250000)]
    [InlineData("  500 000 VND ", 500000)]
    [InlineData("25k", 25000)]
    [InlineData("2,5K", 2500)]
    [InlineData("12,4", 12)]
    public void Parses_currency_input(string text, decimal expected)
    {
        Assert.True(NumericText.TryParse(text, NumericFormat.Currency, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-")]
    [InlineData("-5000")]
    [InlineData("1,2,3")]
    [InlineData("k")]
    public void Rejects_invalid_currency_input(string text) =>
        Assert.False(NumericText.TryParse(text, NumericFormat.Currency, out _));

    [Fact]
    public void Thousands_shortcut_only_when_enabled() =>
        Assert.False(NumericText.TryParse("25k", NumericFormat.Integer, out _));

    [Theory]
    [InlineData("1,5", 1.5)]
    [InlineData("1.5", 1.5)]
    [InlineData("0.25", 0.25)]
    [InlineData("1.500", 1500)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("0,12345", 0.123)]
    public void Parses_weights_with_comma_or_unambiguous_dot(string text, decimal expected)
    {
        Assert.True(NumericText.TryParse(text, NumericFormat.Quantity, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Negative_values_need_a_negative_minimum()
    {
        var signed = NumericFormat.Integer with { Minimum = -100 };

        Assert.True(NumericText.TryParse("-50", signed, out var value));
        Assert.Equal(-50, value);
        Assert.True(NumericText.IsAllowedChar('-', signed));
        Assert.False(NumericText.IsAllowedChar('-', NumericFormat.Integer));
    }

    [Fact]
    public void Clamp_and_range_follow_the_format()
    {
        Assert.Equal(100, NumericText.Clamp(150, NumericFormat.Percent));
        Assert.Equal(0, NumericText.Clamp(-3, NumericFormat.Percent));
        Assert.Equal(12.35m, NumericText.Clamp(12.345m, NumericFormat.Percent));
        Assert.False(NumericText.IsInRange(100.5m, NumericFormat.Percent));
        Assert.True(NumericText.IsInRange(0, NumericFormat.Percent));
    }

    [Fact]
    public void Step_adds_increment_and_stays_in_range()
    {
        var format = NumericFormat.Integer with { Minimum = 1, Maximum = 10 };

        Assert.Equal(6, NumericText.Step(5, 1, +1, format));
        Assert.Equal(1, NumericText.Step(1, 1, -1, format));
        Assert.Equal(10, NumericText.Step(9, 5, +1, format));
        Assert.Equal(2, NumericText.Step(null, 1, +1, format));
        Assert.Equal(1000, NumericText.Step(null, 1000, +1, NumericFormat.Currency));
    }

    [Theory]
    [InlineData('5', true)]
    [InlineData('.', true)]
    [InlineData(',', false)]
    [InlineData('k', true)]
    [InlineData('a', false)]
    [InlineData('\b', true)]
    public void Allowed_characters_for_currency(char ch, bool expected) =>
        Assert.Equal(expected, NumericText.IsAllowedChar(ch, NumericFormat.Currency));

    [Fact]
    public void Decimal_comma_allowed_only_with_decimals()
    {
        Assert.True(NumericText.IsAllowedChar(',', NumericFormat.Quantity));
        Assert.False(NumericText.IsAllowedChar(',', NumericFormat.Integer));
    }

    [Theory]
    [InlineData("1250000", 7, "1.250.000", 9)]
    [InlineData("1.250.0000", 10, "12.500.000", 10)]
    [InlineData("1.25000", 2, "125.000", 1)]
    [InlineData("12.5000", 4, "125.000", 3)]
    [InlineData("", 0, "", 0)]
    [InlineData("0005", 4, "5", 1)]
    [InlineData("1a2b3", 5, "123", 3)]
    public void Regroup_inserts_separators_and_keeps_caret(string text, int caret, string expectedText, int expectedCaret)
    {
        var (result, newCaret) = NumericText.Regroup(text, caret, NumericFormat.Currency);

        Assert.Equal(expectedText, result);
        Assert.Equal(expectedCaret, newCaret);
    }

    [Theory]
    [InlineData("1234,5", 6, "1.234,5", 7)]
    [InlineData(",5", 2, "0,5", 2)]
    [InlineData("1,23456", 7, "1,234", 5)]
    [InlineData("1234,", 5, "1.234,", 6)]
    public void Regroup_keeps_the_decimal_part(string text, int caret, string expectedText, int expectedCaret)
    {
        var (result, newCaret) = NumericText.Regroup(text, caret, NumericFormat.Quantity);

        Assert.Equal(expectedText, result);
        Assert.Equal(expectedCaret, newCaret);
    }

    [Fact]
    public void Regroup_keeps_minus_sign_when_allowed()
    {
        var signed = NumericFormat.Integer with { Minimum = -1_000_000 };

        Assert.Equal(("-12.000", 7), NumericText.Regroup("-12000", 6, signed));
        Assert.Equal(("12.000", 6), NumericText.Regroup("-12000", 6, NumericFormat.Integer));
    }

    [Fact]
    public void Decimal_places_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumericFormat { DecimalPlaces = 7 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumericFormat { DecimalPlaces = -1 });
    }
}
