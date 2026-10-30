using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class SupplierReturnSheetTests
{
    private readonly InMemoryGoodsReceiptRepository _receipts = new();
    private readonly InMemorySupplierRepository _suppliers = new();
    private readonly InMemorySupplierReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();
    private readonly Supplier _vinamilk;
    private readonly Product _milk;
    private readonly Product _yogurt;
    private readonly Product _noodles;

    public SupplierReturnSheetTests()
    {
        _vinamilk = new Supplier("NCC01", "Công ty Vinamilk");
        _suppliers.Add(_vinamilk);

        _milk = new Product("SUA01", "Sữa tươi Vinamilk 180ml", 1, "hộp", costPrice: 7_000, sellPrice: 9_000);
        _milk.SetBarcode("8934673000017");
        _milk.AdjustStock(20);
        _products.Add(_milk);

        _yogurt = new Product("SC01", "Sữa chua Vinamilk", 1, "hộp", costPrice: 5_000, sellPrice: 7_000);
        _products.Add(_yogurt);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 1, "gói", costPrice: 3_500, sellPrice: 4_500);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);

        _session.SignIn(new UserSession(
            2, "quanly", "Trần Thị B", SystemRoles.Manager,
            new[] { Permissions.PurchasesManage, Permissions.SupplierPaymentsManage }.ToHashSet(), _clock.Now));
    }

    private SupplierReturnService Returns() =>
        new(_returns, _receipts, _suppliers, _products, _movements, _uow, _session, _audit, _clock);

    private IReadOnlyList<StockItem> Catalog() => _products.All
        .Select(p => new StockItem(p.Id, p.Code, p.Barcode, p.Name, p.CategoryId, "Sữa", p.Unit, p.StockQuantity, p.MinStockLevel, p.CostPrice, p.SellPrice, p.IsActive))
        .ToList();

    /// <summary>30 milk at 8.000 ₫ and 10 yogurt at 5.500 ₫ from Vinamilk, paid 100.000 ₫ → debt 195.000 ₫.</summary>
    private async Task<ReturnableReceiptDto> ReceiveAsync()
    {
        var service = new GoodsReceiptService(_receipts, _suppliers, _products, _movements, _uow, _session, _audit, _clock);
        var draft = await service.SaveDraftAsync(new SaveGoodsReceiptRequest(
            null,
            _vinamilk.Id,
            new[] { new GoodsReceiptLineInput(_milk.Id, 30, 8_000), new GoodsReceiptLineInput(_yogurt.Id, 10, 5_500) },
            PaidAmount: 100_000));
        Assert.True((await service.CompleteAsync(draft.Value.Id)).IsSuccess);
        return (await Returns().GetReturnableAsync(draft.Value.Code)).Value;
    }

    [Fact]
    public async Task Receipt_mode_lists_lines_with_caps_and_landed_cost()
    {
        var receipt = await ReceiveAsync();
        _yogurt.AdjustStock(-6); // 4 yogurts left on the shelf
        var sheet = SupplierReturnSheet.FromReceipt((await Returns().GetReturnableAsync(receipt.Code)).Value, Catalog());

        Assert.True(sheet.IsAgainstReceipt);
        Assert.Equal(195_000m, sheet.SupplierDebt);
        var milk = sheet.LineOf(_milk.Id);
        var yogurt = sheet.LineOf(_yogurt.Id);
        Assert.Equal(30m, milk.MaxQuantity);
        Assert.Equal(8_000m, milk.DefaultUnitPrice);
        Assert.Equal(7_600m, milk.CostPrice); // (20 × 7.000 + 30 × 8.000) / 50
        Assert.Equal(4m, yogurt.MaxQuantity);
        Assert.Equal("Tồn chỉ còn 4", yogurt.LimitText);

        sheet.SetQuantity(_yogurt.Id, 9);
        Assert.Equal(4m, yogurt.Quantity);
        sheet.ToggleLine(_milk.Id);
        Assert.Equal(30m, milk.Quantity);
        sheet.ToggleLine(_milk.Id);
        Assert.Equal(0m, milk.Quantity);

        Assert.Equal("Vui lòng nhập lý do trả hàng.", sheet.Blocker);
        Assert.Equal(22_000m, sheet.Total);
        Assert.Equal(22_000m, sheet.CostValue); // yogurt cost became 5.500 (no earlier stock)
        Assert.Equal(0m, sheet.CostDifference);
    }

    [Fact]
    public async Task Receipt_mode_scan_refuses_products_not_on_the_receipt_and_caps_at_maximum()
    {
        var receipt = await ReceiveAsync();
        var sheet = SupplierReturnSheet.FromReceipt(receipt, Catalog());

        Assert.Equal(SupplierReturnScanOutcome.NotOnReceipt, sheet.Scan("MI01").Outcome);
        Assert.Equal(SupplierReturnScanOutcome.NotFound, sheet.Scan("XYZ").Outcome);
        Assert.Equal(SupplierReturnScanOutcome.Empty, sheet.Scan("  ").Outcome);

        var first = sheet.Scan("8934673000017");
        var more = sheet.Scan("40*sua01");
        var full = sheet.Scan("SUA01");

        Assert.Equal(SupplierReturnScanOutcome.Added, first.Outcome);
        Assert.Equal(SupplierReturnScanOutcome.Increased, more.Outcome);
        Assert.Equal(30m, sheet.LineOf(_milk.Id).Quantity);
        Assert.Equal(SupplierReturnScanOutcome.AtMaximum, full.Outcome);

        Assert.True(sheet.Remove(_milk.Id));
        Assert.Equal(2, sheet.Lines.Count); // receipt lines stay, quantity reset
        Assert.Equal(0, sheet.SelectedLineCount);
    }

    [Fact]
    public async Task Price_cannot_exceed_landed_cost_and_must_be_whole_dong()
    {
        var receipt = await ReceiveAsync();
        var sheet = SupplierReturnSheet.FromReceipt(receipt, Catalog());
        sheet.SetQuantity(_milk.Id, 10);

        Assert.True(sheet.SetUnitPrice(_milk.Id, 8_001).IsFailure);
        Assert.True(sheet.SetUnitPrice(_milk.Id, 7_500.5m).IsFailure);
        Assert.True(sheet.SetUnitPrice(_milk.Id, 7_000).IsSuccess);
        Assert.True(sheet.LineOf(_milk.Id).PriceChanged);
        Assert.Equal(70_000m, sheet.Total);
        Assert.Equal(-6_000m, sheet.CostDifference); // 10 × (7.000 − 7.600)
    }

    [Fact]
    public async Task Auto_settle_offsets_debt_first_and_refunds_the_rest()
    {
        var receipt = await ReceiveAsync();
        var payments = new SupplierPaymentService(new InMemorySupplierPaymentRepository(), _suppliers, _receipts, _returns, _uow, _session, _audit, _clock);
        Assert.True((await payments.PayAsync(new SupplierPaymentRequest(_vinamilk.Id, 150_000))).IsSuccess); // debt 45.000
        var sheet = SupplierReturnSheet.FromReceipt((await Returns().GetReturnableAsync(receipt.Code)).Value, Catalog());

        sheet.SetQuantity(_milk.Id, 10); // 80.000
        Assert.Equal(45_000m, sheet.DebtOffset);
        Assert.Equal(35_000m, sheet.RefundAmount);
        Assert.Equal(0m, sheet.DebtAfter);

        sheet.SetRefundAmount(20_000);
        Assert.False(sheet.AutoSettle);
        sheet.SetReason("Hàng lỗi / hư hỏng");
        Assert.StartsWith("Công nợ với \"Công ty Vinamilk\" chỉ còn 45.000", sheet.Blocker);

        sheet.RefundEverything();
        Assert.Equal(0m, sheet.DebtOffset);
        Assert.Null(sheet.Blocker);

        sheet.SetRefundAmount(90_000);
        Assert.StartsWith("Tiền NCC hoàn lại không được vượt quá", sheet.Blocker);

        sheet.SetAutoSettle(true);
        Assert.Equal(35_000m, sheet.RefundAmount);
        sheet.SetRefundMethod(PaymentMethod.BankTransfer);
        Assert.Contains("nhận lại 35.000 ₫ chuyển khoản", sheet.Summary);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.SetRefundMethod(PaymentMethod.Card));
    }

    [Fact]
    public async Task Receipt_mode_preview_matches_what_the_service_posts()
    {
        var receipt = await ReceiveAsync();
        var sheet = SupplierReturnSheet.FromReceipt(receipt, Catalog());
        sheet.SetQuantity(_milk.Id, 12);
        sheet.SetUnitPrice(_milk.Id, 7_800);
        sheet.SetQuantity(_yogurt.Id, 2);
        sheet.SetReason("Hàng cận / hết hạn");
        sheet.SetNote("NCC nhận tại cửa hàng");

        var request = sheet.BuildRequest();
        Assert.True(request.IsSuccess);
        Assert.Null(request.Value.Lines.Single(l => l.ProductId == _yogurt.Id).UnitPrice); // default price is not sent
        Assert.Null(request.Value.RefundMethod);

        var posted = await Returns().CreateAsync(request.Value);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.Message : null);
        Assert.Equal(sheet.Total, posted.Value.Return.Total);
        Assert.Equal(sheet.CostValue, posted.Value.Return.CostValue);
        Assert.Equal(sheet.DebtOffset, posted.Value.Return.DebtOffset);
        Assert.Equal(sheet.RefundAmount, posted.Value.Return.RefundAmount);
        Assert.Equal(sheet.DebtAfter, posted.Value.SupplierDebtBalance);
        Assert.Equal(receipt.Code, posted.Value.Return.GoodsReceiptCode);

        // A second sheet on the same receipt sees what is left.
        var again = SupplierReturnSheet.FromReceipt((await Returns().GetReturnableAsync(receipt.Code)).Value, Catalog());
        Assert.Equal(18m, again.LineOf(_milk.Id).MaxQuantity);
        Assert.Equal(12m, again.LineOf(_milk.Id).ReturnedBefore);
    }

    [Fact]
    public async Task Free_return_adds_products_from_stock_at_rounded_cost_and_posts()
    {
        await ReceiveAsync();
        var sheet = SupplierReturnSheet.ForSupplier(_vinamilk.Id, _vinamilk.Name, _vinamilk.DebtBalance, Catalog());

        Assert.False(sheet.IsAgainstReceipt);
        Assert.Equal("Quét mã hoặc tìm sản phẩm cần trả nhà cung cấp.", sheet.Blocker);
        var scanned = sheet.Scan("5*MI01");
        Assert.Equal(SupplierReturnScanOutcome.Added, scanned.Outcome);
        Assert.Equal(5m, scanned.Line!.Quantity);
        Assert.Equal(3_500m, scanned.Line.UnitPrice);
        Assert.Null(scanned.Line.MaxUnitPrice);
        Assert.True(sheet.SetUnitPrice(_noodles.Id, 9_000).IsSuccess); // no receipt cap

        sheet.SetQuantity(_noodles.Id, 500);
        Assert.Equal(100m, sheet.LineOf(_noodles.Id).Quantity);
        sheet.SetQuantity(_noodles.Id, 4);
        sheet.SetUnitPrice(_noodles.Id, 3_000);
        sheet.SetReason("Hàng bán chậm");

        var posted = await Returns().CreateAsync(sheet.BuildRequest().Value);

        Assert.True(posted.IsSuccess, posted.IsFailure ? posted.Error.Message : null);
        Assert.Null(posted.Value.Return.GoodsReceiptCode);
        Assert.Equal(12_000m, posted.Value.Return.Total);
        Assert.Equal(sheet.DebtAfter, posted.Value.SupplierDebtBalance);
        Assert.Equal(96m, _noodles.StockQuantity);

        Assert.True(sheet.Remove(_noodles.Id));
        Assert.Empty(sheet.Lines);
    }

    [Fact]
    public void Free_return_refuses_out_of_stock_products()
    {
        var sheet = SupplierReturnSheet.ForSupplier(_vinamilk.Id, _vinamilk.Name, 0, Catalog());

        var result = sheet.Scan("SC01");

        Assert.Equal(SupplierReturnScanOutcome.AtMaximum, result.Outcome);
        Assert.Empty(sheet.Lines);

        // No debt: the whole value comes back in money.
        sheet.Scan("MI01");
        Assert.Equal(3_500m, sheet.RefundAmount);
        Assert.Equal(0m, sheet.DebtOffset);
    }
}
