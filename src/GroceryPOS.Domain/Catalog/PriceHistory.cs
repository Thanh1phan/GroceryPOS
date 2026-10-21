using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Catalog;

/// <summary>Where a price change originated, for the price-history screen.</summary>
public enum PriceChangeSource
{
    ManualEdit = 0,
    GoodsReceipt = 1,
}

public static class PriceChangeSourceExtensions
{
    public static string DisplayName(this PriceChangeSource source) => source switch
    {
        PriceChangeSource.GoodsReceipt => "Nhập hàng",
        _ => "Sửa trực tiếp",
    };
}

/// <summary>
/// Immutable record of one change to <see cref="Product.CostPrice"/>/<see cref="Product.SellPrice"/>
/// (lịch sử thay đổi giá). Built only from a <see cref="PriceChange"/> returned by
/// <see cref="Product.ChangePrices"/>/<see cref="Product.ApplyPurchaseCost"/>, so a row can never
/// disagree with the change it describes.
/// </summary>
public sealed class PriceHistory : Entity
{
    private PriceHistory()
    {
    }

    private PriceHistory(
        Product product,
        PriceChange change,
        DateTimeOffset changedAt,
        int? changedByUserId,
        PriceChangeSource source,
        string? note)
    {
        ProductId = product.Id;
        Product = product;
        OldCostPrice = change.OldCostPrice;
        OldSellPrice = change.OldSellPrice;
        NewCostPrice = change.NewCostPrice;
        NewSellPrice = change.NewSellPrice;
        ChangedAt = changedAt;
        ChangedByUserId = changedByUserId;
        Source = source;
        Note = Guard.Optional(note);
    }

    public int ProductId { get; private set; }
    public Product? Product { get; private set; }
    public decimal OldCostPrice { get; private set; }
    public decimal OldSellPrice { get; private set; }
    public decimal NewCostPrice { get; private set; }
    public decimal NewSellPrice { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public int? ChangedByUserId { get; private set; }
    public PriceChangeSource Source { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Builds a row from a <see cref="PriceChange"/>; returns null when nothing actually changed.</summary>
    public static PriceHistory? From(
        Product product,
        PriceChange change,
        DateTimeOffset changedAt,
        int? changedByUserId,
        PriceChangeSource source,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(change);
        return change.HasChanged ? new PriceHistory(product, change, changedAt, changedByUserId, source, note) : null;
    }
}
