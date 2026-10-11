using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Domain.Tests;

public class GoodsReceiptTests
{
    private static GoodsReceipt NewReceipt() => new("pn261011-0001", supplierId: 1, createdById: 2, TestData.Now);

    [Fact]
    public void New_receipt_is_draft_with_upper_case_code()
    {
        var receipt = NewReceipt();

        Assert.Equal("PN261011-0001", receipt.Code);
        Assert.True(receipt.IsDraft);
        Assert.Equal(0m, receipt.Total);
        Assert.Throws<DomainException>(() => new GoodsReceipt("PN1", supplierId: 0, createdById: 2, TestData.Now));
    }

    [Fact]
    public void Totals_apply_line_and_receipt_discounts_and_allocate_exactly()
    {
        var receipt = NewReceipt();
        var milk = TestData.Product(1, sellPrice: 9_000, costPrice: 7_000);
        var noodles = TestData.Product(2, sellPrice: 4_500, costPrice: 3_500);

        receipt.AddLine(milk, 48, 7_200, discountAmount: 9_600);   // 345.600 − 9.600 = 336.000
        receipt.AddLine(noodles, 30, 3_400);                        // 102.000
        receipt.SetDiscount(10_000);
        receipt.SetPaidAmount(400_000);

        Assert.Equal(447_600m, receipt.GrossAmount);
        Assert.Equal(438_000m, receipt.Subtotal);
        Assert.Equal(428_000m, receipt.Total);
        Assert.Equal(28_000m, receipt.DebtAmount);
        Assert.Equal(10_000m, receipt.Lines.Sum(l => l.AllocatedDiscount));
        Assert.Equal(receipt.Total, receipt.Lines.Sum(l => l.FinalAmount));
    }

    [Fact]
    public void Lines_reject_duplicates_inactive_products_and_bad_numbers()
    {
        var receipt = NewReceipt();
        var milk = TestData.Product(1, sellPrice: 9_000);
        receipt.AddLine(milk, 10, 7_000);

        Assert.Throws<DomainException>(() => receipt.AddLine(milk, 1, 7_000));
        Assert.Throws<DomainException>(() => receipt.AddLine(TestData.Product(2, 1_000), 0, 500));
        Assert.Throws<DomainException>(() => receipt.AddLine(TestData.Product(3, 1_000), 1, -1));
        Assert.Throws<DomainException>(() => receipt.AddLine(TestData.Product(4, 1_000), 2, 500, discountAmount: 1_001));

        var inactive = TestData.Product(5, 1_000);
        inactive.Deactivate();
        Assert.Throws<DomainException>(() => receipt.AddLine(inactive, 1, 500));

        // A rejected update leaves the line untouched.
        Assert.Throws<DomainException>(() => receipt.UpdateLine(milk.Id, 5, 7_000, discountAmount: 50_000));
        Assert.Equal(10m, receipt.Lines[0].Quantity);
        Assert.Single(receipt.Lines);
    }

    [Fact]
    public void Complete_moves_cost_to_weighted_average_increases_stock_and_writes_ledger()
    {
        var milk = TestData.Product(1, sellPrice: 9_000, costPrice: 7_000);
        milk.AdjustStock(20);
        var receipt = NewReceipt();
        receipt.AddLine(milk, 30, 8_000);
        var products = new Dictionary<int, Product> { [1] = milk };

        var posting = Assert.Single(receipt.Complete(products, TestData.Now, userId: 2));

        // (20 × 7.000 + 30 × 8.000) / 50 = 7.600
        Assert.Equal(7_600m, milk.CostPrice);
        Assert.Equal(50m, milk.StockQuantity);
        Assert.Equal(7_000m, posting.CostChange.OldCostPrice);
        Assert.Null(posting.SellPriceChange);
        Assert.Equal(StockMovementType.GoodsReceipt, posting.Movement.Type);
        Assert.Equal("PN261011-0001", posting.Movement.ReferenceCode);
        Assert.Equal(8_000m, posting.Movement.UnitCost);
        Assert.Equal(240_000m, posting.Movement.CostValue);
        Assert.Equal(GoodsReceiptStatus.Completed, receipt.Status);
        Assert.Equal(2, receipt.CompletedById);
    }

    [Fact]
    public void Complete_uses_landed_cost_after_discounts_and_applies_new_sell_price()
    {
        var beer = TestData.Product(1, sellPrice: 14_000, costPrice: 11_000);
        var receipt = NewReceipt();
        receipt.AddLine(beer, 24, 11_500, discountAmount: 12_000, newSellPrice: 15_000); // 276.000 − 12.000 = 264.000
        receipt.SetDiscount(24_000);                                                       // 240.000 → 10.000 / lon

        var posting = Assert.Single(receipt.Complete(new Dictionary<int, Product> { [1] = beer }, TestData.Now, 2));

        Assert.Equal(10_000m, receipt.Lines[0].EffectiveUnitCost);
        Assert.Equal(10_000m, beer.CostPrice); // stock was 0 → cost is simply the landed cost
        Assert.Equal(15_000m, beer.SellPrice);
        Assert.Equal(14_000m, posting.SellPriceChange!.OldSellPrice);
        Assert.Equal(24m, beer.StockQuantity);
    }

    [Fact]
    public void Complete_checks_all_rules_before_changing_products()
    {
        var milk = TestData.Product(1, sellPrice: 9_000, costPrice: 7_000);
        var sugar = TestData.Product(2, sellPrice: 27_000, costPrice: 22_000);
        var receipt = NewReceipt();
        receipt.AddLine(milk, 10, 7_000);
        receipt.AddLine(sugar, 5, 22_000);

        // Sugar is missing from the loaded products → nothing may change, not even milk.
        Assert.Throws<DomainException>(() => receipt.Complete(new Dictionary<int, Product> { [1] = milk }, TestData.Now, 2));
        Assert.Equal(0m, milk.StockQuantity);
        Assert.True(receipt.IsDraft);

        receipt.SetPaidAmount(1_000_000);
        var ex = Assert.Throws<DomainException>(() =>
            receipt.Complete(new Dictionary<int, Product> { [1] = milk, [2] = sugar }, TestData.Now, 2));
        Assert.Equal("receipt.paid", ex.Code);

        Assert.Throws<DomainException>(() => NewReceipt().Complete(new Dictionary<int, Product>(), TestData.Now, 2));
    }

    [Fact]
    public void Completed_receipt_is_read_only_and_cannot_be_cancelled()
    {
        var milk = TestData.Product(1, sellPrice: 9_000, costPrice: 7_000);
        var receipt = NewReceipt();
        receipt.AddLine(milk, 10, 7_000);
        receipt.Complete(new Dictionary<int, Product> { [1] = milk }, TestData.Now, 2);

        Assert.Throws<DomainException>(() => receipt.SetDiscount(1_000));
        Assert.Throws<DomainException>(() => receipt.AddLine(TestData.Product(2, 1_000), 1, 500));
        Assert.Equal("receipt.completed", Assert.Throws<DomainException>(() => receipt.Cancel(TestData.Now, 2, null)).Code);
    }

    [Fact]
    public void Draft_can_be_cancelled_once()
    {
        var receipt = NewReceipt();
        receipt.Cancel(TestData.Now, 2, "  Nhập nhầm nhà cung cấp ");

        Assert.Equal(GoodsReceiptStatus.Cancelled, receipt.Status);
        Assert.Equal("Nhập nhầm nhà cung cấp", receipt.CancelReason);
        Assert.Throws<DomainException>(() => receipt.Cancel(TestData.Now, 2, null));
    }

    [Theory]
    [InlineData(0, 7_000, 10, 8_000, 8_000)]     // empty stock → purchase cost
    [InlineData(-5, 7_000, 10, 8_000, 8_000)]    // negative stock (sold before receipt) → purchase cost
    [InlineData(10, 7_000, 10, 8_000, 7_500)]
    [InlineData(3, 10_000, 1, 10_001, 10_000)]   // rounds to whole đồng
    public void ApplyPurchaseCost_uses_moving_weighted_average(decimal stock, decimal oldCost, decimal qty, decimal unitCost, decimal expected)
    {
        var product = TestData.Product(1, sellPrice: 20_000, costPrice: oldCost);
        product.AdjustStock(stock, allowNegative: true);

        var change = product.ApplyPurchaseCost(qty, unitCost);

        Assert.Equal(expected, product.CostPrice);
        Assert.Equal(oldCost, change.OldCostPrice);
        Assert.Equal(20_000m, product.SellPrice);
    }

    [Fact]
    public void Supplier_debt_increases_on_receipt_and_cannot_be_overpaid()
    {
        var supplier = new Supplier("ncc01", "Công ty Vinamilk");
        supplier.AddDebt(28_000);
        supplier.PayDebt(8_000);

        Assert.Equal(20_000m, supplier.DebtBalance);
        Assert.Throws<DomainException>(() => supplier.PayDebt(20_001));
        Assert.Throws<DomainException>(() => supplier.PayDebt(0));
        Assert.Throws<DomainException>(() => supplier.AddDebt(-1));
    }
}
