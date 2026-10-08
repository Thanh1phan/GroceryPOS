using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Sales;

public enum DiscountKind
{
    None = 0,
    Percent = 1,
    Amount = 2,
}

/// <summary>
/// A discount expressed either as a percentage (0–100) or as a fixed VND amount.
/// The resulting amount is rounded to whole đồng and never exceeds the base it applies to.
/// </summary>
public sealed record Discount
{
    private Discount(DiscountKind kind, decimal value)
    {
        Kind = kind;
        Value = value;
    }

    public static Discount None { get; } = new(DiscountKind.None, 0);

    public DiscountKind Kind { get; }

    /// <summary>Percent (0–100) for <see cref="DiscountKind.Percent"/>, VND for <see cref="DiscountKind.Amount"/>.</summary>
    public decimal Value { get; }

    public bool IsNone => Kind == DiscountKind.None || Value == 0;

    public static Discount Percent(decimal percent)
    {
        if (percent is < 0 or > 100)
        {
            throw new DomainException("discount.percent", "Phần trăm giảm giá phải từ 0 đến 100.");
        }

        return percent == 0 ? None : new Discount(DiscountKind.Percent, percent);
    }

    public static Discount Amount(decimal amount)
    {
        Guard.NotNegative(amount, "discount.amount", "Số tiền giảm giá không được âm.");
        return amount == 0 ? None : new Discount(DiscountKind.Amount, Money.Round(amount));
    }

    /// <summary>Discount amount on <paramref name="baseAmount"/>, capped at the base.</summary>
    public decimal AmountOn(decimal baseAmount)
    {
        if (baseAmount <= 0 || IsNone)
        {
            return 0;
        }

        var raw = Kind == DiscountKind.Percent ? baseAmount * Value / 100m : Value;
        return Math.Min(Money.Round(raw), baseAmount);
    }

    public override string ToString() => Kind switch
    {
        DiscountKind.Percent => $"{Value:0.##}%",
        DiscountKind.Amount => $"{Value:#,0} ₫",
        _ => "0",
    };
}
