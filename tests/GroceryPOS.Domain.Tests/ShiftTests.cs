using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Domain.Tests;

public class ShiftTests
{
    /// <summary>500.000 in sales cash, 30.000 voided, 20.000 refunded in cash → net drawer cash 450.000.</summary>
    private static readonly ShiftTotals Totals = ShiftTotals.Empty with
    {
        SalesCount = 12,
        SalesTotal = 800_000,
        CashSales = 500_000,
        TransferSales = 300_000,
        VoidCount = 1,
        VoidTotal = 30_000,
        VoidCash = 30_000,
        ReturnCount = 1,
        CashRefunds = 20_000,
    };

    private static Shift OpenShift(decimal openingFloat = 1_000_000) =>
        new("ca261013-0001", userId: 7, TestData.Now, openingFloat);

    [Fact]
    public void New_shift_is_open_with_float_and_upper_case_code()
    {
        var shift = OpenShift();

        Assert.True(shift.IsOpen);
        Assert.Equal("CA261013-0001", shift.Code);
        Assert.Equal(1_000_000m, shift.OpeningFloat);
        Assert.Null(shift.Variance);
        Assert.Null(shift.Totals);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000.5)]
    public void Opening_float_must_be_whole_and_not_negative(decimal openingFloat)
    {
        var ex = Assert.Throws<DomainException>(() => OpenShift(openingFloat));
        Assert.Equal("shift.float", ex.Code);
    }

    [Fact]
    public void Expected_cash_combines_float_counter_activity_and_manual_movements()
    {
        var shift = OpenShift();
        shift.AddCashMovement(CashMovementType.PayIn, 200_000, "Đổi tiền lẻ", TestData.Now, 7);
        shift.AddCashMovement(CashMovementType.PayOut, 50_000, "Trả tiền đá", TestData.Now, 7);

        Assert.Equal(450_000m, Totals.NetCash);
        Assert.Equal(750_000m, Totals.NetRevenue);
        Assert.Equal(1_000_000m + 450_000 + 200_000 - 50_000, shift.ExpectedCashWith(Totals));
        Assert.Equal(-50_000m, shift.CashMovements[1].SignedAmount);
    }

    [Fact]
    public void Cash_movement_needs_reason_and_positive_amount()
    {
        var shift = OpenShift();

        Assert.Equal("cash.reason", Assert.Throws<DomainException>(
            () => shift.AddCashMovement(CashMovementType.PayOut, 10_000, " ", TestData.Now, 7)).Code);
        Assert.Equal("cash.amount", Assert.Throws<DomainException>(
            () => shift.AddCashMovement(CashMovementType.PayIn, 0, "Nộp", TestData.Now, 7)).Code);
    }

    [Fact]
    public void Close_freezes_totals_and_computes_variance_and_handover()
    {
        var shift = OpenShift();

        shift.Close(Totals, countedCash: 1_448_000, cashLeftInDrawer: 1_000_000, note: null,
            TestData.Now.AddHours(8), userId: 7, varianceNoteThreshold: 5_000);

        Assert.False(shift.IsOpen);
        Assert.Equal(1_450_000m, shift.ExpectedCash);
        Assert.Equal(-2_000m, shift.Variance);
        Assert.Equal(448_000m, shift.CashHandedOver);
        Assert.Same(Totals, shift.Totals);
        Assert.Equal(7, shift.ClosedBy);
    }

    [Fact]
    public void Large_variance_requires_a_note()
    {
        var shift = OpenShift();

        var ex = Assert.Throws<DomainException>(() => shift.Close(Totals, 1_400_000, 0, null, TestData.Now.AddHours(8), 7, 5_000));
        Assert.Equal("shift.variance_note", ex.Code);
        Assert.True(shift.IsOpen);

        shift.Close(Totals, 1_400_000, 0, "Thối nhầm cho khách", TestData.Now.AddHours(8), 7, 5_000);
        Assert.Equal(-50_000m, shift.Variance);
        Assert.Equal("Thối nhầm cho khách", shift.CloseNote);
    }

    [Fact]
    public void Cannot_leave_more_than_counted_or_touch_a_closed_shift()
    {
        var shift = OpenShift();
        Assert.Equal("shift.left", Assert.Throws<DomainException>(
            () => shift.Close(Totals, 1_450_000, 1_500_000, null, TestData.Now.AddHours(8), 7, 5_000)).Code);

        shift.Close(Totals, 1_450_000, 500_000, null, TestData.Now.AddHours(8), 7, 5_000);

        Assert.Equal("shift.closed", Assert.Throws<DomainException>(
            () => shift.AddCashMovement(CashMovementType.PayIn, 10_000, "Nộp", TestData.Now, 7)).Code);
        Assert.Equal("shift.closed", Assert.Throws<DomainException>(
            () => shift.Close(Totals, 1_450_000, 0, null, TestData.Now.AddHours(9), 7, 5_000)).Code);
    }

    [Fact]
    public void Cannot_close_before_opening()
    {
        var shift = OpenShift();
        Assert.Equal("shift.time", Assert.Throws<DomainException>(
            () => shift.Close(Totals, 1_450_000, 0, null, TestData.Now.AddMinutes(-1), 7, 5_000)).Code);
    }

    [Fact]
    public void Cash_count_totals_known_denominations()
    {
        var total = CashCount.Total(new Dictionary<decimal, int>
        {
            [500_000] = 2,
            [50_000] = 3,
            [2_000] = 4,
            [500] = 1,
        });

        Assert.Equal(1_158_500m, total);
    }

    [Fact]
    public void Cash_count_rejects_unknown_denomination_and_negative_count()
    {
        Assert.Equal("cash.denomination", Assert.Throws<DomainException>(
            () => CashCount.Total(new Dictionary<decimal, int> { [300_000] = 1 })).Code);
        Assert.Equal("cash.count", Assert.Throws<DomainException>(
            () => CashCount.Total(new Dictionary<decimal, int> { [10_000] = -1 })).Code);
    }
}
