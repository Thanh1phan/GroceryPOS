using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Domain.Tests;

/// <summary>Builders for domain objects used across tests.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Simulates a persisted entity by setting its identity (normally assigned by the database).</summary>
    public static T WithId<T>(this T entity, int id) where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
        return entity;
    }

    public static Product Product(int id, decimal sellPrice, decimal costPrice = 0, decimal vat = VatRate.None, string? name = null) =>
        new Product($"SP{id:000}", name ?? $"Sản phẩm {id}", categoryId: 1, unit: "cái", costPrice, sellPrice, vat).WithId(id);

    public static Customer Customer(int id = 1, int points = 0)
    {
        var customer = new Customer($"KH{id:000}", "Nguyễn Văn A", "0901234567").WithId(id);
        if (points > 0)
        {
            // Earn points via a purchase so the test uses the public API only.
            customer.RecordPurchase(points * LoyaltyPolicy.Default.VndPerPoint, LoyaltyPolicy.Default);
        }

        return customer;
    }
}
