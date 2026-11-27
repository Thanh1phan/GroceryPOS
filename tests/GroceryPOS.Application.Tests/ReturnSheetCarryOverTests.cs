using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

/// <summary><see cref="ReturnSheet.CopySelectionFrom"/>: keeping the cashier's picks when the invoice is reloaded.</summary>
public class ReturnSheetCarryOverTests
{
    private static ReturnableSaleDto Sale(decimal milkReturned = 0, decimal noodlesReturned = 0, int id = 1) => new(
        id, "HD261127-0001", DateTimeOffset.UnixEpoch, null, null, 50_000, 0, 0, 0, true, true,
        [
            new ReturnableLineDto(11, 1, "SUA01", "Sữa", "hộp", 3, milkReturned, 3 - milkReturned, 30_000, (3 - milkReturned) * 10_000),
            new ReturnableLineDto(12, 2, "MI01", "Mì", "gói", 4, noodlesReturned, 4 - noodlesReturned, 20_000, (4 - noodlesReturned) * 5_000),
        ]);

    [Fact]
    public void Picks_restock_reason_and_method_carry_over_unchanged_when_nothing_moved()
    {
        var old = new ReturnSheet(Sale());
        old.SetQuantity(11, 2);
        old.SetRestock(11, false);
        old.SetReason("Hàng lỗi");
        old.SetRefundMethod(PaymentMethod.BankTransfer);
        var fresh = new ReturnSheet(Sale());
        var changed = 0;
        fresh.Changed += (_, _) => changed++;

        var reduced = fresh.CopySelectionFrom(old);

        Assert.Equal(0, reduced);
        Assert.Equal(2, fresh.LineOf(11).Quantity);
        Assert.False(fresh.LineOf(11).Restock);
        Assert.Equal(0, fresh.LineOf(12).Quantity);
        Assert.Equal("Hàng lỗi", fresh.Reason);
        Assert.Equal(PaymentMethod.BankTransfer, fresh.RefundMethod);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Picks_are_clamped_to_what_another_return_left_and_counted()
    {
        var old = new ReturnSheet(Sale());
        old.ReturnAll();
        var fresh = new ReturnSheet(Sale(milkReturned: 2, noodlesReturned: 4));

        var reduced = fresh.CopySelectionFrom(old);

        Assert.Equal(2, reduced);
        Assert.Equal(1, fresh.LineOf(11).Quantity);
        Assert.Equal(0, fresh.LineOf(12).Quantity);
        Assert.Equal(10_000, fresh.RefundTotal);
    }

    [Fact]
    public void Another_invoice_is_rejected()
    {
        var fresh = new ReturnSheet(Sale());
        Assert.Throws<ArgumentException>(() => fresh.CopySelectionFrom(new ReturnSheet(Sale(id: 2))));
    }
}
