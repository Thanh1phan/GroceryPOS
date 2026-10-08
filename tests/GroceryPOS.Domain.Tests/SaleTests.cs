using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Tests;

public class SaleTests
{
    private static Sale NewSale() => new("hd20261008-0001", cashierId: 1, TestData.Now);

    [Fact]
    public void New_sale_is_an_empty_draft_with_normalized_code()
    {
        var sale = NewSale();

        Assert.Equal("HD20261008-0001", sale.Code);
        Assert.Equal(SaleStatus.Draft, sale.Status);
        Assert.True(sale.IsEmpty);
        Assert.Equal(0m, sale.Total);
    }

    [Fact]
    public void Adding_same_product_twice_merges_into_one_line()
    {
        var sale = NewSale();
        var milk = TestData.Product(1, 8_000m);

        sale.AddItem(milk);
        sale.AddItem(milk, 2);

        var line = Assert.Single(sale.Lines);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(24_000m, sale.Total);
    }

    [Fact]
    public void Discounted_line_is_not_merged()
    {
        var sale = NewSale();
        var milk = TestData.Product(1, 8_000m);
        var first = sale.AddItem(milk);
        sale.SetLineDiscount(first, Discount.Amount(1_000));

        sale.AddItem(milk);

        Assert.Equal(2, sale.Lines.Count);
        Assert.Equal(15_000m, sale.Total);
    }

    [Fact]
    public void Line_snapshots_product_details()
    {
        var sale = NewSale();
        var product = TestData.Product(7, 12_000m, costPrice: 9_000m, vat: VatRate.Eight, name: "Mì Hảo Hảo");

        var line = sale.AddItem(product);
        product.ChangePrices(9_500m, 13_000m);

        Assert.Equal("Mì Hảo Hảo", line.ProductName);
        Assert.Equal(12_000m, line.UnitPrice);
        Assert.Equal(9_000m, line.UnitCost);
        Assert.Equal(VatRate.Eight, line.VatRate);
    }

    [Fact]
    public void Weighed_quantities_are_rounded_to_three_decimals()
    {
        var sale = NewSale();
        var line = sale.AddItem(TestData.Product(1, 120_000m), 0.4567m);

        Assert.Equal(0.457m, line.Quantity);
        Assert.Equal(54_840m, line.GrossAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Non_positive_quantity_is_rejected(decimal quantity)
    {
        var sale = NewSale();
        var line = sale.AddItem(TestData.Product(1, 10_000m));

        Assert.Throws<DomainException>(() => sale.ChangeQuantity(line, quantity));
    }

    [Fact]
    public void Inactive_or_unsaved_products_cannot_be_sold()
    {
        var sale = NewSale();
        var inactive = TestData.Product(1, 10_000m);
        inactive.Deactivate();
        var unsaved = new Product("SP999", "Chưa lưu", 1, "cái", 0, 1_000);

        Assert.Throws<DomainException>(() => sale.AddItem(inactive));
        Assert.Throws<DomainException>(() => sale.AddItem(unsaved));
    }

    [Fact]
    public void Totals_apply_line_then_invoice_discounts()
    {
        var sale = NewSale();
        var a = sale.AddItem(TestData.Product(1, 50_000m), 2); // 100.000
        sale.AddItem(TestData.Product(2, 30_000m));            //  30.000
        sale.SetLineDiscount(a, Discount.Percent(10));          // -10.000
        sale.SetInvoiceDiscount(Discount.Amount(5_000));        //  -5.000

        Assert.Equal(130_000m, sale.GrossAmount);
        Assert.Equal(120_000m, sale.Subtotal);
        Assert.Equal(5_000m, sale.InvoiceDiscountAmount);
        Assert.Equal(115_000m, sale.Total);
        Assert.Equal(15_000m, sale.TotalDiscount);
    }

    [Fact]
    public void Invoice_discount_is_allocated_to_lines_exactly()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 10_000m));
        sale.AddItem(TestData.Product(2, 10_000m));
        sale.AddItem(TestData.Product(3, 10_000m));
        sale.SetInvoiceDiscount(Discount.Amount(1_000));

        Assert.Equal(1_000m, sale.Lines.Sum(l => l.AllocatedDiscount));
        Assert.Equal(sale.Total, sale.Lines.Sum(l => l.FinalAmount));
    }

    [Fact]
    public void Vat_breakdown_groups_by_rate_after_discounts()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 108_000m, vat: VatRate.Eight));
        sale.AddItem(TestData.Product(2, 110_000m, vat: VatRate.Ten));
        sale.AddItem(TestData.Product(3, 20_000m));

        var breakdown = sale.VatBreakdown();

        Assert.Equal(3, breakdown.Count);
        Assert.Equal(new VatSummary(0m, 20_000m, 0m, 20_000m), breakdown[0]);
        Assert.Equal(new VatSummary(8m, 108_000m, 8_000m, 100_000m), breakdown[1]);
        Assert.Equal(new VatSummary(10m, 110_000m, 10_000m, 100_000m), breakdown[2]);
        Assert.Equal(18_000m, sale.VatTotal);
    }

    [Fact]
    public void Profit_excludes_vat_and_cost()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 108_000m, costPrice: 80_000m, vat: VatRate.Eight));

        Assert.Equal(20_000m, sale.Profit);
    }

    [Fact]
    public void Cash_payment_produces_change()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 37_000m));
        sale.AddPayment(PaymentMethod.Cash, 50_000m);

        Assert.Equal(0m, sale.AmountDue);
        Assert.Equal(13_000m, sale.ChangeDue);

        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        Assert.Equal(SaleStatus.Completed, sale.Status);
        Assert.Equal(TestData.Now, sale.CompletedAt);
    }

    [Fact]
    public void Split_payment_cash_and_transfer_is_accepted()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 250_000m));
        sale.AddPayment(PaymentMethod.BankTransfer, 200_000m, "FT2610081234");
        sale.AddPayment(PaymentMethod.Cash, 100_000m);

        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        Assert.Equal(50_000m, sale.ChangeDue);
        Assert.Equal(200_000m, sale.NonCashPaid);
    }

    [Fact]
    public void Transfer_exceeding_total_is_rejected()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 90_000m));
        sale.AddPayment(PaymentMethod.BankTransfer, 100_000m);

        var ex = Assert.Throws<DomainException>(() => sale.Complete(TestData.Now, LoyaltyPolicy.Default));
        Assert.Equal("sale.overpaid", ex.Code);
    }

    [Fact]
    public void Underpaid_sale_cannot_complete()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 90_000m));
        sale.AddPayment(PaymentMethod.Cash, 50_000m);

        var ex = Assert.Throws<DomainException>(() => sale.Complete(TestData.Now, LoyaltyPolicy.Default));
        Assert.Equal("sale.unpaid", ex.Code);
        Assert.Equal(40_000m, sale.AmountDue);
    }

    [Fact]
    public void Empty_sale_cannot_complete()
    {
        var ex = Assert.Throws<DomainException>(() => NewSale().Complete(TestData.Now, LoyaltyPolicy.Default));
        Assert.Equal("sale.empty", ex.Code);
    }

    [Fact]
    public void Completed_sale_is_locked()
    {
        var sale = NewSale();
        var line = sale.AddItem(TestData.Product(1, 10_000m));
        sale.AddPayment(PaymentMethod.Cash, 10_000m);
        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        Assert.Throws<DomainException>(() => sale.AddItem(TestData.Product(2, 5_000m)));
        Assert.Throws<DomainException>(() => sale.ChangeQuantity(line, 2));
        Assert.Throws<DomainException>(() => sale.SetInvoiceDiscount(Discount.Percent(5)));
        Assert.Throws<DomainException>(() => sale.AddPayment(PaymentMethod.Cash, 1_000m));
    }

    [Fact]
    public void Customer_earns_points_on_total_after_discounts()
    {
        var sale = NewSale();
        sale.AttachCustomer(TestData.Customer());
        sale.AddItem(TestData.Product(1, 100_000m));
        sale.SetInvoiceDiscount(Discount.Amount(15_000));
        sale.AddPayment(PaymentMethod.Cash, 85_000m);

        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        Assert.Equal(8, sale.PointsEarned);
    }

    [Fact]
    public void Walk_in_customer_earns_no_points()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 100_000m));
        sale.AddPayment(PaymentMethod.Cash, 100_000m);

        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        Assert.Equal(0, sale.PointsEarned);
    }

    [Fact]
    public void Redeeming_points_reduces_total()
    {
        var sale = NewSale();
        sale.AttachCustomer(TestData.Customer(points: 50));
        sale.AddItem(TestData.Product(1, 100_000m));

        sale.RedeemPoints(50, LoyaltyPolicy.Default);

        Assert.Equal(5_000m, sale.PointsDiscountAmount);
        Assert.Equal(95_000m, sale.Total);
        Assert.Equal(sale.Total, sale.Lines.Sum(l => l.FinalAmount));
    }

    [Fact]
    public void Redeeming_requires_customer_and_enough_points()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 100_000m));

        Assert.Throws<DomainException>(() => sale.RedeemPoints(10, LoyaltyPolicy.Default));

        sale.AttachCustomer(TestData.Customer(points: 5));
        Assert.Throws<DomainException>(() => sale.RedeemPoints(10, LoyaltyPolicy.Default));
    }

    [Fact]
    public void Redeemed_value_cannot_exceed_invoice_amount()
    {
        var sale = NewSale();
        sale.AttachCustomer(TestData.Customer(points: 500));
        sale.AddItem(TestData.Product(1, 10_000m));

        Assert.Throws<DomainException>(() => sale.RedeemPoints(500, LoyaltyPolicy.Default));
    }

    [Fact]
    public void Detaching_customer_clears_points_redemption()
    {
        var sale = NewSale();
        sale.AttachCustomer(TestData.Customer(points: 50));
        sale.AddItem(TestData.Product(1, 100_000m));
        sale.RedeemPoints(50, LoyaltyPolicy.Default);

        sale.DetachCustomer();

        Assert.Equal(0, sale.PointsRedeemed);
        Assert.Equal(100_000m, sale.Total);
    }

    [Fact]
    public void Removing_items_after_redeeming_blocks_completion_until_redeemed_again()
    {
        var sale = NewSale();
        sale.AttachCustomer(TestData.Customer(points: 100));
        var big = sale.AddItem(TestData.Product(1, 100_000m));
        sale.AddItem(TestData.Product(2, 5_000m));
        sale.RedeemPoints(100, LoyaltyPolicy.Default); // 10.000 ₫
        sale.RemoveLine(big);
        sale.AddPayment(PaymentMethod.Cash, 5_000m);

        var ex = Assert.Throws<DomainException>(() => sale.Complete(TestData.Now, LoyaltyPolicy.Default));
        Assert.Equal("sale.points", ex.Code);
    }

    [Fact]
    public void Only_completed_sales_can_be_voided_and_reason_is_required()
    {
        var sale = NewSale();
        sale.AddItem(TestData.Product(1, 10_000m));
        Assert.Throws<DomainException>(() => sale.Void(TestData.Now, 2, "Nhầm hàng"));

        sale.AddPayment(PaymentMethod.Cash, 10_000m);
        sale.Complete(TestData.Now, LoyaltyPolicy.Default);
        Assert.Throws<DomainException>(() => sale.Void(TestData.Now, 2, " "));

        sale.Void(TestData.Now, 2, "Nhầm hàng");
        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal(2, sale.VoidedBy);
    }

    [Fact]
    public void Lines_from_another_sale_are_rejected()
    {
        var sale = NewSale();
        var other = NewSale();
        var foreign = other.AddItem(TestData.Product(1, 10_000m));

        Assert.Throws<DomainException>(() => sale.RemoveLine(foreign));
    }
}
