using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;

namespace GroceryPOS.Application.Tests;

public class GoodsReceiptServiceTests
{
    private readonly InMemoryGoodsReceiptRepository _receipts = new();
    private readonly InMemorySupplierRepository _suppliers = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 02:00 UTC = 09:00 in Vietnam
    private readonly Supplier _vinamilk;
    private readonly Product _milk;
    private readonly Product _beer;

    public GoodsReceiptServiceTests()
    {
        _vinamilk = new Supplier("NCC01", "Công ty Vinamilk");
        _suppliers.Add(_vinamilk);

        _milk = new Product("SUA01", "Sữa tươi Vinamilk 180ml", 1, "hộp", costPrice: 7_000, sellPrice: 9_000);
        _milk.AdjustStock(20);
        _milk.SetMinStockLevel(24);
        _products.Add(_milk);

        _beer = new Product("BIA01", "Bia Sài Gòn lon 330ml", 1, "lon", costPrice: 11_000, sellPrice: 14_000, VatRate.Ten);
        _beer.SetMinStockLevel(24);
        _products.Add(_beer);

        SignIn(Permissions.PurchasesManage, Permissions.PricesChange);
    }

    private GoodsReceiptService CreateSut() =>
        new(_receipts, _suppliers, _products, _movements, _uow, _session, _audit, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(5, "thukho", "Lê Văn C", SystemRoles.Storekeeper, permissions.ToHashSet(), _clock.Now));

    private SaveGoodsReceiptRequest MilkAndBeer(int? id = null, decimal paid = 0) => new(
        id,
        _vinamilk.Id,
        new[]
        {
            new GoodsReceiptLineInput(_milk.Id, 30, 8_000),
            new GoodsReceiptLineInput(_beer.Id, 24, 11_500, DiscountAmount: 12_000, NewSellPrice: 15_000),
        },
        DiscountAmount: 4_000,
        PaidAmount: paid,
        SupplierInvoiceNo: "0001234");

    [Fact]
    public async Task SaveDraft_creates_numbered_draft_without_touching_stock()
    {
        var result = await CreateSut().SaveDraftAsync(MilkAndBeer());

        Assert.True(result.IsSuccess);
        var dto = result.Value;
        Assert.Equal("PN261008-0001", dto.Code);
        Assert.Equal(GoodsReceiptStatus.Draft, dto.Status);
        Assert.Equal("Công ty Vinamilk", dto.SupplierName);
        Assert.Equal(504_000m, dto.Subtotal); // 240.000 + 264.000
        Assert.Equal(500_000m, dto.Total);
        Assert.Equal(2, dto.Lines.Count);
        Assert.Equal(20m, _milk.StockQuantity);
        Assert.Empty(_movements.All);
        Assert.Equal(1, _uow.SaveCount);

        var second = await CreateSut().SaveDraftAsync(MilkAndBeer());
        Assert.Equal("PN261008-0002", second.Value.Code);
    }

    [Fact]
    public async Task SaveDraft_with_id_rewrites_existing_draft()
    {
        var sut = CreateSut();
        var created = await sut.SaveDraftAsync(MilkAndBeer());

        var updated = await sut.SaveDraftAsync(new SaveGoodsReceiptRequest(
            created.Value.Id, _vinamilk.Id, new[] { new GoodsReceiptLineInput(_milk.Id, 10, 7_500) }, Note: "Giao bổ sung"));

        Assert.True(updated.IsSuccess);
        Assert.Equal(created.Value.Code, updated.Value.Code);
        Assert.Equal(75_000m, updated.Value.Total);
        Assert.Equal("Giao bổ sung", updated.Value.Note);
        Assert.Single(_receipts.All);
    }

    [Fact]
    public async Task SaveDraft_validates_permissions_supplier_products_and_amounts()
    {
        SignIn(Permissions.InventoryView);
        Assert.Equal("forbidden", (await CreateSut().SaveDraftAsync(MilkAndBeer())).Error.Code);

        SignIn(Permissions.PurchasesManage); // no prices.change → cannot set a new sell price
        Assert.Equal(PurchasingErrors.PriceChangeNotAllowed, (await CreateSut().SaveDraftAsync(MilkAndBeer())).Error);

        SignIn(Permissions.PurchasesManage, Permissions.PricesChange);
        var sut = CreateSut();
        var empty = await sut.SaveDraftAsync(new SaveGoodsReceiptRequest(null, _vinamilk.Id, Array.Empty<GoodsReceiptLineInput>()));
        var noSupplier = await sut.SaveDraftAsync(MilkAndBeer() with { SupplierId = 99 });
        var noProduct = await sut.SaveDraftAsync(MilkAndBeer() with { Lines = new[] { new GoodsReceiptLineInput(99, 1, 1_000) } });
        var duplicate = await sut.SaveDraftAsync(MilkAndBeer() with
        {
            Lines = new[] { new GoodsReceiptLineInput(_milk.Id, 1, 1_000), new GoodsReceiptLineInput(_milk.Id, 2, 1_000) },
        });
        var bigDiscount = await sut.SaveDraftAsync(MilkAndBeer() with { DiscountAmount = 600_000 });
        var overPaid = await sut.SaveDraftAsync(MilkAndBeer(paid: 500_001));

        _vinamilk.Deactivate();
        var inactive = await sut.SaveDraftAsync(MilkAndBeer());

        Assert.Equal(PurchasingErrors.EmptyReceipt, empty.Error);
        Assert.Equal(PurchasingErrors.SupplierNotFound, noSupplier.Error);
        Assert.Equal("not_found", noProduct.Error.Code);
        Assert.Equal("receipt.duplicate", duplicate.Error.Code);
        Assert.Equal(PurchasingErrors.DiscountTooLarge, bigDiscount.Error);
        Assert.Equal(PurchasingErrors.OverPaid, overPaid.Error);
        Assert.Equal("validation", inactive.Error.Code);
        Assert.Empty(_receipts.All);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Complete_posts_stock_cost_prices_debt_and_audit()
    {
        var sut = CreateSut();
        var draft = await sut.SaveDraftAsync(MilkAndBeer(paid: 300_000));

        var result = await sut.CompleteAsync(draft.Value.Id);

        Assert.True(result.IsSuccess);
        var done = result.Value;
        Assert.Equal(500_000m, done.Total);
        Assert.Equal(200_000m, done.DebtAmount);
        Assert.Equal(200_000m, done.SupplierDebtBalance);
        Assert.Equal(200_000m, _vinamilk.DebtBalance);

        // Receipt discount 4.000 is split 240.000 : 264.000 → milk 1.905, beer 2.095.
        // Milk: landed (240.000 − 1.905) / 30 = 7.936,5 → 7.937; average (20 × 7.000 + 30 × 7.937) / 50 = 7.562
        Assert.Equal(50m, _milk.StockQuantity);
        Assert.Equal(7_562m, _milk.CostPrice);
        // Beer had no stock → cost is the landed cost (264.000 − 2.095) / 24 = 10.912,7 → 10.913; new shelf price applied.
        Assert.Equal(24m, _beer.StockQuantity);
        Assert.Equal(10_913m, _beer.CostPrice);
        Assert.Equal(15_000m, _beer.SellPrice);

        var beerLine = done.Lines.Single(l => l.ProductId == _beer.Id);
        Assert.True(beerLine.SellPriceChanged);
        Assert.Equal(11_000m, beerLine.OldCostPrice);
        Assert.Empty(done.BelowCostLines);

        Assert.Equal(2, _movements.All.Count);
        Assert.All(_movements.All, m =>
        {
            Assert.Equal(StockMovementType.GoodsReceipt, m.Type);
            Assert.Equal("PN261008-0001", m.ReferenceCode);
            Assert.Equal(5, m.UserId);
        });

        Assert.Equal(new[] { AuditActions.PriceChanged, AuditActions.GoodsReceived }, _audit.Actions.ToArray());
        Assert.Contains("14.000", _audit.Entries[0].Details!);
        Assert.Contains("công nợ 200.000", _audit.Entries[1].Details!);
        Assert.Equal(2, _uow.SaveCount);

        var again = await sut.CompleteAsync(draft.Value.Id);
        Assert.Equal("receipt.status", again.Error.Code);
        Assert.Equal(50m, _milk.StockQuantity);
    }

    [Fact]
    public async Task Complete_warns_when_new_cost_reaches_sell_price()
    {
        var sut = CreateSut();
        var draft = await sut.SaveDraftAsync(new SaveGoodsReceiptRequest(
            null, _vinamilk.Id, new[] { new GoodsReceiptLineInput(_beer.Id, 24, 14_500) }));

        var done = await sut.CompleteAsync(draft.Value.Id);

        var line = Assert.Single(done.Value.BelowCostLines);
        Assert.Equal(_beer.Id, line.ProductId);
        Assert.Equal(14_500m, line.NewCostPrice);
    }

    [Fact]
    public async Task Complete_requires_price_permission_when_receipt_changes_sell_price()
    {
        var draft = await CreateSut().SaveDraftAsync(MilkAndBeer());
        SignIn(Permissions.PurchasesManage);

        var result = await CreateSut().CompleteAsync(draft.Value.Id);

        Assert.Equal(PurchasingErrors.PriceChangeNotAllowed, result.Error);
        Assert.Equal(20m, _milk.StockQuantity);
        Assert.Equal(GoodsReceiptStatus.Draft, _receipts.All[0].Status);
    }

    [Fact]
    public async Task Cancel_discards_draft_only()
    {
        var sut = CreateSut();
        var draft = await sut.SaveDraftAsync(MilkAndBeer());

        var cancelled = await sut.CancelAsync(draft.Value.Id, "NCC giao thiếu hàng");
        var completeCancelled = await sut.CompleteAsync(draft.Value.Id);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(GoodsReceiptStatus.Cancelled, _receipts.All[0].Status);
        Assert.Equal(AuditActions.GoodsReceiptCancelled, _audit.Actions.Last());
        Assert.Equal("receipt.status", completeCancelled.Error.Code);
        Assert.Equal(PurchasingErrors.ReceiptNotFound, (await sut.CancelAsync(99, null)).Error);
    }

    [Fact]
    public async Task Search_filters_and_reports_debt_only_for_completed_receipts()
    {
        var sut = CreateSut();
        var first = await sut.SaveDraftAsync(MilkAndBeer());
        await sut.CompleteAsync(first.Value.Id);
        _clock.Advance(TimeSpan.FromHours(1));
        await sut.SaveDraftAsync(MilkAndBeer());

        var all = await sut.SearchAsync(new GoodsReceiptFilter());
        var drafts = await sut.SearchAsync(new GoodsReceiptFilter(Status: GoodsReceiptStatus.Draft));
        var invalid = await sut.SearchAsync(new GoodsReceiptFilter(From: _clock.Now, To: _clock.Now));

        Assert.Equal(new[] { "PN261008-0002", "PN261008-0001" }, all.Value.Select(r => r.Code).ToArray());
        Assert.Equal(500_000m, all.Value[1].DebtAmount);
        Assert.Equal(0m, all.Value[0].DebtAmount);
        Assert.Equal("Công ty Vinamilk", all.Value[0].SupplierName);
        Assert.Single(drafts.Value);
        Assert.Equal(PurchasingErrors.InvalidPeriod, invalid.Error);
    }

    [Fact]
    public async Task SuggestReorder_refills_to_twice_the_minimum_out_of_stock_first()
    {
        var result = await CreateSut().SuggestReorderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { _beer.Id, _milk.Id }, result.Value.Select(s => s.ProductId).ToArray());
        Assert.Equal(48m, result.Value[0].SuggestedQuantity);      // 0 → 48
        Assert.Equal(28m, result.Value[1].SuggestedQuantity);      // 20 → 48
        Assert.Equal(196_000m, result.Value[1].EstimatedAmount);   // 28 × 7.000
    }

    [Fact]
    public async Task Requires_signed_in_user()
    {
        _session.SignOut();

        var result = await CreateSut().SuggestReorderAsync();

        Assert.True(result.IsFailure);
        Assert.NotEqual(Error.None, result.Error);
    }
}
