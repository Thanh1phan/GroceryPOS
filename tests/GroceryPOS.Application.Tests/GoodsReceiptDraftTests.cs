using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Tests;

public class GoodsReceiptDraftTests
{
    private static StockItem Item(int id, string code, string? barcode, decimal stock, decimal cost, decimal sell, bool active = true) =>
        new(id, code, barcode, $"SP {code}", 1, "Đồ uống", "lon", stock, 0, cost, sell, active);

    private static GoodsReceiptDraft NewDraft(bool canChangePrices = true) => new(
        [
            Item(1, "BIA01", "8934588012112", stock: 10, cost: 11_000, sell: 14_000),
            Item(2, "NUOC01", "8934588063015", stock: 0, cost: 6_000, sell: 8_000),
            Item(3, "CU01", null, stock: 5, cost: 1_000, sell: 2_000, active: false),
        ],
        canChangePrices);

    [Fact]
    public void Starts_blocked_until_supplier_and_lines_are_set()
    {
        var draft = NewDraft();
        Assert.Equal("Chưa chọn nhà cung cấp.", draft.Blocker);
        Assert.False(draft.IsDirty);

        draft.SetSupplier(7);
        Assert.Equal("Phiếu nhập chưa có sản phẩm.", draft.Blocker);

        draft.Add(1);
        Assert.Null(draft.Blocker);
        Assert.True(draft.IsDirty);
        Assert.True(draft.BuildRequest().IsSuccess);
    }

    [Fact]
    public void Scan_adds_at_last_cost_and_increases_with_quantity_prefix()
    {
        var draft = NewDraft();

        var first = draft.Scan("8934588012112");
        var second = draft.Scan("24*bia01");
        var missing = draft.Scan("000");
        var inactive = draft.Scan("CU01");
        var empty = draft.Scan(" ");

        Assert.Equal(GoodsReceiptScanOutcome.Added, first.Outcome);
        Assert.Equal(GoodsReceiptScanOutcome.Increased, second.Outcome);
        Assert.Equal(GoodsReceiptScanOutcome.NotFound, missing.Outcome);
        Assert.Equal(GoodsReceiptScanOutcome.Inactive, inactive.Outcome);
        Assert.Equal(GoodsReceiptScanOutcome.Empty, empty.Outcome);
        var line = Assert.Single(draft.Lines);
        Assert.Equal(25m, line.Quantity);
        Assert.Equal(11_000m, line.UnitCost);
        Assert.Contains("SL 25 lon", second.Message);
        Assert.True(draft.Add(3).IsFailure);
    }

    [Fact]
    public void UpdateLine_validates_like_the_domain_and_rejected_edit_changes_nothing()
    {
        var draft = NewDraft(canChangePrices: false);
        draft.SetSupplier(7);
        draft.Add(1, 10);

        Assert.Equal("Số lượng nhập của \"SP BIA01\" phải lớn hơn 0.", draft.UpdateLine(1, 0, 11_000).Error.Message);
        Assert.Equal("Giá nhập của \"SP BIA01\" không được âm.", draft.UpdateLine(1, 5, -1).Error.Message);
        Assert.Equal("Chiết khấu của \"SP BIA01\" vượt quá thành tiền.", draft.UpdateLine(1, 5, 1_000, 5_001).Error.Message);
        Assert.Equal(PurchasingErrors.PriceChangeNotAllowed, draft.UpdateLine(1, 5, 1_000, 0, 15_000).Error);

        var line = draft.Lines[0];
        Assert.Equal(10m, line.Quantity);
        Assert.Equal(11_000m, line.UnitCost);
        Assert.Null(line.NewSellPrice);

        Assert.True(draft.UpdateLine(1, 12.3456m, 10_999.6m, 1_000).IsSuccess);
        Assert.Equal(12.346m, line.Quantity);
        Assert.Equal(11_000m, line.UnitCost);
        Assert.False(draft.UpdateLine(99, 1, 1).IsSuccess);
    }

    [Fact]
    public void Totals_allocate_the_receipt_discount_and_track_paid_and_debt()
    {
        var draft = NewDraft();
        draft.SetSupplier(7);
        draft.Add(1, 30);                               // 330.000
        draft.Add(2, 24);                               // 144.000
        draft.UpdateLine(2, 24, 6_000, 4_000);          // net 140.000
        draft.SetDiscount(10_000);

        Assert.Equal(474_000m, draft.GrossAmount);
        Assert.Equal(470_000m, draft.Subtotal);
        Assert.Equal(460_000m, draft.Total);
        Assert.Equal(10_000m, draft.Lines.Sum(l => l.AllocatedDiscount));
        Assert.Equal(460_000m, draft.Lines.Sum(l => l.FinalAmount));
        Assert.Equal(460_000m, draft.DebtAmount);

        draft.SetPaidAmount(200_000);
        Assert.Equal(260_000m, draft.DebtAmount);

        draft.SetPayInFull(true);
        Assert.Equal(460_000m, draft.PaidAmount);
        draft.SetDiscount(20_000);
        Assert.Equal(450_000m, draft.PaidAmount);       // follows the total
        Assert.Equal(0m, draft.DebtAmount);

        draft.SetPayInFull(false);
        Assert.Equal(450_000m, draft.PaidAmount);       // keeps the last full amount
        draft.SetDiscount(40_000);
        Assert.Equal(PurchasingErrors.OverPaid.Message, draft.Blocker);
        draft.SetPaidAmount(0);
        draft.SetDiscount(500_000);
        Assert.Equal(PurchasingErrors.DiscountTooLarge.Message, draft.Blocker);
        Assert.Equal(0m, draft.Total);
    }

    [Fact]
    public void Lines_preview_average_cost_margin_and_warnings()
    {
        var draft = NewDraft();
        draft.SetSupplier(7);
        draft.Add(1, 10);
        draft.UpdateLine(1, 10, 14_000);                 // (10×11.000 + 10×14.000) / 20 = 12.500
        draft.Add(2, 12);
        draft.UpdateLine(2, 12, 9_000);                  // no stock → landed cost 9.000 ≥ sell 8.000

        var beer = draft.Find(1)!;
        var water = draft.Find(2)!;
        Assert.Equal(12_500m, beer.NewAverageCost);
        Assert.Equal(20m, beer.StockAfter);
        Assert.Equal(10.7m, beer.MarginPercent);         // (14.000 − 12.500) / 14.000
        Assert.Equal(27.3m, beer.CostChangePercent);
        Assert.False(beer.IsBelowCost);
        Assert.Equal("Giá nhập tăng", beer.StatusName);
        Assert.Equal(9_000m, water.NewAverageCost);
        Assert.True(water.IsBelowCost);
        Assert.Equal("Bán ≤ giá vốn", water.StatusName);
        Assert.Equal([water], draft.BelowCostLines);
        Assert.Equal(2, draft.Warnings.Count);

        draft.UpdateLine(2, 12, 6_500, 0, 11_000);
        Assert.False(water.IsBelowCost);
        Assert.Equal("Đổi giá bán", water.StatusName);
    }

    [Fact]
    public void AddSuggestions_skips_lines_already_there_and_inactive_products()
    {
        var draft = NewDraft();
        draft.Add(1, 5);

        var added = draft.AddSuggestions([
            new ReorderSuggestion(1, "BIA01", "SP BIA01", "lon", 10, 24, 38, 11_000),
            new ReorderSuggestion(2, "NUOC01", "SP NUOC01", "lon", 0, 12, 24, 6_500),
            new ReorderSuggestion(3, "CU01", "SP CU01", "lon", 5, 10, 15, 1_000),
        ]);

        Assert.Equal(1, added);
        Assert.Equal(5m, draft.Find(1)!.Quantity);
        Assert.Equal(24m, draft.Find(2)!.Quantity);
        Assert.Equal(6_500m, draft.Find(2)!.UnitCost);
    }

    [Fact]
    public void FromDto_restores_a_saved_draft_and_MarkSaved_clears_dirty()
    {
        var dto = new GoodsReceiptDto(
            4, "PN261029-0001", GoodsReceiptStatus.Draft, "Nháp", DateTimeOffset.UnixEpoch, 7, "NCC", "HD01", "ghi chú",
            100_000, 0, 100_000, 100_000, 0, null, null,
            [
                new GoodsReceiptLineDto(1, "BIA01", "SP BIA01", "lon", 10, 10_000, 0, 0, 100_000, 10_000, 15_000),
                new GoodsReceiptLineDto(9, "OLD", "SP đã xóa", "cái", 1, 0, 0, 0, 0, 0, null),
            ]);

        var draft = GoodsReceiptDraft.FromDto(NewDraft().Catalog, canChangePrices: true, dto);

        Assert.Equal(4, draft.Id);
        Assert.Equal("PN261029-0001", draft.Code);
        Assert.Equal(7, draft.SupplierId);
        Assert.True(draft.PayInFull);
        Assert.False(draft.IsDirty);
        Assert.Equal(15_000m, draft.Find(1)!.NewSellPrice);
        Assert.Contains("SP đã xóa", draft.Blocker);     // unknown product treated as no longer sold

        draft.Remove(9);
        Assert.True(draft.IsDirty);
        var request = draft.BuildRequest().Value;
        Assert.Equal(4, request.Id);
        Assert.Equal("HD01", request.SupplierInvoiceNo);
        Assert.Equal(100_000m, request.PaidAmount);

        draft.MarkSaved(dto);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public async Task Draft_preview_matches_what_the_service_saves_and_posts()
    {
        var receipts = new InMemoryGoodsReceiptRepository();
        var suppliers = new InMemorySupplierRepository();
        var products = new InMemoryProductRepository();
        var session = new UserSessionStore();
        var clock = new FakeClock();
        var supplier = new Supplier("NCC01", "Công ty Sabeco");
        suppliers.Add(supplier);
        var beer = new Product("BIA01", "Bia Sài Gòn", 1, "lon", costPrice: 11_000, sellPrice: 14_000, VatRate.Ten);
        beer.AdjustStock(7);
        products.Add(beer);
        var water = new Product("NUOC01", "Nước suối", 1, "chai", costPrice: 4_000, sellPrice: 5_000);
        products.Add(water);
        var tea = new Product("TRA01", "Trà xanh", 1, "chai", costPrice: 7_000, sellPrice: 10_000);
        tea.AdjustStock(3);
        products.Add(tea);
        session.SignIn(new UserSession(5, "thukho", "Lê Văn C", SystemRoles.Storekeeper,
            new HashSet<string> { Permissions.PurchasesManage, Permissions.PricesChange }, clock.Now));

        var catalog = new[] { beer, water, tea }
            .Select(p => new StockItem(p.Id, p.Code, p.Barcode, p.Name, 1, "Đồ uống", p.Unit, p.StockQuantity, 0, p.CostPrice, p.SellPrice, p.IsActive))
            .ToList();
        var draft = new GoodsReceiptDraft(catalog, canChangePrices: true);
        draft.SetSupplier(supplier.Id);
        draft.Add(beer.Id, 25);
        draft.UpdateLine(beer.Id, 25, 11_730, 3_333);
        draft.Add(water.Id, 37);
        draft.UpdateLine(water.Id, 37, 5_100, 0, 6_000);
        draft.Add(tea.Id, 11);
        draft.UpdateLine(tea.Id, 11, 12_490);                  // average ≈ 11.000 > shelf 10.000
        draft.SetDiscount(17_777);
        draft.SetPaidAmount(300_000);
        draft.SetReference("0004567", "Giao buổi sáng");

        var sut = new GoodsReceiptService(receipts, suppliers, products, new InMemoryStockMovementRepository(),
            new FakeUnitOfWork(), session, new RecordingAuditService(), clock);
        var saved = await sut.SaveDraftAsync(draft.BuildRequest().Value);
        Assert.True(saved.IsSuccess);
        Assert.Equal(draft.Subtotal, saved.Value.Subtotal);
        Assert.Equal(draft.Total, saved.Value.Total);
        Assert.Equal(draft.DebtAmount, saved.Value.DebtAmount);
        foreach (var line in saved.Value.Lines)
        {
            var preview = draft.Find(line.ProductId)!;
            Assert.Equal(preview.AllocatedDiscount, line.AllocatedDiscount);
            Assert.Equal(preview.FinalAmount, line.FinalAmount);
            Assert.Equal(preview.EffectiveUnitCost, line.EffectiveUnitCost);
        }

        draft.MarkSaved(saved.Value);
        var completed = await sut.CompleteAsync(saved.Value.Id);
        Assert.True(completed.IsSuccess);
        Assert.Equal(draft.DebtAmount, completed.Value.DebtAmount);
        foreach (var posted in completed.Value.Lines)
        {
            var preview = draft.Find(posted.ProductId)!;
            Assert.Equal(preview.NewAverageCost, posted.NewCostPrice);
            Assert.Equal(preview.StockAfter, posted.StockAfter);
            Assert.Equal(preview.SellPriceAfter, posted.SellPrice);
            Assert.Equal(preview.IsBelowCost, posted.IsBelowCost);
        }

        Assert.Contains(completed.Value.Lines, l => l.IsBelowCost && l.ProductId == tea.Id);
    }
}
