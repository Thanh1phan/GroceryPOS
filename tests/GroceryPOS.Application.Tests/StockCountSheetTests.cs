using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Tests;

public class StockCountSheetTests
{
    private static StockItem Item(int id, string code, string? barcode, int category, decimal stock, decimal cost, bool active = true, decimal min = 0) =>
        new(id, code, barcode, $"SP {code}", category, $"Nhóm {category}", "cái", stock, min, cost, cost + 1_000, active);

    private static StockCountSheet NewSheet() => new([
        Item(1, "BIA01", "8934588012112", 1, stock: 48, cost: 11_000),
        Item(2, "NUOC01", "8934588063015", 1, stock: 20, cost: 6_000),
        Item(3, "DUONG01", null, 2, stock: 3, cost: 22_000),
        Item(4, "CU01", null, 2, stock: 5, cost: 1_000, active: false),
    ]);

    [Fact]
    public void Starts_blocked_and_adds_lines_by_product_category_and_all_without_duplicates()
    {
        var sheet = NewSheet();
        Assert.Equal("Chưa có sản phẩm nào trong phiếu kiểm kê.", sheet.Blocker);

        Assert.NotNull(sheet.Add(3));
        Assert.Same(sheet.Add(3), sheet.Find(3));
        Assert.Null(sheet.Add(999));
        Assert.Equal(2, sheet.AddCategory(1));
        Assert.Equal(0, sheet.AddCategory(2)); // DUONG01 already there, CU01 inactive
        Assert.Equal(0, sheet.AddAllActive());
        Assert.Equal([3, 1, 2], sheet.Lines.Select(l => l.ProductId));
        Assert.Equal("Chưa nhập số lượng thực tế cho sản phẩm nào.", sheet.Blocker);
        Assert.Equal(3, sheet.UncountedCount);
        Assert.All(sheet.Lines, l => Assert.Equal("Chưa đếm", l.StatusName));
    }

    [Fact]
    public void Scan_counts_by_barcode_or_code_with_quantity_prefix()
    {
        var sheet = NewSheet();

        var first = sheet.Scan("8934588012112");
        var second = sheet.Scan("12*bia01");
        var missing = sheet.Scan("000");
        var empty = sheet.Scan("  ");

        Assert.Equal(StockCountScanOutcome.Counted, first.Outcome);
        Assert.Equal(StockCountScanOutcome.Counted, second.Outcome);
        Assert.Equal(StockCountScanOutcome.NotFound, missing.Outcome);
        Assert.Equal(StockCountScanOutcome.Empty, empty.Outcome);
        var line = Assert.Single(sheet.Lines);
        Assert.Equal(13m, line.Counted);
        Assert.Equal(-35m, line.Variance);
        Assert.Equal("Thiếu", line.StatusName);
        Assert.Contains("đã đếm 13 cái", second.Message);
    }

    [Fact]
    public void Totals_split_shortage_and_surplus_and_uncounted_lines_stay_out_of_the_request()
    {
        var sheet = NewSheet();
        sheet.AddAllActive();
        Assert.True(sheet.SetCounted(1, 46).IsSuccess);      // −2 × 11.000
        Assert.True(sheet.SetCounted(3, 4.0004m).IsSuccess); // rounds to 4: +1 × 22.000
        Assert.True(sheet.SetCounted(2, -1).IsFailure);
        sheet.SetNote("  Kiểm kê quầy nước  ");

        Assert.Equal(2, sheet.CountedCount);
        Assert.Equal(2, sheet.MismatchCount);
        Assert.Equal(-22_000m, sheet.ShortageValue);
        Assert.Equal(22_000m, sheet.SurplusValue);
        Assert.Equal(0m, sheet.NetVarianceValue);
        Assert.Equal("1 sản phẩm chưa đếm sẽ giữ nguyên tồn kho.", sheet.Warning);
        Assert.True(sheet.CanComplete);

        var request = sheet.BuildRequest().Value;
        Assert.Equal([(1, 46m), (3, 4m)], request.Lines.Select(l => (l.ProductId, l.CountedQuantity)));
        Assert.Equal("Kiểm kê quầy nước", request.Note);

        Assert.True(sheet.SetCounted(1, null).IsSuccess);
        Assert.Equal(2, sheet.RemoveUncounted());
        Assert.Equal([3], sheet.Lines.Select(l => l.ProductId));
    }

    [Fact]
    public void Mark_uncounted_as_recorded_matches_the_book_quantity()
    {
        var sheet = NewSheet();
        sheet.AddCategory(1);
        sheet.SetCounted(1, 40);

        Assert.Equal(1, sheet.MarkUncountedAsRecorded());
        Assert.Equal(20m, sheet.Find(2)!.Counted);
        Assert.Equal("Khớp", sheet.Find(2)!.StatusName);
        Assert.Null(sheet.Warning);
    }

    [Fact]
    public async Task Sheet_preview_matches_what_the_count_service_applies()
    {
        var products = new InMemoryProductRepository();
        var beer = new Product("BIA01", "Bia Sài Gòn lon 330ml", 1, "lon", costPrice: 11_000, sellPrice: 14_000);
        beer.AdjustStock(48);
        var rice = new Product("GAO01", "Gạo ST25", 1, "kg", costPrice: 28_500, sellPrice: 35_000);
        rice.AdjustStock(12.5m);
        var salt = new Product("MUOI01", "Muối i-ốt", 2, "gói", costPrice: 4_000, sellPrice: 6_000);
        salt.AdjustStock(7);
        products.Add(beer);
        products.Add(rice);
        products.Add(salt);

        var session = new UserSessionStore();
        var clock = new FakeClock();
        session.SignIn(new UserSession(5, "thukho", "Lê Văn C", SystemRoles.Storekeeper,
            new HashSet<string> { Permissions.InventoryView, Permissions.InventoryAdjust }, clock.Now));
        var sut = new InventoryService(products, new InMemoryStockMovementRepository(), new FakeUnitOfWork(), session, new RecordingAuditService(), clock);

        var stock = (await sut.GetStockListAsync()).Value;
        var sheet = new StockCountSheet(stock);
        sheet.AddAllActive();
        sheet.SetCounted(beer.Id, 45);
        sheet.SetCounted(rice.Id, 12.25m);
        sheet.Scan("3*muoi01");

        var result = (await sut.CountAsync(sheet.BuildRequest().Value)).Value;

        Assert.Equal(sheet.MismatchCount, result.MismatchCount);
        Assert.Equal(sheet.ShortageValue, result.ShortageValue);
        Assert.Equal(sheet.SurplusValue, result.SurplusValue);
        Assert.Equal(sheet.NetVarianceValue, result.NetVarianceValue);
        Assert.Equal(3m, salt.StockQuantity);
    }

    [Fact]
    public async Task Stock_list_needs_inventory_view_and_reports_value_and_status()
    {
        var products = new InMemoryProductRepository();
        var eggs = new Product("TRUNG01", "Trứng gà (vỉ 10)", 1, "vỉ", costPrice: 28_000, sellPrice: 35_000);
        eggs.SetMinStockLevel(5);
        var beer = new Product("BIA01", "Bia Sài Gòn", 1, "lon", costPrice: 11_000, sellPrice: 14_000);
        beer.AdjustStock(30);
        beer.SetMinStockLevel(24);
        products.Add(eggs);
        products.Add(beer);

        var session = new UserSessionStore();
        var clock = new FakeClock();
        IInventoryService Sut() => new InventoryService(products, new InMemoryStockMovementRepository(), new FakeUnitOfWork(), session, new RecordingAuditService(), clock);

        session.SignIn(new UserSession(7, "thungan", "Thu ngân", SystemRoles.Cashier, new HashSet<string> { Permissions.SalesCreate }, clock.Now));
        Assert.True((await Sut().GetStockListAsync()).IsFailure);

        session.SignIn(new UserSession(5, "thukho", "Thủ kho", SystemRoles.Storekeeper, new HashSet<string> { Permissions.InventoryView }, clock.Now));
        var list = (await Sut().GetStockListAsync()).Value;

        Assert.Equal(["BIA01", "TRUNG01"], list.Select(i => i.ProductCode));
        Assert.Equal(330_000m, list[0].StockValue);
        Assert.Equal("Còn hàng", list[0].StatusName);
        Assert.Equal("Hết hàng", list[1].StatusName);
    }
}

public class StockAdjustmentDraftTests
{
    private static readonly StockItem Beer = new(1, "BIA01", null, "Bia Sài Gòn", 1, "Đồ uống", "lon", 30, 24, 11_000, 14_000, true);

    [Fact]
    public void Reason_fixes_the_direction_and_free_reasons_keep_the_last_one()
    {
        var draft = new StockAdjustmentDraft(Beer);
        Assert.Equal(StockAdjustmentReason.Damaged, draft.Reason);
        Assert.False(draft.Increase);
        Assert.False(draft.CanChooseDirection);

        draft.SetIncrease(true); // ignored
        Assert.False(draft.Increase);

        draft.SetReason(StockAdjustmentReason.Found);
        Assert.True(draft.Increase);

        draft.SetReason(StockAdjustmentReason.Correction);
        Assert.True(draft.CanChooseDirection);
        Assert.True(draft.Increase);
        draft.SetIncrease(false);
        Assert.False(draft.Increase);
    }

    [Fact]
    public void Preview_and_blockers_mirror_the_domain_rules()
    {
        var draft = new StockAdjustmentDraft(Beer);
        Assert.Equal("Nhập số lượng điều chỉnh lớn hơn 0.", draft.Blocker);

        draft.SetQuantity(8);
        Assert.Equal(22m, draft.BalanceAfter);
        Assert.Equal(-88_000m, draft.CostValue);
        Assert.True(draft.WillBeLowStock);
        Assert.True(draft.CanSave);

        draft.SetQuantity(31);
        Assert.Contains("chỉ còn 30 lon", draft.Blocker);

        draft.SetReason(StockAdjustmentReason.Other);
        draft.SetQuantity(2);
        Assert.Equal("Vui lòng ghi chú lý do điều chỉnh.", draft.Blocker);
        draft.SetNote("  Chuyển sang chi nhánh 2 ");

        var request = draft.BuildRequest().Value;
        Assert.Equal(new StockAdjustmentRequest(1, -2, StockAdjustmentReason.Other, "Chuyển sang chi nhánh 2"), request);
    }

    [Fact]
    public void Target_balance_sets_quantity_and_direction_or_refuses_against_the_reason()
    {
        var draft = new StockAdjustmentDraft(Beer);
        Assert.False(draft.SetTargetBalance(35)); // "Hư hỏng" cannot increase
        Assert.Equal(0m, draft.Quantity);

        Assert.True(draft.SetTargetBalance(26));
        Assert.Equal(-4m, draft.SignedQuantity);

        draft.SetReason(StockAdjustmentReason.Correction);
        Assert.True(draft.SetTargetBalance(32.5m));
        Assert.Equal(2.5m, draft.SignedQuantity);
        Assert.Equal(32.5m, draft.BalanceAfter);
    }
}
