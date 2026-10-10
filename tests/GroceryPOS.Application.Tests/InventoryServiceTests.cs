using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Tests;

public class InventoryServiceTests
{
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 02:00 UTC = 09:00 in Vietnam
    private readonly Product _beer;
    private readonly Product _eggs;
    private readonly Product _sugar;

    public InventoryServiceTests()
    {
        _beer = new Product("BIA01", "Bia Sài Gòn lon 330ml", 1, "lon", costPrice: 11_000, sellPrice: 14_000, VatRate.Ten);
        _beer.AdjustStock(48);
        _beer.SetMinStockLevel(24);
        _products.Add(_beer);

        _eggs = new Product("TRUNG01", "Trứng gà (vỉ 10)", 1, "vỉ", costPrice: 28_000, sellPrice: 35_000);
        _eggs.SetMinStockLevel(5);
        _products.Add(_eggs); // out of stock

        _sugar = new Product("DUONG01", "Đường Biên Hòa 1kg", 1, "gói", costPrice: 22_000, sellPrice: 27_000);
        _sugar.AdjustStock(3);
        _sugar.SetMinStockLevel(10);
        _products.Add(_sugar);

        SignIn(Permissions.InventoryView, Permissions.InventoryAdjust);
    }

    private InventoryService CreateSut() => new(_products, _movements, _uow, _session, _audit, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(5, "thukho", "Lê Văn C", SystemRoles.Storekeeper, permissions.ToHashSet(), _clock.Now));

    [Fact]
    public async Task Adjust_writes_ledger_entry_audits_and_saves()
    {
        var result = await CreateSut().AdjustAsync(new StockAdjustmentRequest(_beer.Id, -2, StockAdjustmentReason.Damaged, "Lon bị móp"));

        Assert.True(result.IsSuccess);
        Assert.Equal(46m, _beer.StockQuantity);
        Assert.Equal(46m, result.Value.BalanceAfter);
        Assert.Equal(-22_000m, result.Value.CostValue);
        Assert.False(result.Value.IsLowStock);

        var movement = Assert.Single(_movements.All);
        Assert.Equal(StockMovementType.Adjustment, movement.Type);
        Assert.Equal(StockAdjustmentReason.Damaged, movement.Reason);
        Assert.Equal(5, movement.UserId);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.StockAdjusted, entry.Action);
        Assert.Contains("Hư hỏng", entry.Details!);
        Assert.Contains("48 → 46", entry.Details!);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Adjust_rejects_missing_permission_unknown_product_and_broken_rules()
    {
        SignIn(Permissions.InventoryView);
        var forbidden = await CreateSut().AdjustAsync(new StockAdjustmentRequest(_beer.Id, -1, StockAdjustmentReason.Lost));

        SignIn(Permissions.InventoryAdjust);
        var sut = CreateSut();
        var missing = await sut.AdjustAsync(new StockAdjustmentRequest(999, -1, StockAdjustmentReason.Lost));
        var wrongDirection = await sut.AdjustAsync(new StockAdjustmentRequest(_beer.Id, 5, StockAdjustmentReason.Expired));
        var belowZero = await sut.AdjustAsync(new StockAdjustmentRequest(_sugar.Id, -4, StockAdjustmentReason.Lost));

        Assert.Equal("forbidden", forbidden.Error.Code);
        Assert.Equal("not_found", missing.Error.Code);
        Assert.Equal("stock.reason", wrongDirection.Error.Code);
        Assert.Equal("product.stock", belowZero.Error.Code);
        Assert.Equal(3m, _sugar.StockQuantity);
        Assert.Empty(_movements.All);
        Assert.Equal(0, _uow.SaveCount);
    }

    [Fact]
    public async Task Adjust_requires_sign_in()
    {
        _session.SignOut();

        var result = await CreateSut().AdjustAsync(new StockAdjustmentRequest(_beer.Id, -1, StockAdjustmentReason.Lost));

        Assert.Equal("auth.required", result.Error.Code);
    }

    [Fact]
    public async Task Count_sets_counted_quantities_and_reports_variance()
    {
        var result = await CreateSut().CountAsync(new StockCountRequest(
            new[]
            {
                new StockCountLine(_beer.Id, 45),   // 3 missing
                new StockCountLine(_eggs.Id, 2),    // 2 found
                new StockCountLine(_sugar.Id, 3),   // matches
            },
            "Kiểm kê cuối tháng"));

        Assert.True(result.IsSuccess);
        var count = result.Value;
        Assert.Equal("KK261008-090000", count.Code);
        Assert.Equal(2, count.MismatchCount);
        Assert.Equal(-33_000m, count.ShortageValue);
        Assert.Equal(56_000m, count.SurplusValue);
        Assert.Equal(23_000m, count.NetVarianceValue);

        Assert.Equal(45m, _beer.StockQuantity);
        Assert.Equal(2m, _eggs.StockQuantity);
        Assert.Equal(3m, _sugar.StockQuantity);

        Assert.Equal(2, _movements.All.Count);
        Assert.True(_movements.All.All(m => m.Type == StockMovementType.StockCount && m.ReferenceCode == count.Code));
        Assert.Equal(AuditActions.StockCounted, Assert.Single(_audit.Entries).Action);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Count_validates_all_lines_before_changing_anything()
    {
        var sut = CreateSut();

        var empty = await sut.CountAsync(new StockCountRequest(Array.Empty<StockCountLine>()));
        var duplicate = await sut.CountAsync(new StockCountRequest(new[] { new StockCountLine(_beer.Id, 40), new StockCountLine(_beer.Id, 41) }));
        var negative = await sut.CountAsync(new StockCountRequest(new[] { new StockCountLine(_beer.Id, 40), new StockCountLine(_sugar.Id, -1) }));
        var missing = await sut.CountAsync(new StockCountRequest(new[] { new StockCountLine(_beer.Id, 40), new StockCountLine(404, 1) }));

        Assert.Equal(InventoryErrors.EmptyCount, empty.Error);
        Assert.Contains("nhiều lần", duplicate.Error.Message);
        Assert.Contains("không được âm", negative.Error.Message);
        Assert.Equal("not_found", missing.Error.Code);
        Assert.Equal(48m, _beer.StockQuantity);
        Assert.Empty(_movements.All);
    }

    [Fact]
    public async Task Ledger_lists_movements_with_opening_and_closing_balance()
    {
        var sut = CreateSut();
        await sut.AdjustAsync(new StockAdjustmentRequest(_beer.Id, -2, StockAdjustmentReason.Damaged));
        _clock.Advance(TimeSpan.FromDays(1));
        var dayTwo = _clock.Now;
        await sut.AdjustAsync(new StockAdjustmentRequest(_beer.Id, 6, StockAdjustmentReason.Found));
        _clock.Advance(TimeSpan.FromHours(2));
        await sut.CountAsync(new StockCountRequest(new[] { new StockCountLine(_beer.Id, 50) }));
        _clock.Advance(TimeSpan.FromDays(1));
        var dayThree = _clock.Now.AddHours(-12);
        await sut.AdjustAsync(new StockAdjustmentRequest(_beer.Id, -1, StockAdjustmentReason.InternalUse));

        var all = (await sut.GetLedgerAsync(_beer.Id)).Value;
        var period = (await sut.GetLedgerAsync(_beer.Id, dayTwo, dayThree)).Value;

        Assert.Equal(4, all.Entries.Count);
        Assert.Equal(48m, all.OpeningBalance);
        Assert.Equal(49m, all.ClosingBalance);
        Assert.Equal("Điều chỉnh", all.Entries[0].TypeName);
        Assert.Equal("Hư hỏng", all.Entries[0].ReasonName);
        Assert.Equal(2m, all.Entries[0].QuantityOut);

        Assert.Equal(2, period.Entries.Count);
        Assert.Equal(46m, period.OpeningBalance);
        Assert.Equal(6m, period.TotalIn);
        Assert.Equal(2m, period.TotalOut);
        Assert.Equal(50m, period.ClosingBalance);
        Assert.Equal(StockMovementType.StockCount, period.Entries[1].Type);
    }

    [Fact]
    public async Task Ledger_of_quiet_period_uses_balance_at_period_end()
    {
        var sut = CreateSut();
        var from = _clock.Now.AddDays(-2);
        var to = _clock.Now.AddDays(-1);
        await sut.AdjustAsync(new StockAdjustmentRequest(_beer.Id, -8, StockAdjustmentReason.Expired));

        var ledger = (await sut.GetLedgerAsync(_beer.Id, from, to)).Value;
        var invalid = await sut.GetLedgerAsync(_beer.Id, to, from);

        Assert.Empty(ledger.Entries);
        Assert.Equal(48m, ledger.OpeningBalance);
        Assert.Equal(48m, ledger.ClosingBalance);
        Assert.Equal(InventoryErrors.InvalidPeriod, invalid.Error);
    }

    [Fact]
    public async Task Low_stock_lists_out_of_stock_first_then_largest_shortage()
    {
        _beer.Deactivate();
        _beer.AdjustStock(-40); // inactive products are never reported

        var result = await CreateSut().GetLowStockAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "TRUNG01", "DUONG01" }, result.Value.Select(i => i.ProductCode));
        Assert.True(result.Value[0].IsOutOfStock);
        Assert.Equal(7m, result.Value[1].Shortage);
    }

    [Fact]
    public async Task Read_queries_require_inventory_view()
    {
        SignIn(Permissions.SalesCreate);
        var sut = CreateSut();

        Assert.Equal("forbidden", (await sut.GetLowStockAsync()).Error.Code);
        Assert.Equal("forbidden", (await sut.GetLedgerAsync(_beer.Id)).Error.Code);
    }
}
