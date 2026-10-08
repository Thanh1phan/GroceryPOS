using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Tests;

public class VndTests
{
    [Theory]
    [InlineData(125000, "125.000 ₫")]
    [InlineData(0, "0 ₫")]
    [InlineData(1234567.5, "1.234.568 ₫")]
    public void Format_uses_vi_VN_grouping(decimal amount, string expected) =>
        Assert.Equal(expected, Vnd.Format(amount));

    [Theory]
    [InlineData("125.000", 125000)]
    [InlineData("125000", 125000)]
    [InlineData("125.000 ₫", 125000)]
    [InlineData("50k", 50000)]
    [InlineData("1,5k", 1500)]
    public void TryParse_accepts_common_inputs(string text, decimal expected)
    {
        Assert.True(Vnd.TryParse(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5000")]
    public void TryParse_rejects_invalid_or_negative(string text) =>
        Assert.False(Vnd.TryParse(text, out _));
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("matkhau123", true)]
    [InlineData("short1", false)]
    [InlineData("chikhongcoso", false)]
    [InlineData("12345678", false)]
    public void Requires_length_letters_and_digits(string password, bool valid) =>
        Assert.Equal(valid, PasswordPolicy.Validate(password) is null);
}

public class ResultTests
{
    [Fact]
    public void Failed_result_value_access_throws()
    {
        Result<int> result = Error.NotFound("Không tìm thấy");

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Implicit_success_from_value()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }
}
