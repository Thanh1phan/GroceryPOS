using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Catalog;

/// <summary>How a margin looks next to the selling price in the product dialog.</summary>
public enum PriceMarginTone
{
    /// <summary>Nothing to judge yet (no selling price or no cost price).</summary>
    Neutral,

    /// <summary>Selling above cost with a healthy margin.</summary>
    Good,

    /// <summary>Selling above cost but with a thin margin (below <see cref="PriceMargin.ThinMarginPercent"/>).</summary>
    Thin,

    /// <summary>Selling at or below cost: every sale loses money.</summary>
    Loss,
}

/// <summary>Gross margin of one unit (giá bán − giá vốn), shown live while prices are typed.</summary>
public sealed record PriceMargin(decimal Amount, decimal? PercentOfSellPrice, PriceMarginTone Tone, string Text)
{
    /// <summary>Margins under this share of the selling price are flagged as thin (5 %).</summary>
    public const decimal ThinMarginPercent = 5m;

    public static PriceMargin Of(decimal costPrice, decimal sellPrice)
    {
        if (sellPrice <= 0)
        {
            return new PriceMargin(0, null, PriceMarginTone.Neutral, "Nhập giá bán để xem lãi gộp.");
        }

        if (costPrice <= 0)
        {
            return new PriceMargin(sellPrice, null, PriceMarginTone.Neutral, "Chưa có giá vốn – lãi gộp sẽ tính sau khi nhập hàng.");
        }

        var amount = Vnd.Round(sellPrice - costPrice);
        var percent = Math.Round(amount / sellPrice * 100m, 1, MidpointRounding.AwayFromZero);
        var percentText = percent.ToString("0.0", Vnd.Culture) + " %";

        if (amount <= 0)
        {
            var text = amount == 0
                ? "Giá bán bằng giá vốn – không có lãi."
                : $"Bán lỗ {Vnd.Format(-amount)} mỗi {UnitWord} ({percentText}).";
            return new PriceMargin(amount, percent, PriceMarginTone.Loss, text);
        }

        var tone = percent < ThinMarginPercent ? PriceMarginTone.Thin : PriceMarginTone.Good;
        var prefix = tone == PriceMarginTone.Thin ? "Lãi mỏng" : "Lãi gộp";
        return new PriceMargin(amount, percent, tone, $"{prefix} {Vnd.Format(amount)} mỗi {UnitWord} ({percentText} giá bán).");
    }

    private const string UnitWord = "đơn vị";
}
