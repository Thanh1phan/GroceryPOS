using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Tests;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Sữa tươi", "sua tuoi")]
    [InlineData("ĐƯỜNG  Biên   Hòa", "duong bien hoa")]
    [InlineData("  Nước mắm Phú Quốc ", "nuoc mam phu quoc")]
    [InlineData(null, "")]
    public void Fold_removes_diacritics_case_and_extra_spaces(string? input, string expected) =>
        Assert.Equal(expected, SearchText.Fold(input));

    [Fact]
    public void Matches_requires_every_word_in_any_field()
    {
        Assert.True(SearchText.Matches("sua 1l", "Sữa tươi Vinamilk 1L", "SUA01"));
        Assert.True(SearchText.Matches("vina sua01", "Sữa tươi Vinamilk 1L", "SUA01"));
        Assert.False(SearchText.Matches("sua 2l", "Sữa tươi Vinamilk 1L", "SUA01"));
        Assert.False(SearchText.Matches("  ", "Sữa"));
        Assert.True(SearchText.Matches("8934", "Sữa", null, "8934673000017"));
    }
}
