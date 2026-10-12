using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Tests;

public class SaleReturnTests
{
    private static int _code;

    private static string NextCode() => $"TH261012-{++_code:0000}";

    /// <summary>
    /// Milk 3 × 10.000 + noodles 4 × 5.000 = 50.000, 10% invoice discount → total 45.000
    /// (milk final 27.000, noodles final 18.000). With a member the sale earns 4 points.
    /// </summary>
    private static Sale CompletedSale(Customer? customer = null, int redeem = 0)
    {
        var sale = new Sale("HD261012-0001", cashierId: 1, TestData.Now).WithId(10);
        sale.AddItem(TestData.Product(1, 10_000m, costPrice: 7_000m, vat: 5m, name: "Sữa tươi"), 3).WithId(101);
        sale.AddItem(TestData.Product(2, 5_000m, costPrice: 3_500m, name: "Mì gói"), 4).WithId(102);
        sale.SetInvoiceDiscount(Discount.Percent(10));
        if (customer is not null)
        {
            sale.AttachCustomer(customer);
            if (redeem > 0)
            {
                sale.RedeemPoints(redeem, LoyaltyPolicy.Default);
            }
        }

        sale.AddPayment(PaymentMethod.Cash, 50_000m);
        sale.Complete(TestData.Now, LoyaltyPolicy.Default);
        return sale;
    }

    private static SaleReturn Return(Sale sale, params ReturnItem[] items) =>
        SaleReturn.Create(NextCode(), sale, userId: 2, TestData.Now.AddDays(1), "Khách đổi ý", PaymentMethod.Cash, items);

    private static SaleLine Line(Sale sale, int id) => sale.Lines.Single(l => l.Id == id);

    [Fact]
    public void Partial_return_refunds_share_of_discounted_line_amount()
    {
        var sale = CompletedSale();
        var milk = Line(sale, 101);

        var result = Return(sale, new ReturnItem(milk, 1));

        Assert.Equal(27_000m, milk.FinalAmount);
        Assert.Equal(9_000m, result.RefundTotal);
        Assert.Equal(1m, milk.ReturnedQuantity);
        Assert.Equal(2m, milk.RemainingQuantity);
        Assert.Equal(9_000m, sale.RefundedTotal);
        Assert.Equal(36_000m, sale.NetTotal);
        Assert.Equal("HD261012-0001", result.SaleCode);
        Assert.Equal(10, result.SaleId);
        var line = Assert.Single(result.Lines);
        Assert.Equal(101, line.SaleLineId);
        Assert.Equal(7_000m, line.CostAmount);
        Assert.Equal(429m, line.VatAmount); // 9.000 × 5/105
    }

    [Fact]
    public void Returning_line_in_pieces_refunds_exactly_its_final_amount()
    {
        var sale = new Sale("HD261012-0002", cashierId: 1, TestData.Now);
        var line = sale.AddItem(TestData.Product(1, 10_000m), 3).WithId(1);
        sale.SetLineDiscount(line, Discount.Amount(1_000m));
        sale.AddPayment(PaymentMethod.Cash, 29_000m);
        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        var refunds = Enumerable.Range(0, 3).Select(_ => Return(sale, new ReturnItem(line, 1)).RefundTotal).ToList();

        Assert.Equal(new[] { 9_667m, 9_667m, 9_666m }, refunds);
        Assert.Equal(29_000m, sale.RefundedTotal);
        Assert.True(sale.IsFullyReturned);
    }

    [Fact]
    public void Cannot_return_more_than_remaining_quantity()
    {
        var sale = CompletedSale();
        var milk = Line(sale, 101);
        Return(sale, new ReturnItem(milk, 2));

        var ex = Assert.Throws<DomainException>(() => Return(sale, new ReturnItem(milk, 2)));

        Assert.Equal("return.quantity", ex.Code);
        Assert.Equal(2m, milk.ReturnedQuantity);
    }

    [Fact]
    public void Rejected_return_leaves_every_line_untouched()
    {
        var sale = CompletedSale();

        Assert.Throws<DomainException>(() =>
            Return(sale, new ReturnItem(Line(sale, 101), 1), new ReturnItem(Line(sale, 102), 5)));

        Assert.False(sale.HasReturns);
        Assert.Equal(0m, sale.RefundedTotal);
    }

    [Fact]
    public void Same_line_twice_in_one_return_is_rejected()
    {
        var sale = CompletedSale();
        var milk = Line(sale, 101);

        var ex = Assert.Throws<DomainException>(() => Return(sale, new ReturnItem(milk, 1), new ReturnItem(milk, 1)));

        Assert.Equal("return.duplicate", ex.Code);
    }

    [Fact]
    public void Line_from_another_sale_is_rejected()
    {
        var sale = CompletedSale();
        var other = CompletedSale();

        Assert.Throws<DomainException>(() => Return(sale, new ReturnItem(Line(other, 101), 1)));
        Assert.False(sale.HasReturns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(int quantity)
    {
        var sale = CompletedSale();

        var ex = Assert.Throws<DomainException>(() => Return(sale, new ReturnItem(Line(sale, 101), quantity)));

        Assert.Equal("return.quantity", ex.Code);
    }

    [Fact]
    public void Return_requires_items_reason_and_cash_or_transfer_refund()
    {
        var sale = CompletedSale();
        var item = new ReturnItem(Line(sale, 101), 1);

        Assert.Equal("return.empty", Assert.Throws<DomainException>(() => Return(sale)).Code);
        Assert.Equal("return.reason", Assert.Throws<DomainException>(() =>
            SaleReturn.Create(NextCode(), sale, 2, TestData.Now, " ", PaymentMethod.Cash, new[] { item })).Code);
        Assert.Equal("return.refund_method", Assert.Throws<DomainException>(() =>
            SaleReturn.Create(NextCode(), sale, 2, TestData.Now, "Lỗi", PaymentMethod.Card, new[] { item })).Code);
        Assert.False(sale.HasReturns);
    }

    [Fact]
    public void Voided_sale_cannot_be_returned()
    {
        var sale = CompletedSale();
        sale.Void(TestData.Now, 1, "Nhập sai");

        var ex = Assert.Throws<DomainException>(() => Return(sale, new ReturnItem(Line(sale, 101), 1)));

        Assert.Equal("return.sale", ex.Code);
    }

    [Fact]
    public void Sale_with_returns_cannot_be_voided()
    {
        var sale = CompletedSale();
        Return(sale, new ReturnItem(Line(sale, 102), 1));

        var ex = Assert.Throws<DomainException>(() => sale.Void(TestData.Now, 1, "Hủy"));

        Assert.Equal("sale.void", ex.Code);
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [Fact]
    public void Earned_points_are_reversed_in_proportion_and_fully_at_the_end()
    {
        var sale = CompletedSale(TestData.Customer());
        Assert.Equal(4, sale.PointsEarned);

        var first = Return(sale, new ReturnItem(Line(sale, 101), 1));   // 9.000 / 45.000 → floor(0.8) = 0
        var second = Return(sale, new ReturnItem(Line(sale, 101), 2));  // 27.000 / 45.000 → floor(2.4) = 2
        var last = Return(sale, new ReturnItem(Line(sale, 102), 4));    // everything → 4

        Assert.Equal(0, first.PointsReversed);
        Assert.Equal(2, second.PointsReversed);
        Assert.Equal(2, last.PointsReversed);
        Assert.Equal(4, sale.PointsReversed);
        Assert.Equal(45_000m, sale.RefundedTotal);
    }

    [Fact]
    public void Redeemed_points_come_back_only_when_whole_invoice_is_returned()
    {
        var sale = CompletedSale(TestData.Customer(points: 50), redeem: 20); // 2.000 off

        var partial = Return(sale, new ReturnItem(Line(sale, 101), 3));
        var rest = Return(sale, new ReturnItem(Line(sale, 102), 4));

        Assert.Equal(0, partial.PointsRestored);
        Assert.Equal(20, rest.PointsRestored);
        Assert.True(sale.RedeemedPointsRestored);
        Assert.Equal(sale.Total, sale.RefundedTotal);
    }

    [Fact]
    public void Written_off_lines_are_valued_separately_from_restocked_lines()
    {
        var sale = CompletedSale();

        var result = Return(sale, new ReturnItem(Line(sale, 101), 1, Restock: false), new ReturnItem(Line(sale, 102), 2));

        Assert.Equal(7_000m, result.WrittenOffCost);
        Assert.Equal(7_000m, result.RestockedCost);
        Assert.Equal(9_000m + 9_000m, result.RefundTotal);
    }

    [Fact]
    public void Weighed_goods_can_be_returned_by_fraction()
    {
        var sale = new Sale("HD261012-0003", cashierId: 1, TestData.Now);
        var pork = sale.AddItem(TestData.Product(5, 150_000m, name: "Thịt ba chỉ"), 0.8m).WithId(1); // 120.000
        sale.AddPayment(PaymentMethod.Cash, 120_000m);
        sale.Complete(TestData.Now, LoyaltyPolicy.Default);

        var result = Return(sale, new ReturnItem(pork, 0.3m));

        Assert.Equal(45_000m, result.RefundTotal);
        Assert.Equal(0.5m, pork.RemainingQuantity);
    }
}
