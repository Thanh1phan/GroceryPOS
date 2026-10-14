using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Tests;

public class SupplierPaymentTests
{
    private static Supplier SupplierOwing(decimal debt)
    {
        var supplier = new Supplier("NCC01", "Công ty Vinamilk").WithId(1);
        supplier.AddDebt(debt);
        return supplier;
    }

    [Fact]
    public void Pay_reduces_debt_and_records_balances()
    {
        var supplier = SupplierOwing(500_000);

        var payment = SupplierPayment.Pay("pc261014-0001", supplier, 200_000, PaymentMethod.BankTransfer, TestData.Now, 3, " FT2610 ", null);

        Assert.Equal("PC261014-0001", payment.Code);
        Assert.Equal(300_000m, supplier.DebtBalance);
        Assert.Equal(500_000m, payment.DebtBefore);
        Assert.Equal(300_000m, payment.DebtAfter);
        Assert.Equal("FT2610", payment.Reference);
        Assert.False(payment.IsCancelled);
    }

    [Fact]
    public void Pay_rejects_card_overpayment_zero_and_fractional_amounts()
    {
        var supplier = SupplierOwing(100_000);

        Assert.Throws<DomainException>(() => SupplierPayment.Pay("PC1", supplier, 10_000, PaymentMethod.Card, TestData.Now, 3));
        var over = Assert.Throws<DomainException>(() => SupplierPayment.Pay("PC1", supplier, 100_001, PaymentMethod.Cash, TestData.Now, 3));
        Assert.Contains("100.000 ₫", over.Message);
        Assert.Throws<DomainException>(() => SupplierPayment.Pay("PC1", supplier, 0, PaymentMethod.Cash, TestData.Now, 3));
        Assert.Throws<DomainException>(() => SupplierPayment.Pay("PC1", supplier, 1_000.5m, PaymentMethod.Cash, TestData.Now, 3));
        Assert.Throws<DomainException>(() => SupplierPayment.Pay("PC1", supplier, 1_000, PaymentMethod.Cash, TestData.Now, userId: 0));
        Assert.Equal(100_000m, supplier.DebtBalance);
    }

    [Fact]
    public void Cancel_restores_debt_once_and_requires_reason()
    {
        var supplier = SupplierOwing(500_000);
        var payment = SupplierPayment.Pay("PC1", supplier, 200_000, PaymentMethod.Cash, TestData.Now, 3);

        Assert.Throws<DomainException>(() => payment.Cancel(supplier, TestData.Now.AddMinutes(5), 1, "  "));
        Assert.Throws<DomainException>(() => payment.Cancel(SupplierOwing(0).WithId(2), TestData.Now.AddMinutes(5), 1, "Nhập nhầm"));

        payment.Cancel(supplier, TestData.Now.AddMinutes(5), 1, "Nhập nhầm số tiền");

        Assert.True(payment.IsCancelled);
        Assert.Equal("Nhập nhầm số tiền", payment.CancelReason);
        Assert.Equal(500_000m, supplier.DebtBalance);
        Assert.Throws<DomainException>(() => payment.Cancel(supplier, TestData.Now.AddMinutes(6), 1, "Lần nữa"));
        Assert.Equal(500_000m, supplier.DebtBalance);
    }

    [Fact]
    public void OffsetDebt_cannot_exceed_balance()
    {
        var supplier = SupplierOwing(50_000);

        supplier.OffsetDebt(20_000);

        Assert.Equal(30_000m, supplier.DebtBalance);
        Assert.Throws<DomainException>(() => supplier.OffsetDebt(30_001));
        Assert.Throws<DomainException>(() => supplier.OffsetDebt(0));
    }

    [Fact]
    public void Money_format_uses_vietnamese_grouping()
    {
        Assert.Equal("1.250.000 ₫", Money.Format(1_250_000));
        Assert.Equal("0 ₫", Money.Format(0));
    }
}

public class SupplierReturnTests
{
    private static readonly IReadOnlyDictionary<int, decimal> NothingReturned = new Dictionary<int, decimal>();

    private readonly Supplier _supplier = new Supplier("NCC01", "Công ty Vinamilk").WithId(1);
    private readonly Product _milk = TestData.Product(1, sellPrice: 9_000);
    private readonly Product _noodles = TestData.Product(2, sellPrice: 4_500);
    private readonly Dictionary<int, Product> _products;
    private readonly GoodsReceipt _receipt;

    public SupplierReturnTests()
    {
        _products = new Dictionary<int, Product> { [1] = _milk, [2] = _noodles };
        _receipt = new GoodsReceipt("PN261011-0001", supplierId: 1, createdById: 2, TestData.Now).WithId(7);
        _receipt.AddLine(_milk, 48, 7_200, discountAmount: 9_600); // landed 7.000
        _receipt.AddLine(_noodles, 30, 3_400);                      // landed 3.400
        _receipt.Complete(_products, TestData.Now, 2);
        _supplier.AddDebt(_receipt.DebtAmount);                     // 438.000
    }

    private SupplierReturnPosting Return(
        IReadOnlyList<SupplierReturnItem> items,
        GoodsReceipt? receipt = null,
        IReadOnlyDictionary<int, decimal>? returned = null,
        decimal refund = 0,
        PaymentMethod? method = null,
        string reason = "Hàng cận date",
        Supplier? supplier = null) =>
        SupplierReturn.Create(
            "tn261014-0001",
            supplier ?? _supplier,
            receipt,
            returned ?? NothingReturned,
            items,
            _products,
            refund,
            method,
            reason,
            note: null,
            TestData.Now.AddDays(3),
            userId: 5);

    [Fact]
    public void Return_against_receipt_uses_landed_cost_takes_stock_out_and_offsets_debt()
    {
        var posting = Return(new[] { new SupplierReturnItem(1, 6) }, _receipt);
        var ret = posting.Return;

        Assert.Equal("TN261014-0001", ret.Code);
        Assert.Equal("PN261011-0001", ret.GoodsReceiptCode);
        Assert.Equal(7, ret.GoodsReceiptId);
        Assert.Equal(42_000m, ret.Total);
        Assert.Equal(42_000m, ret.DebtOffset);
        Assert.Equal(0m, ret.CostDifference);
        Assert.Equal(396_000m, _supplier.DebtBalance);
        Assert.Equal(42m, _milk.StockQuantity);

        var movement = Assert.Single(posting.Movements);
        Assert.Equal(StockMovementType.SupplierReturn, movement.Type);
        Assert.Equal(-6m, movement.Quantity);
        Assert.Equal(7_000m, movement.UnitCost);
        Assert.Equal("TN261014-0001", movement.ReferenceCode);
    }

    [Fact]
    public void Return_against_receipt_is_limited_to_remaining_quantity_and_landed_price()
    {
        var returned = new Dictionary<int, decimal> { [1] = 45 };

        var ex = Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 4) }, _receipt, returned));
        Assert.Contains("tối đa 3", ex.Message);
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(2, 1, UnitPrice: 3_401) }, _receipt));

        var other = TestData.Product(3, 1_000);
        other.AdjustStock(5);
        _products[3] = other;
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(3, 1) }, _receipt));

        // Nothing was applied by the rejected returns.
        Assert.Equal(48m, _milk.StockQuantity);
        Assert.Equal(438_000m, _supplier.DebtBalance);

        // A lower agreed price is fine.
        var posting = Return(new[] { new SupplierReturnItem(1, 3), new SupplierReturnItem(2, 10, UnitPrice: 3_000) }, _receipt, returned);
        Assert.Equal(21_000m + 30_000m, posting.Return.Total);
        Assert.Equal(-4_000m, posting.Return.CostDifference);
    }

    [Fact]
    public void Return_rejects_receipt_of_other_supplier_or_not_completed()
    {
        var other = new Supplier("NCC02", "Acecook").WithId(2);
        other.AddDebt(1_000_000);
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 1) }, _receipt, supplier: other));

        var draft = new GoodsReceipt("PN261011-0002", supplierId: 1, createdById: 2, TestData.Now).WithId(8);
        draft.AddLine(_milk, 10, 7_000);
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 1) }, draft));
    }

    [Fact]
    public void Free_return_uses_current_cost_and_splits_refund_and_offset()
    {
        var posting = Return(
            new[] { new SupplierReturnItem(2, 10) },
            refund: 4_000,
            method: PaymentMethod.Cash);

        Assert.Null(posting.Return.GoodsReceiptCode);
        Assert.Equal(34_000m, posting.Return.Total);
        Assert.Equal(4_000m, posting.Return.RefundAmount);
        Assert.Equal(PaymentMethod.Cash, posting.Return.RefundMethod);
        Assert.Equal(30_000m, posting.Return.DebtOffset);
        Assert.Equal(408_000m, _supplier.DebtBalance);
        Assert.Equal(20m, _noodles.StockQuantity);
    }

    [Fact]
    public void Offset_beyond_debt_requires_refund_and_nothing_changes_on_failure()
    {
        var supplier = new Supplier("NCC03", "Đại lý nhỏ").WithId(3);
        supplier.AddDebt(20_000);

        var ex = Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 5) }, supplier: supplier)); // 35.000
        Assert.Contains("15.000 ₫", ex.Message);
        Assert.Equal(48m, _milk.StockQuantity);
        Assert.Equal(20_000m, supplier.DebtBalance);

        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 5) }, refund: 15_000, supplier: supplier)); // no method
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 5) }, refund: 35_001, method: PaymentMethod.Cash, supplier: supplier));

        var posting = Return(new[] { new SupplierReturnItem(1, 5) }, refund: 15_000, method: PaymentMethod.BankTransfer, supplier: supplier);
        Assert.Equal(20_000m, posting.Return.DebtOffset);
        Assert.Equal(0m, supplier.DebtBalance);
    }

    [Fact]
    public void Return_rejects_bad_lines()
    {
        Assert.Throws<DomainException>(() => Return(Array.Empty<SupplierReturnItem>()));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 1) }, reason: " "));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 0) }));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 49) }));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 1), new SupplierReturnItem(1, 2) }));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(99, 1) }));
        Assert.Throws<DomainException>(() => Return(new[] { new SupplierReturnItem(1, 1, UnitPrice: -1) }));
        Assert.Equal(48m, _milk.StockQuantity);
    }
}
