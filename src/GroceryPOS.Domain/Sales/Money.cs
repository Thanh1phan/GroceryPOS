namespace GroceryPOS.Domain.Sales;

/// <summary>VND money helpers used by the sales domain. VND has no minor unit: amounts are whole đồng.</summary>
public static class Money
{
    public static decimal Round(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    /// <summary>VAT contained in a VAT-inclusive amount, e.g. 108.000 at 8% → 8.000.</summary>
    public static decimal VatPortionOf(decimal grossAmount, decimal vatRate) =>
        vatRate == 0 ? 0 : Round(grossAmount * vatRate / (100 + vatRate));

    /// <summary>
    /// Splits <paramref name="total"/> across <paramref name="weights"/> proportionally, in whole đồng,
    /// using the largest-remainder method so the parts always add up exactly to the total.
    /// </summary>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var weightSum = weights.Sum();
        if (total == 0 || weightSum <= 0)
        {
            return result;
        }

        var remainders = new (int Index, decimal Fraction)[weights.Count];
        decimal allocated = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = total * weights[i] / weightSum;
            var floor = Math.Floor(exact);
            result[i] = floor;
            allocated += floor;
            remainders[i] = (i, exact - floor);
        }

        var leftover = (int)(total - allocated);
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Fraction).ThenBy(r => r.Index).Take(leftover))
        {
            result[index] += 1;
        }

        return result;
    }
}
