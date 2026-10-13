using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Shifts;

/// <summary>Counting the drawer by denomination (kiểm đếm tiền theo mệnh giá).</summary>
public static class CashCount
{
    /// <summary>Vietnamese banknotes in circulation, largest first (coins are no longer used at the counter).</summary>
    public static IReadOnlyList<decimal> Denominations { get; } = new decimal[]
    {
        500_000, 200_000, 100_000, 50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500,
    };

    /// <summary>Total of a count; every denomination must be a known note and every count non-negative.</summary>
    public static decimal Total(IReadOnlyDictionary<decimal, int> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        decimal total = 0;
        foreach (var (denomination, count) in notes)
        {
            if (!Denominations.Contains(denomination))
            {
                throw new DomainException("cash.denomination", $"Mệnh giá {denomination:0} không hợp lệ.");
            }

            if (count < 0)
            {
                throw new DomainException("cash.count", "Số tờ không được âm.");
            }

            total += denomination * count;
        }

        return total;
    }
}
