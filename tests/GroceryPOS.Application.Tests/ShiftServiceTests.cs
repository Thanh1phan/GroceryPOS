using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Tests;

public class ShiftServiceTests
{
    private const int CashierId = 7;
    private const int ManagerId = 9;

    private readonly InMemoryShiftRepository _shifts = new();
    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemorySaleReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly InMemoryUserRepository _users = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 09:00 in Vietnam
    private readonly Product _milk;

    public ShiftServiceTests()
    {
        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(100);
        _products.Add(_milk);

        var role = new Role(SystemRoles.Cashier, null, isSystem: true);
        for (var id = 1; id <= ManagerId; id++)
        {
            _users.Add(new User($"user{id}", id == CashierId ? "Trần Thị B" : $"Nhân viên {id}", "hashed:x", role));
        }
    }

    private ShiftService CreateSut() =>
        new(_shifts, _sales, _returns, _users, _uow, _session, _audit, _clock);

    private void SignInCashier() =>
        SignIn(CashierId, Permissions.ShiftsManage, Permissions.SalesCreate, Permissions.ReturnsCreate);

    private void SignIn(int userId, params string[] permissions) =>
        _session.SignIn(new UserSession(userId, $"user{userId}", "Người dùng", SystemRoles.Cashier, permissions.ToHashSet(), _clock.Now));

    private async Task<ShiftSummaryDto> OpenAsync(decimal openingFloat = 500_000)
    {
        var result = await CreateSut().OpenAsync(new OpenShiftRequest(openingFloat));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Sells milk as the signed-in user; the customer pays with the given tenders.</summary>
    private async Task<int> SellAsync(int quantity, params PaymentInput[] payments)
    {
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var result = await checkout.CheckoutAsync(new CheckoutRequest(new[] { new CheckoutLine(_milk.Id, quantity) }, payments));
        Assert.True(result.IsSuccess);
        return result.Value.SaleId;
    }

    [Fact]
    public async Task Open_creates_daily_coded_shift_and_audits()
    {
        SignInCashier();

        var shift = await OpenAsync(500_000);

        Assert.Equal("CA261008-0001", shift.Code);
        Assert.Equal(ShiftStatus.Open, shift.Status);
        Assert.Equal("Trần Thị B", shift.UserName);
        Assert.Equal(500_000m, shift.ExpectedCash);
        Assert.Null(shift.Variance);
        Assert.Contains(AuditActions.ShiftOpened, _audit.Actions);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Cannot_open_a_second_shift()
    {
        SignInCashier();
        await OpenAsync();

        var result = await CreateSut().OpenAsync(new OpenShiftRequest(0));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Single(_shifts.All);
    }

    [Fact]
    public async Task Requires_shift_permission()
    {
        SignIn(CashierId, Permissions.SalesCreate);

        var result = await CreateSut().OpenAsync(new OpenShiftRequest(0));

        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Live_report_counts_sales_by_tender_voids_and_returns()
    {
        SignInCashier();
        await OpenAsync(500_000);

        // 2 × 35.000 = 70.000 paid with 100.000 cash → 70.000 stays in the drawer.
        var cashSale = await SellAsync(2, new PaymentInput(PaymentMethod.Cash, 100_000));
        // 3 × 35.000 = 105.000 by transfer.
        await SellAsync(3, new PaymentInput(PaymentMethod.BankTransfer, 105_000));
        // 1 × 35.000 split 20.000 card + 15.000 cash.
        await SellAsync(1, new PaymentInput(PaymentMethod.Card, 20_000), new PaymentInput(PaymentMethod.Cash, 15_000));
        // 4 × 35.000 = 140.000 cash, voided by a manager-capable cashier → cash handed back.
        SignIn(CashierId, Permissions.ShiftsManage, Permissions.SalesCreate, Permissions.SalesVoid, Permissions.ReturnsCreate);
        var voided = await SellAsync(4, new PaymentInput(PaymentMethod.Cash, 140_000));
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        Assert.True((await checkout.VoidAsync(voided, "Khách đổi ý")).IsSuccess);
        // One milk of the cash sale returned in cash.
        var sale = _sales.All.Single(s => s.Id == cashSale);
        var returns = new ReturnService(_sales, _returns, _products, _movements, _customers, _uow, _session, _audit, _clock);
        Assert.True((await returns.CreateAsync(new CreateReturnRequest(
            cashSale, new[] { new ReturnLineInput(sale.Lines[0].Id, 1) }, "Hộp móp"))).IsSuccess);

        var current = (await CreateSut().GetCurrentAsync()).Value.Current!;

        var totals = current.Totals;
        Assert.Equal(4, totals.SalesCount);
        Assert.Equal(350_000m, totals.SalesTotal);
        Assert.Equal(70_000m + 15_000 + 140_000, totals.CashSales);
        Assert.Equal(105_000m, totals.TransferSales);
        Assert.Equal(20_000m, totals.CardSales);
        Assert.Equal(1, totals.VoidCount);
        Assert.Equal(140_000m, totals.VoidCash);
        Assert.Equal(1, totals.ReturnCount);
        Assert.Equal(35_000m, totals.CashRefunds);
        Assert.Equal(350_000m - 140_000 - 35_000, totals.NetRevenue);
        Assert.Equal(500_000m + 70_000 + 15_000 - 35_000, current.ExpectedCash);
    }

    [Fact]
    public async Task Other_users_sales_and_sales_before_opening_are_not_counted()
    {
        SignInCashier();
        await SellAsync(1, new PaymentInput(PaymentMethod.Cash, 35_000)); // before the shift
        _clock.Advance(TimeSpan.FromMinutes(5));
        await OpenAsync(0);
        SignIn(ManagerId, Permissions.SalesCreate);
        await SellAsync(2, new PaymentInput(PaymentMethod.Cash, 70_000)); // someone else's drawer
        SignInCashier();

        var current = (await CreateSut().GetCurrentAsync()).Value.Current!;

        Assert.Equal(0, current.Totals.SalesCount);
        Assert.Equal(0m, current.ExpectedCash);
    }

    [Fact]
    public async Task Pay_in_and_pay_out_change_expected_cash_and_are_audited()
    {
        SignInCashier();
        await OpenAsync(300_000);
        var sut = CreateSut();

        Assert.True((await sut.RecordCashMovementAsync(new CashMovementRequest(CashMovementType.PayIn, 200_000, "Đổi tiền lẻ"))).IsSuccess);
        var result = await sut.RecordCashMovementAsync(new CashMovementRequest(CashMovementType.PayOut, 120_000, "Trả tiền giao nước"));

        Assert.True(result.IsSuccess);
        Assert.Equal(380_000m, result.Value.ExpectedCash);
        Assert.Equal(2, result.Value.CashMovements.Count);
        Assert.Equal(2, _audit.Actions.Count(a => a == AuditActions.CashMoved));
    }

    [Fact]
    public async Task Pay_out_cannot_exceed_cash_in_drawer()
    {
        SignInCashier();
        await OpenAsync(100_000);

        var result = await CreateSut().RecordCashMovementAsync(new CashMovementRequest(CashMovementType.PayOut, 150_000, "Rút tiền"));

        Assert.True(result.IsFailure);
        Assert.Contains("100.000", result.Error.Message);
        Assert.Empty(_shifts.All[0].CashMovements);
    }

    [Fact]
    public async Task Cash_movement_without_open_shift_fails()
    {
        SignInCashier();

        var result = await CreateSut().RecordCashMovementAsync(new CashMovementRequest(CashMovementType.PayIn, 10_000, "Nộp"));

        Assert.Equal(ShiftErrors.NoOpenShift, result.Error);
    }

    [Fact]
    public async Task Close_by_denomination_reports_variance_and_handover()
    {
        SignInCashier();
        var shift = await OpenAsync(500_000);
        await SellAsync(2, new PaymentInput(PaymentMethod.Cash, 100_000)); // +70.000 cash
        _clock.Advance(TimeSpan.FromHours(8));

        // 500.000 + 50.000 + 20.000 − 1.000 short = 569.000 counted, expected 570.000.
        var result = await CreateSut().CloseAsync(new CloseShiftRequest(
            shift.ShiftId,
            Notes: new Dictionary<decimal, int> { [500_000] = 1, [50_000] = 1, [10_000] = 1, [5_000] = 1, [2_000] = 2 },
            CashLeftInDrawer: 500_000));

        Assert.True(result.IsSuccess);
        var closed = result.Value;
        Assert.Equal(ShiftStatus.Closed, closed.Status);
        Assert.Equal(570_000m, closed.ExpectedCash);
        Assert.Equal(569_000m, closed.CountedCash);
        Assert.Equal(-1_000m, closed.Variance);
        Assert.Equal("Thiếu tiền", closed.VarianceLabel);
        Assert.Equal(69_000m, closed.CashHandedOver);
        Assert.Contains(AuditActions.ShiftClosed, _audit.Actions);

        // Totals are frozen: a later sale does not change the closed report.
        await SellAsync(1, new PaymentInput(PaymentMethod.Cash, 35_000));
        Assert.Equal(1, (await CreateSut().GetSummaryAsync(shift.ShiftId)).Value.Totals.SalesCount);
    }

    [Fact]
    public async Task Close_rejects_mismatched_count_and_unexplained_variance()
    {
        SignInCashier();
        var shift = await OpenAsync(500_000);
        var sut = CreateSut();

        var mismatch = await sut.CloseAsync(new CloseShiftRequest(
            shift.ShiftId, CountedCash: 500_000, Notes: new Dictionary<decimal, int> { [200_000] = 2 }));
        Assert.Equal("validation", mismatch.Error.Code);

        var missing = await sut.CloseAsync(new CloseShiftRequest(shift.ShiftId));
        Assert.Equal(ShiftErrors.CountMissing, missing.Error);

        var unexplained = await sut.CloseAsync(new CloseShiftRequest(shift.ShiftId, CountedCash: 450_000));
        Assert.Equal("shift.variance_note", unexplained.Error.Code);
        Assert.True(_shifts.All[0].IsOpen);
        Assert.DoesNotContain(AuditActions.ShiftClosed, _audit.Actions);

        var explained = await sut.CloseAsync(new CloseShiftRequest(shift.ShiftId, CountedCash: 450_000, Note: "Đã ứng lương 50.000"));
        Assert.True(explained.IsSuccess);
    }

    [Fact]
    public async Task Next_shift_suggests_float_left_in_drawer()
    {
        SignInCashier();
        var shift = await OpenAsync(500_000);
        await CreateSut().CloseAsync(new CloseShiftRequest(shift.ShiftId, CountedCash: 500_000, CashLeftInDrawer: 300_000));

        SignIn(ManagerId, Permissions.ShiftsManage);
        var current = (await CreateSut().GetCurrentAsync()).Value;

        Assert.Null(current.Current);
        Assert.Equal(300_000m, current.SuggestedOpeningFloat);
        Assert.Equal("CA261008-0002", (await OpenAsync(current.SuggestedOpeningFloat)).Code);
    }

    [Fact]
    public async Task Cashier_cannot_see_or_close_another_users_shift_but_supervisor_can()
    {
        SignInCashier();
        var shift = await OpenAsync(200_000);

        SignIn(ManagerId, Permissions.ShiftsManage);
        Assert.Equal(ShiftErrors.NotYourShift, (await CreateSut().GetSummaryAsync(shift.ShiftId)).Error);
        Assert.Equal(ShiftErrors.NotYourShift, (await CreateSut().CloseAsync(new CloseShiftRequest(shift.ShiftId, CountedCash: 200_000))).Error);

        SignIn(ManagerId, Permissions.ShiftsSupervise);
        var closed = await CreateSut().CloseAsync(new CloseShiftRequest(shift.ShiftId, CountedCash: 200_000));

        Assert.True(closed.IsSuccess);
        Assert.Equal(ManagerId, _shifts.All[0].ClosedBy);
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.ShiftClosed && e.Details!.Contains("kết ca hộ"));
    }

    [Fact]
    public async Task List_is_limited_to_own_shifts_without_supervise_permission()
    {
        SignInCashier();
        await OpenAsync();
        SignIn(ManagerId, Permissions.ShiftsManage);
        await OpenAsync();

        var own = await CreateSut().ListAsync(new ShiftFilter());
        Assert.Single(own.Value);
        Assert.Equal(ManagerId, own.Value[0].UserId);

        SignIn(ManagerId, Permissions.ShiftsSupervise);
        var all = await CreateSut().ListAsync(new ShiftFilter(Status: ShiftStatus.Open));
        Assert.Equal(2, all.Value.Count);
        Assert.Contains(all.Value, s => s.UserName == "Trần Thị B");
    }
}
