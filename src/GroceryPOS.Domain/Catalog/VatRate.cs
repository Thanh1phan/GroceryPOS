namespace GroceryPOS.Domain.Catalog;

/// <summary>Vietnamese VAT rates applicable to grocery goods (percent).</summary>
public static class VatRate
{
    public const decimal None = 0m;
    public const decimal Five = 5m;
    public const decimal Eight = 8m;
    public const decimal Ten = 10m;

    public static IReadOnlyList<decimal> Allowed { get; } = new[] { None, Five, Eight, Ten };

    public static bool IsAllowed(decimal rate) => Allowed.Contains(rate);
}
