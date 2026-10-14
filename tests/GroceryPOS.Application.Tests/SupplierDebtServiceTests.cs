using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class SupplierDebtServiceTests
{
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    private readonly InMemoryGoodsReceiptRepository _receipts = new();
    private readonly InMemorySupplierRepository _suppliers = new();
    private readonly InMemorySupplierPaymentRepository _payments = new();
    private readonly InMemorySupplierReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 09:00 in Vietnam
    private readonly Supplier _vinamilk;
    private readonly Supplier _acecook;
    private readonly Product _milk;
    private readonly Product _noodles;

    public SupplierDebtServiceTests()
    {
        _vinamilk = new Supplier("NCC01", "Công ty Vinamilk");
        _acecook = new Supplier("NCC02", "Acecook Việt Nam");
        _suppliers.Add(_vinamilk);
        _suppliers.Add(_acecook);

        _milk = new Product("SUA01", "Sữa tươi Vinamilk 180ml", 1, "hộp", costPrice: 7_000, sellPrice: 9_000);
        _milk.AdjustStock(20);
        _products.Add(_milk);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 4_500);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        SignIn(Permissions.PurchasesManage, Permissions.SupplierPaymentsManage);
    }

    private SupplierPaymentService Payments() =>
        new(_payments, _suppliers, _receipts, _returns, _uow, _session, _audit, _clock);

    private SupplierReturnService Returns() =>
        new(_returns, _receipts, _suppliers, _products, _movements, _uow, _session, _audit, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(2, "quanly", "Trần Thị B", SystemRoles.Manager, permissions.ToHashSet(), _clock.Now));

    /// <summary>Receives 30 boxes of milk at 8.000 ₫ from Vinamilk, paying 100.000 ₫ → debt 140.000 ₫.</summary>
    private async Task<GoodsReceiptDto> ReceiveMilkAsync()
    {
        var receipts = new GoodsReceiptService(_receipts, _suppliers, _products, _movements, _uow, _session, _audit, _clock);
        var draft = await receipts.SaveDraftAsync(new SaveGoodsReceiptRequest(
            null, _vinamilk.Id, new[] { new GoodsReceiptLineInput(_milk.Id, 30, 8_000) }, PaidAmount: 100_000));
        Assert.True((await receipts.CompleteAsync(draft.Value.Id)).IsSuccess);
        return draft.Value;
    }

    [Fact]
    public async Task Pay_reduces_debt_numbers_daily_and_audits()
    {
        await ReceiveMilkAsync();
        _clock.Advance(TimeSpan.FromDays(1));

        var result = await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 50_000, PaymentMethod.BankTransfer, "FT123"));

        Assert.True(result.IsSuccess);
        Assert.Equal("PC261009-0001", result.Value.Code);
        Assert.Equal("Chuyển khoản", result.Value.MethodName);
        Assert.Equal(140_000m, result.Value.DebtBefore);
        Assert.Equal(90_000m, result.Value.DebtAfter);
        Assert.Equal(90_000m, _vinamilk.DebtBalance);
        Assert.Contains(AuditActions.SupplierPaid, _audit.Actions);

        var second = await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 10_000));
        Assert.Equal("PC261009-0002", second.Value.Code);
        Assert.Equal(80_000m, _vinamilk.DebtBalance);
    }

    [Fact]
    public async Task Pay_validates_permission_supplier_debt_and_amount()
    {
        await ReceiveMilkAsync();
        var savesBefore = _uow.SaveCount;

        SignIn(Permissions.PurchasesManage); // storekeeper: can view payables but not pay
        Assert.Equal(SupplierDebtErrors.PaymentForbidden, (await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 1_000))).Error);
        Assert.True((await Payments().ListPayablesAsync()).IsSuccess);

        SignIn(Permissions.SalesCreate);
        Assert.Equal(SupplierDebtErrors.ViewForbidden, (await Payments().ListPayablesAsync()).Error);

        SignIn(Permissions.SupplierPaymentsManage);
        var sut = Payments();
        Assert.Equal(PurchasingErrors.SupplierNotFound, (await sut.PayAsync(new SupplierPaymentRequest(99, 1_000))).Error);
        Assert.Equal(SupplierDebtErrors.NoDebt, (await sut.PayAsync(new SupplierPaymentRequest(_acecook.Id, 1_000))).Error);
        Assert.Equal("supplier.payment", (await sut.PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 140_001))).Error.Code);
        Assert.Equal("supplier_payment.method", (await sut.PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 1_000, PaymentMethod.Card))).Error.Code);

        Assert.Empty(_payments.All);
        Assert.Equal(140_000m, _vinamilk.DebtBalance);
        Assert.Equal(savesBefore, _uow.SaveCount);
    }

    [Fact]
    public async Task Cancel_payment_restores_debt_and_cannot_repeat()
    {
        await ReceiveMilkAsync();
        var paid = await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 40_000));

        var noReason = await Payments().CancelAsync(paid.Value.Id, null);
        var cancelled = await Payments().CancelAsync(paid.Value.Id, "Chuyển khoản bị hoàn");
        var again = await Payments().CancelAsync(paid.Value.Id, "Lần 2");

        Assert.Equal("supplier_payment.reason", noReason.Error.Code);
        Assert.True(cancelled.Value.IsCancelled);
        Assert.Equal("Chuyển khoản bị hoàn", cancelled.Value.CancelReason);
        Assert.Equal(140_000m, _vinamilk.DebtBalance);
        Assert.Equal("supplier_payment.cancelled", again.Error.Code);
        Assert.Contains(AuditActions.SupplierPaymentCancelled, _audit.Actions);
        Assert.Equal(SupplierDebtErrors.PaymentNotFound, (await Payments().CancelAsync(99, "x")).Error);
    }

    [Fact]
    public async Task Payables_list_only_suppliers_with_debt_largest_first()
    {
        await ReceiveMilkAsync();
        _acecook.AddDebt(500_000);

        var result = await Payments().ListPayablesAsync();

        Assert.Equal(new[] { "NCC02", "NCC01" }, result.Value.Select(p => p.Code));
        Assert.Equal(500_000m, result.Value[0].DebtBalance);
    }

    [Fact]
    public async Task Returnable_receipt_shows_remaining_quantities_and_return_updates_them()
    {
        var receipt = await ReceiveMilkAsync();
        _clock.Advance(TimeSpan.FromDays(2));
        var sut = Returns();

        var before = await sut.GetReturnableAsync(" " + receipt.Code.ToLowerInvariant());
        Assert.True(before.Value.CanReturn);
        var line = Assert.Single(before.Value.Lines);
        Assert.Equal(30m, line.RemainingQuantity);
        Assert.Equal(8_000m, line.LandedUnitCost);
        Assert.Equal(140_000m, before.Value.SupplierDebtBalance);

        var created = await sut.CreateAsync(new CreateSupplierReturnRequest(
            _vinamilk.Id, new[] { new SupplierReturnLineInput(_milk.Id, 5) }, "Hộp bị móp", GoodsReceiptId: receipt.Id));

        Assert.True(created.IsSuccess);
        var slip = created.Value.Return;
        Assert.Equal("TN261010-0001", slip.Code);
        Assert.Equal(receipt.Code, slip.GoodsReceiptCode);
        Assert.Equal(40_000m, slip.Total);
        Assert.Equal(40_000m, slip.DebtOffset);
        Assert.Equal(100_000m, created.Value.SupplierDebtBalance);
        Assert.Equal(45m, created.Value.StockAfter[_milk.Id]);
        Assert.Equal(7_600m, slip.Lines[0].CostPrice); // weighted average after the receipt
        Assert.Equal(2_000m, slip.CostDifference);
        Assert.Contains(_movements.All, m => m.Type == StockMovementType.SupplierReturn && m.ReferenceCode == slip.Code);
        Assert.Contains(AuditActions.SupplierReturned, _audit.Actions);

        var after = await sut.GetReturnableAsync(receipt.Code);
        Assert.Equal(5m, after.Value.Lines[0].ReturnedQuantity);
        Assert.Equal(25m, after.Value.Lines[0].MaxReturnable);

        var tooMany = await sut.CreateAsync(new CreateSupplierReturnRequest(
            _vinamilk.Id, new[] { new SupplierReturnLineInput(_milk.Id, 26) }, "Trả thêm", GoodsReceiptId: receipt.Id));
        Assert.Equal("supplier_return.quantity", tooMany.Error.Code);
        Assert.Equal(45m, _milk.StockQuantity);
    }

    [Fact]
    public async Task Free_return_with_refund_and_validation_errors()
    {
        await ReceiveMilkAsync();
        _acecook.AddDebt(20_000);
        var sut = Returns();

        var tooMuchOffset = await sut.CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, new[] { new SupplierReturnLineInput(_noodles.Id, 10) }, "Hết hạn"));
        Assert.Equal("supplier_return.offset", tooMuchOffset.Error.Code);

        var created = await sut.CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, new[] { new SupplierReturnLineInput(_noodles.Id, 10) }, "Hết hạn", RefundAmount: 15_000, RefundMethod: PaymentMethod.Cash));

        Assert.True(created.IsSuccess);
        Assert.Null(created.Value.Return.GoodsReceiptCode);
        Assert.Equal(35_000m, created.Value.Return.Total);
        Assert.Equal("Tiền mặt", created.Value.Return.RefundMethodName);
        Assert.Equal(0m, created.Value.SupplierDebtBalance);
        Assert.Equal(90m, _noodles.StockQuantity);

        Assert.Equal("supplier_return.empty", (await sut.CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, Array.Empty<SupplierReturnLineInput>(), "x"))).Error.Code);
        Assert.Equal("not_found", (await sut.CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, new[] { new SupplierReturnLineInput(99, 1) }, "x"))).Error.Code);
        Assert.Equal(PurchasingErrors.ReceiptNotFound, (await sut.CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, new[] { new SupplierReturnLineInput(_noodles.Id, 1) }, "x", GoodsReceiptId: 99))).Error);

        SignIn(Permissions.SupplierPaymentsManage);
        Assert.Equal("forbidden", (await Returns().CreateAsync(new CreateSupplierReturnRequest(
            _acecook.Id, new[] { new SupplierReturnLineInput(_noodles.Id, 1) }, "x"))).Error.Code);
        Assert.Single(_returns.All);
    }

    [Fact]
    public async Task Ledger_rebuilds_opening_running_and_closing_balances()
    {
        var receipt = await ReceiveMilkAsync();                                     // 08/10: +140.000
        _clock.Advance(TimeSpan.FromDays(1));
        var payment = await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 50_000)); // 09/10: −50.000
        _clock.Advance(TimeSpan.FromDays(1));
        await Returns().CreateAsync(new CreateSupplierReturnRequest(                // 10/10: −40.000
            _vinamilk.Id, new[] { new SupplierReturnLineInput(_milk.Id, 5) }, "Hộp bị móp", GoodsReceiptId: receipt.Id));
        _clock.Advance(TimeSpan.FromDays(1));
        await Payments().CancelAsync(payment.Value.Id, "Nhập nhầm");                // 11/10: +50.000
        Assert.Equal(100_000m, _vinamilk.DebtBalance);

        var all = await Payments().GetLedgerAsync(
            _vinamilk.Id, new DateTimeOffset(2026, 10, 1, 0, 0, 0, Vn), new DateTimeOffset(2026, 10, 15, 0, 0, 0, Vn));

        Assert.True(all.IsSuccess);
        Assert.Equal(0m, all.Value.OpeningBalance);
        Assert.Equal(
            new[] { SupplierDebtEntryKind.GoodsReceipt, SupplierDebtEntryKind.Payment, SupplierDebtEntryKind.SupplierReturn, SupplierDebtEntryKind.PaymentCancelled },
            all.Value.Entries.Select(e => e.Kind));
        Assert.Equal(new[] { 140_000m, 90_000m, 50_000m, 100_000m }, all.Value.Entries.Select(e => e.Balance));
        Assert.Equal(190_000m, all.Value.TotalIncrease);
        Assert.Equal(90_000m, all.Value.TotalDecrease);
        Assert.Equal(100_000m, all.Value.ClosingBalance);

        var oneDay = await Payments().GetLedgerAsync(
            _vinamilk.Id, new DateTimeOffset(2026, 10, 9, 0, 0, 0, Vn), new DateTimeOffset(2026, 10, 10, 0, 0, 0, Vn));

        Assert.Equal(140_000m, oneDay.Value.OpeningBalance);
        var entry = Assert.Single(oneDay.Value.Entries);
        Assert.Equal(payment.Value.Code, entry.DocumentCode);
        Assert.Equal(90_000m, oneDay.Value.ClosingBalance);

        Assert.Equal(PurchasingErrors.InvalidPeriod, (await Payments().GetLedgerAsync(_vinamilk.Id, _clock.Now, _clock.Now)).Error);
    }

    [Fact]
    public async Task Search_lists_payments_and_returns_of_a_period()
    {
        var receipt = await ReceiveMilkAsync();
        await Payments().PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 10_000));
        await Returns().CreateAsync(new CreateSupplierReturnRequest(
            _vinamilk.Id, new[] { new SupplierReturnLineInput(_milk.Id, 1) }, "Hỏng", GoodsReceiptId: receipt.Id));

        var filter = new SupplierDocumentFilter(_clock.Now.AddHours(-1), _clock.Now.AddHours(1), _vinamilk.Id);
        var payments = await Payments().SearchAsync(filter);
        var returns = await Returns().SearchAsync(filter);

        Assert.Equal("Công ty Vinamilk", Assert.Single(payments.Value).SupplierName);
        var ret = Assert.Single(returns.Value);
        Assert.Equal(1, ret.LineCount);
        Assert.Equal(8_000m, ret.DebtOffset);
        Assert.Equal(PurchasingErrors.InvalidPeriod, (await Returns().SearchAsync(filter with { From = filter.To })).Error);
        Assert.Equal(ret.Code, (await Returns().GetAsync(ret.Id)).Value.Code);
        Assert.Equal(SupplierDebtErrors.ReturnNotFound, (await Returns().GetAsync(99)).Error);
    }
}
