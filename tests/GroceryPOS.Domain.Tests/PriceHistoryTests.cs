using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Tests;

public class PriceHistoryTests
{
    private static Product NewProduct() => new("sp001", "Sữa tươi Vinamilk 1L", 1, "hộp", 28_000m, 34_000m, VatRate.Five);

    [Fact]
    public void From_returns_null_when_prices_unchanged()
    {
        var product = NewProduct();
        var change = product.ChangePrices(28_000m, 34_000m);

        var history = PriceHistory.From(product, change, DateTimeOffset.UtcNow, changedByUserId: 1, PriceChangeSource.ManualEdit);

        Assert.Null(history);
    }

    [Fact]
    public void From_captures_old_and_new_values_source_and_note()
    {
        var product = NewProduct();
        var changedAt = DateTimeOffset.UtcNow;
        var change = product.ChangePrices(29_000m, 36_000m);

        var history = PriceHistory.From(product, change, changedAt, changedByUserId: 2, PriceChangeSource.GoodsReceipt, "Nhập hàng NCC A");

        Assert.NotNull(history);
        Assert.Equal(product.Id, history!.ProductId);
        Assert.Equal(28_000m, history.OldCostPrice);
        Assert.Equal(34_000m, history.OldSellPrice);
        Assert.Equal(29_000m, history.NewCostPrice);
        Assert.Equal(36_000m, history.NewSellPrice);
        Assert.Equal(changedAt, history.ChangedAt);
        Assert.Equal(2, history.ChangedByUserId);
        Assert.Equal(PriceChangeSource.GoodsReceipt, history.Source);
        Assert.Equal("Nhập hàng NCC A", history.Note);
    }

    [Fact]
    public void From_throws_on_null_product()
    {
        var change = new PriceChange(28_000m, 34_000m, 29_000m, 36_000m);
        Assert.Throws<ArgumentNullException>(() =>
            PriceHistory.From(null!, change, DateTimeOffset.UtcNow, null, PriceChangeSource.ManualEdit));
    }

    [Fact]
    public void DisplayName_is_vietnamese()
    {
        Assert.Equal("Sửa trực tiếp", PriceChangeSource.ManualEdit.DisplayName());
        Assert.Equal("Nhập hàng", PriceChangeSource.GoodsReceipt.DisplayName());
    }
}
