using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Settings;

namespace GroceryPOS.Domain.Tests;

public class AppSettingTests
{
    [Fact]
    public void Key_is_trimmed_and_lower_cased()
    {
        var setting = new AppSetting("  Store.Name ", "Tạp hóa Cô Ba");

        Assert.Equal("store.name", setting.Key);
        Assert.Equal("Tạp hóa Cô Ba", setting.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("store name")]
    [InlineData("store-name")]
    [InlineData("cửa_hàng")]
    public void Invalid_keys_are_rejected(string key)
    {
        Assert.Throws<DomainException>(() => new AppSetting(key, "x"));
    }

    [Fact]
    public void SetValue_reports_whether_the_value_changed()
    {
        var setting = new AppSetting("loyalty.vnd_per_point", "10000");

        Assert.False(setting.SetValue("10000"));
        Assert.True(setting.SetValue("20000"));
        Assert.Equal("20000", setting.Value);
        Assert.True(setting.SetValue(null));
        Assert.Equal(string.Empty, setting.Value);
    }

    [Fact]
    public void Values_longer_than_the_column_are_rejected()
    {
        var setting = new AppSetting("receipt.footer", "");

        Assert.Throws<DomainException>(() => setting.SetValue(new string('x', AppSetting.ValueMaxLength + 1)));
    }
}
