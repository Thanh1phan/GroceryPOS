using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Domain.Tests;

public class StockMovementTests
{
    [Fact]
    public void Apply_changes_stock_and_snapshots_balance_and_cost()
    {
        var product = TestData.Product(1, sellPrice: 12_000, costPrice: 9_000);
        product.AdjustStock(10);

        var movement = StockMovement.Apply(product, StockMovementType.Adjustment, -2, TestData.Now, userId: 3,
            reason: StockAdjustmentReason.Damaged, note: "Vỡ chai");

        Assert.Equal(8m, product.StockQuantity);
        Assert.Equal(-2m, movement.Quantity);
        Assert.Equal(10m, movement.BalanceBefore);
        Assert.Equal(8m, movement.BalanceAfter);
        Assert.Equal(-18_000m, movement.CostValue);
        Assert.Equal(1, movement.ProductId);
        Assert.Equal(3, movement.UserId);
        Assert.False(movement.IsInbound);
    }

    [Fact]
    public void Apply_rounds_weighed_quantities_to_three_decimals()
    {
        var product = TestData.Product(1, sellPrice: 120_000);

        var movement = StockMovement.Apply(product, StockMovementType.GoodsReceipt, 1.23456m, TestData.Now, null);

        Assert.Equal(1.235m, movement.Quantity);
        Assert.Equal(1.235m, product.StockQuantity);
    }

    [Fact]
    public void Zero_quantity_is_rejected()
    {
        var product = TestData.Product(1, sellPrice: 1_000);

        var ex = Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, StockMovementType.StockCount, 0.0001m, TestData.Now, null));

        Assert.Equal("stock.quantity", ex.Code);
    }

    [Theory]
    [InlineData(StockMovementType.Sale, 1)]
    [InlineData(StockMovementType.SupplierReturn, 1)]
    [InlineData(StockMovementType.GoodsReceipt, -1)]
    [InlineData(StockMovementType.SaleVoid, -1)]
    [InlineData(StockMovementType.CustomerReturn, -1)]
    [InlineData(StockMovementType.Opening, -1)]
    public void Movement_types_enforce_their_direction(StockMovementType type, int quantity)
    {
        var product = TestData.Product(1, sellPrice: 1_000);
        product.AdjustStock(5);

        var ex = Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, type, quantity, TestData.Now, null, allowNegative: true));

        Assert.Equal("stock.direction", ex.Code);
        Assert.Equal(5m, product.StockQuantity);
    }

    [Theory]
    [InlineData(StockAdjustmentReason.Damaged, 1)]
    [InlineData(StockAdjustmentReason.Expired, 1)]
    [InlineData(StockAdjustmentReason.Lost, 1)]
    [InlineData(StockAdjustmentReason.InternalUse, 1)]
    [InlineData(StockAdjustmentReason.Found, -1)]
    public void Adjustment_reason_must_match_direction(StockAdjustmentReason reason, int quantity)
    {
        var product = TestData.Product(1, sellPrice: 1_000);
        product.AdjustStock(5);

        var ex = Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, StockMovementType.Adjustment, quantity, TestData.Now, null, reason: reason));

        Assert.Equal("stock.reason", ex.Code);
        Assert.Equal(5m, product.StockQuantity);
    }

    [Fact]
    public void Adjustment_requires_reason_and_note_for_other()
    {
        var product = TestData.Product(1, sellPrice: 1_000);
        product.AdjustStock(5);

        var noReason = Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, StockMovementType.Adjustment, -1, TestData.Now, null));
        var noNote = Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, StockMovementType.Adjustment, -1, TestData.Now, null, reason: StockAdjustmentReason.Other, note: " "));
        var correction = StockMovement.Apply(product, StockMovementType.Adjustment, 2, TestData.Now, null, reason: StockAdjustmentReason.Correction);

        Assert.Equal("stock.reason", noReason.Code);
        Assert.Equal("stock.note", noNote.Code);
        Assert.Equal(7m, correction.BalanceAfter);
    }

    [Fact]
    public void Stock_cannot_go_negative_unless_allowed()
    {
        var product = TestData.Product(1, sellPrice: 1_000);
        product.AdjustStock(1);

        Assert.Throws<DomainException>(() =>
            StockMovement.Apply(product, StockMovementType.Adjustment, -2, TestData.Now, null, reason: StockAdjustmentReason.Lost));
        var sale = StockMovement.Apply(product, StockMovementType.Sale, -2, TestData.Now, null, "HD261008-0001", allowNegative: true);

        Assert.Equal(-1m, sale.BalanceAfter);
        Assert.Equal("HD261008-0001", sale.ReferenceCode);
    }

    [Fact]
    public void Every_type_and_reason_has_a_vietnamese_label()
    {
        foreach (var type in Enum.GetValues<StockMovementType>())
        {
            Assert.NotEqual(type.ToString(), type.DisplayName());
        }

        foreach (var reason in Enum.GetValues<StockAdjustmentReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(reason.DisplayName()));
        }
    }
}
