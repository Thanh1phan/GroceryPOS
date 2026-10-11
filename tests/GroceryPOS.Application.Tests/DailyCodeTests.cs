using GroceryPOS.Application.Common;

namespace GroceryPOS.Application.Tests;

public class DailyCodeTests
{
    [Theory]
    [InlineData(null, "PN261011-0001")]
    [InlineData("PN261011-0009", "PN261011-0010")]
    [InlineData("pn261011-0041", "PN261011-0042")]
    [InlineData("PN261011-ABCD", "PN261011-0001")]
    [InlineData("PN261010-0005", "PN261011-0001")]
    public void Next_continues_the_daily_sequence(string? last, string expected)
    {
        var prefix = DailyCode.PrefixFor(" pn ", new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.FromHours(7)));

        Assert.Equal("PN261011-", prefix);
        Assert.Equal(expected, DailyCode.Next(prefix, last));
    }
}
