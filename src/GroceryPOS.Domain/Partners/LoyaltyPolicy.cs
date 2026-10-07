namespace GroceryPOS.Domain.Partners;

/// <summary>
/// Loyalty program rules: customers earn 1 point per <see cref="VndPerPoint"/> spent
/// and each redeemed point is worth <see cref="PointValueVnd"/>.
/// </summary>
public sealed record LoyaltyPolicy(decimal VndPerPoint, decimal PointValueVnd)
{
    public static LoyaltyPolicy Default { get; } = new(10_000m, 100m);

    public int PointsFor(decimal paidAmount) =>
        paidAmount <= 0 || VndPerPoint <= 0 ? 0 : (int)Math.Floor(paidAmount / VndPerPoint);

    public decimal ValueOf(int points) => points * PointValueVnd;
}
