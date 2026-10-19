using GroceryPOS.UI.Controls.Inputs.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("Sữa tươi Vinamilk", "sua tuoi vinamilk")]
    [InlineData("ĐƯỜNG  Biên Hòa", "duong bien hoa")]
    [InlineData("  Nước mắm ", "nuoc mam")]
    [InlineData(null, "")]
    public void Fold_removes_accents_case_and_extra_spaces(string? text, string expected) =>
        Assert.Equal(expected, TextMatch.Fold(text));

    [Theory]
    [InlineData("Sữa tươi Vinamilk", "sua", true)]
    [InlineData("Sữa tươi Vinamilk", "vina sữa", true)]
    [InlineData("Đường Biên Hòa", "duong", true)]
    [InlineData("Đường Biên Hòa", "muoi", false)]
    [InlineData("Bất kỳ", "", true)]
    public void Matches_every_word_in_any_order(string text, string query, bool expected) =>
        Assert.Equal(expected, TextMatch.Matches(text, query));

    [Fact]
    public void Rank_prefers_equal_then_prefix()
    {
        Assert.Equal(0, TextMatch.Rank("Thu ngân", "thu ngan"));
        Assert.Equal(1, TextMatch.Rank("Thủ kho", "thu"));
        Assert.Equal(2, TextMatch.Rank("Quản lý kho", "kho"));
    }
}
