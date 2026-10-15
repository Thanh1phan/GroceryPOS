using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Tests;

public class ReportServiceTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 8);
    private static readonly DateOnly Day2 = new(2026, 10, 9);

    private readonly InMemorySaleRepository _sales = new();
    private readonly InMemorySaleReturnRepository _returns = new();
    private readonly InMemoryProductRepository _products = new();
    private readonly InMemoryCategoryRepository _categories = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new(); // 2026-10-08 09:00 in Vietnam
    private readonly Product _milk;
    private readonly Product _noodles;

    public ReportServiceTests()
    {
        _categories.Add(new Category("Sữa"));
        _categories.Add(new Category("Mì gói"));

        _milk = new Product("SUA01", "Sữa tươi Vinamilk 1L", 1, "hộp", costPrice: 28_000, sellPrice: 35_000, VatRate.Five);
        _milk.AdjustStock(20);
        _products.Add(_milk);

        _noodles = new Product("MI01", "Mì Hảo Hảo tôm chua cay", 2, "gói", costPrice: 3_500, sellPrice: 5_000, VatRate.Ten);
        _noodles.AdjustStock(100);
        _products.Add(_noodles);
    }

    private ReportService CreateSut() => new(_sales, _returns, _products, _categories, _session, _clock);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(3, "quanly", "Lê Văn C", SystemRoles.Manager, permissions.ToHashSet(), _clock.Now));

    private void At(DateOnly day, int hour) =>
        _clock.Now = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(7)).ToUniversalTime();

    private async Task<Sale> SellAsync(IEnumerable<CheckoutLine> lines, PaymentInput payment)
    {
        SignIn(Permissions.SalesCreate, Permissions.SalesVoid, Permissions.ReturnsCreate);
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        var result = await checkout.CheckoutAsync(new CheckoutRequest(lines.ToArray(), new[] { payment }));
        Assert.True(result.IsSuccess);
        return _sales.All.Single(s => s.Id == result.Value.SaleId);
    }

    /// <summary>
    /// Day 1: A = milk 2 + noodles 10 (120.000, paid 150.000 cash), B = noodles 4 (20.000 transfer),
    /// C = milk 1 (voided). Day 2: D = milk 1 (35.000 card) and a return on A: milk 1 restocked +
    /// noodles 2 written off (refund 45.000).
    /// </summary>
    private async Task SeedTwoDaysAsync()
    {
        At(Day1, 9);
        var a = await SellAsync(
            new[] { new CheckoutLine(_milk.Id, 2), new CheckoutLine(_noodles.Id, 10) },
            new PaymentInput(PaymentMethod.Cash, 150_000));
        At(Day1, 15);
        await SellAsync(new[] { new CheckoutLine(_noodles.Id, 4) }, new PaymentInput(PaymentMethod.BankTransfer, 20_000));
        At(Day1, 16);
        var c = await SellAsync(new[] { new CheckoutLine(_milk.Id, 1) }, new PaymentInput(PaymentMethod.Cash, 35_000));
        var checkout = new CheckoutService(
            _sales, _products, _movements, _customers, new InvoiceNumberGenerator(_sales), _uow, _session, _audit, _clock);
        Assert.True((await checkout.VoidAsync(c.Id, "Khách không lấy hàng")).IsSuccess);

        At(Day2, 10);
        await SellAsync(new[] { new CheckoutLine(_milk.Id, 1) }, new PaymentInput(PaymentMethod.Card, 35_000));
        var returns = new ReturnService(_sales, _returns, _products, _movements, _customers, _uow, _session, _audit, _clock);
        var returned = await returns.CreateAsync(new CreateReturnRequest(
            a.Id,
            new[]
            {
                new ReturnLineInput(a.Lines.Single(l => l.ProductId == _milk.Id).Id, 1),
                new ReturnLineInput(a.Lines.Single(l => l.ProductId == _noodles.Id).Id, 2, Restock: false),
            },
            "Hàng lỗi"));
        Assert.True(returned.IsSuccess);
        Assert.Equal(45_000m, returned.Value.RefundTotal);
    }

    [Fact]
    public async Task Revenue_report_splits_days_excludes_voids_and_deducts_returns_on_return_day()
    {
        await SeedTwoDaysAsync();
        SignIn(Permissions.ReportsView);

        var result = await CreateSut().GetRevenueAsync(new ReportPeriod(Day1, Day2));

        Assert.True(result.IsSuccess);
        var report = result.Value;
        Assert.Equal(2, report.Days.Count);

        var day1 = report.Days[0];
        Assert.Equal(Day1, day1.Date);
        Assert.Equal(2, day1.InvoiceCount);
        Assert.Equal(1, day1.VoidedCount);
        Assert.Equal(140_000m, day1.SalesTotal);
        Assert.Equal(140_000m, day1.NetRevenue);
        Assert.Equal(9_696m, day1.VatAmount);       // 3.333 + 4.545 + 1.818
        Assert.Equal(105_000m, day1.CostOfGoods);
        Assert.Equal(25_304m, day1.GrossProfit);
        Assert.Equal(120_000m, day1.CashCollected); // 150.000 tendered − 30.000 change
        Assert.Equal(20_000m, day1.TransferCollected);
        Assert.Equal(70_000m, day1.AverageBasket);

        var day2 = report.Days[1];
        Assert.Equal(1, day2.InvoiceCount);
        Assert.Equal(1, day2.ReturnCount);
        Assert.Equal(45_000m, day2.RefundTotal);
        Assert.Equal(-10_000m, day2.NetRevenue);
        Assert.Equal(0m, day2.CostOfGoods);         // the restocked milk gives its cost back
        Assert.Equal(-9_091m, day2.GrossProfit);    // written-off noodles lose their whole refund
        Assert.Equal(7_000m, day2.WriteOffCost);
        Assert.Equal(35_000m, day2.CardCollected);

        Assert.Null(report.Totals.Date);
        Assert.Equal(3, report.Totals.InvoiceCount);
        Assert.Equal(130_000m, report.Totals.NetRevenue);
        Assert.Equal(16_213m, report.Totals.GrossProfit);
        Assert.Equal(report.Totals.NetRevenueExclVat - report.Totals.CostOfGoods, report.Totals.GrossProfit);
    }

    [Fact]
    public async Task Revenue_report_includes_empty_days_and_uses_store_time()
    {
        // 23:30 in Vietnam is still the same store day although it is 16:30 UTC.
        At(Day1, 23);
        _clock.Advance(TimeSpan.FromMinutes(30));
        await SellAsync(new[] { new CheckoutLine(_noodles.Id, 1) }, new PaymentInput(PaymentMethod.Cash, 5_000));
        SignIn(Permissions.ReportsView);

        var report = (await CreateSut().GetRevenueAsync(new ReportPeriod(Day1.AddDays(-1), Day2))).Value;

        Assert.Equal(3, report.Days.Count);
        Assert.Equal(0, report.Days[0].InvoiceCount);
        Assert.Equal(5_000m, report.Days[1].SalesTotal);
        Assert.Equal(0m, report.Days[2].SalesTotal);
        Assert.Equal(0m, report.Days[2].AverageBasket);
    }

    [Fact]
    public async Task Best_sellers_rank_net_of_returns_and_show_share()
    {
        await SeedTwoDaysAsync();
        SignIn(Permissions.ReportsView);
        var sut = CreateSut();
        var period = new ReportPeriod(Day1, Day2);

        var byQuantity = (await sut.GetBestSellersAsync(new BestSellerQuery(period))).Value;
        Assert.Equal(130_000m, byQuantity.TotalRevenue);
        Assert.Equal(2, byQuantity.ProductCount);
        var noodles = byQuantity.Rows[0];
        Assert.Equal(1, noodles.Rank);
        Assert.Equal("MI01", noodles.ProductCode);
        Assert.Equal("Mì gói", noodles.CategoryName);
        Assert.Equal(12m, noodles.Quantity);          // 10 + 4 − 2 returned
        Assert.Equal(60_000m, noodles.Revenue);
        Assert.Equal(49_000m, noodles.Cost);          // written-off units keep their cost
        Assert.Equal(5_546m, noodles.Profit);
        Assert.Equal(2, noodles.InvoiceCount);
        Assert.Equal(46.2m, noodles.RevenueSharePercent);

        var milk = byQuantity.Rows[1];
        Assert.Equal(2m, milk.Quantity);              // voided sale C is left out
        Assert.Equal(70_000m, milk.Revenue);
        Assert.Equal(10_667m, milk.Profit);

        var byRevenue = (await sut.GetBestSellersAsync(new BestSellerQuery(period, ProductRanking.Revenue))).Value;
        Assert.Equal("SUA01", byRevenue.Rows[0].ProductCode);

        var top1 = (await sut.GetBestSellersAsync(new BestSellerQuery(period, ProductRanking.Profit, Top: 1))).Value;
        Assert.Single(top1.Rows);
        Assert.Equal("SUA01", top1.Rows[0].ProductCode);

        var onlyNoodles = (await sut.GetBestSellersAsync(new BestSellerQuery(period, CategoryId: 2))).Value;
        Assert.Single(onlyNoodles.Rows);
        Assert.Equal(46.2m, onlyNoodles.Rows[0].RevenueSharePercent); // share stays relative to all products
    }

    [Fact]
    public async Task Profit_by_category_adds_up_to_revenue_report()
    {
        await SeedTwoDaysAsync();
        SignIn(Permissions.ReportsView);
        var sut = CreateSut();
        var period = new ReportPeriod(Day1, Day2);

        var profit = (await sut.GetProfitByCategoryAsync(period)).Value;
        var revenue = (await sut.GetRevenueAsync(period)).Value;

        Assert.Equal(2, profit.Rows.Count);
        Assert.Equal("Sữa", profit.Rows[0].CategoryName); // highest profit first
        Assert.Equal(10_667m, profit.Rows[0].Profit);
        Assert.Equal(53.8m, profit.Rows[0].RevenueSharePercent);
        Assert.Equal(revenue.Totals.NetRevenue, profit.Totals.Revenue);
        Assert.Equal(revenue.Totals.GrossProfit, profit.Totals.Profit);
        Assert.Equal(revenue.Totals.CostOfGoods, profit.Totals.Cost);
    }

    [Fact]
    public async Task Inventory_value_by_category_ignores_negative_stock_and_inactive_products()
    {
        // Stock after setup: milk 20, noodles 100.
        var soap = new Product("XP01", "Xà phòng Lifebuoy", 2, "bánh", costPrice: 15_000, sellPrice: 20_000);
        soap.AdjustStock(-3, allowNegative: true);
        _products.Add(soap);
        var oldMilk = new Product("SUA99", "Sữa ngừng bán", 1, "hộp", costPrice: 10_000, sellPrice: 12_000);
        oldMilk.AdjustStock(5);
        oldMilk.Deactivate();
        _products.Add(oldMilk);
        _noodles.SetMinStockLevel(120);
        SignIn(Permissions.InventoryView);

        var report = (await CreateSut().GetInventoryValueAsync()).Value;

        Assert.Equal(2, report.Rows.Count);
        var dairy = report.Rows.Single(r => r.CategoryName == "Sữa");
        Assert.Equal(1, dairy.ProductCount);
        Assert.Equal(560_000m, dairy.CostValue);
        Assert.Equal(700_000m, dairy.RetailValue);
        Assert.Equal(666_667m, dairy.RetailValueExclVat); // 700.000 − 33.333 VAT 5%
        Assert.Equal(106_667m, dairy.PotentialProfit);

        var noodles = report.Rows.Single(r => r.CategoryName == "Mì gói");
        Assert.Equal(350_000m, noodles.CostValue);       // soap's −3 adds nothing
        Assert.Equal(1, noodles.NegativeStockCount);
        Assert.Equal(1, noodles.OutOfStockCount);
        Assert.Equal(1, noodles.LowStockCount);

        Assert.Equal(910_000m, report.Totals.CostValue);
        Assert.Equal(3, report.Totals.ProductCount);

        var withInactive = (await CreateSut().GetInventoryValueAsync(includeInactive: true)).Value;
        Assert.Equal(960_000m, withInactive.Totals.CostValue);
    }

    [Fact]
    public async Task Dashboard_compares_today_with_yesterday_and_hides_profit_from_cashiers()
    {
        At(Day1, 9);
        await SellAsync(
            new[] { new CheckoutLine(_milk.Id, 2), new CheckoutLine(_noodles.Id, 10) },
            new PaymentInput(PaymentMethod.Cash, 120_000));
        At(Day1, 15);
        await SellAsync(new[] { new CheckoutLine(_noodles.Id, 4) }, new PaymentInput(PaymentMethod.BankTransfer, 20_000));
        At(Day2, 10);
        await SellAsync(new[] { new CheckoutLine(_milk.Id, 1) }, new PaymentInput(PaymentMethod.Card, 35_000));
        _milk.SetMinStockLevel(30);
        _clock.Advance(TimeSpan.FromMinutes(45));

        SignIn(Permissions.DashboardView);
        var cashierView = (await CreateSut().GetDashboardAsync()).Value;

        Assert.Equal(Day2, cashierView.Today);
        Assert.Equal(35_000m, cashierView.NetRevenueToday);
        Assert.Equal(1, cashierView.InvoiceCountToday);
        Assert.Null(cashierView.GrossProfitToday);
        Assert.Equal(140_000m, cashierView.NetRevenueYesterday);
        Assert.Equal(-75.0m, cashierView.RevenueChangePercent);
        Assert.Equal(7, cashierView.Last7Days.Count);
        Assert.Equal(Day2, cashierView.Last7Days[^1].Date);
        Assert.Equal(140_000m, cashierView.Last7Days[^2].NetRevenue);
        Assert.Equal(24, cashierView.TodayByHour.Count);
        Assert.Equal(35_000m, cashierView.TodayByHour[10].SalesTotal);
        Assert.Equal("SUA01", Assert.Single(cashierView.TopProductsToday).ProductCode);
        Assert.Equal(1, cashierView.LowStockCount);
        Assert.Equal(0, cashierView.OutOfStockCount);
        Assert.Equal("SUA01", Assert.Single(cashierView.LowStockItems).ProductCode);

        SignIn(Permissions.DashboardView, Permissions.ReportsView);
        var managerView = (await CreateSut().GetDashboardAsync()).Value;
        Assert.Equal(5_333m, managerView.GrossProfitToday); // 35.000 − 1.667 VAT − 28.000 cost
    }

    [Fact]
    public async Task Dashboard_has_no_change_percent_without_sales_yesterday()
    {
        SignIn(Permissions.DashboardView);

        var dashboard = (await CreateSut().GetDashboardAsync()).Value;

        Assert.Null(dashboard.RevenueChangePercent);
        Assert.Equal(0m, dashboard.NetRevenueToday);
        Assert.Empty(dashboard.TopProductsToday);
    }

    [Fact]
    public async Task Reports_need_permission_and_a_valid_period()
    {
        var sut = CreateSut();
        var period = ReportPeriod.SingleDay(Day1);

        Assert.Equal(AuthErrors.NotAuthenticated, (await sut.GetRevenueAsync(period)).Error);

        SignIn(Permissions.SalesCreate, Permissions.DashboardView);
        Assert.Equal(ReportErrors.Denied, (await sut.GetRevenueAsync(period)).Error);
        Assert.Equal(ReportErrors.Denied, (await sut.GetBestSellersAsync(new BestSellerQuery(period))).Error);
        Assert.Equal(ReportErrors.Denied, (await sut.GetInventoryValueAsync()).Error);

        SignIn(Permissions.SalesCreate);
        Assert.Equal(ReportErrors.DashboardDenied, (await sut.GetDashboardAsync()).Error);

        SignIn(Permissions.ReportsView);
        Assert.Equal(ReportErrors.InvalidPeriod, (await sut.GetRevenueAsync(new ReportPeriod(Day2, Day1))).Error);
        var tooLong = await sut.GetProfitByCategoryAsync(new ReportPeriod(Day1, Day1.AddDays(ReportService.MaxPeriodDays)));
        Assert.Equal("validation", tooLong.Error.Code);
        Assert.True((await sut.GetRevenueAsync(new ReportPeriod(Day1, Day1.AddDays(ReportService.MaxPeriodDays - 1)))).IsSuccess);
    }

    [Fact]
    public void Store_calendar_maps_utc_instants_to_vietnam_days()
    {
        var offset = TimeSpan.FromHours(7);
        var lateEvening = new DateTimeOffset(2026, 10, 8, 16, 59, 0, TimeSpan.Zero); // 23:59 VN
        var midnight = new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero);     // 00:00 VN next day

        Assert.Equal(Day1, StoreCalendar.DateOf(lateEvening, offset));
        Assert.Equal(Day2, StoreCalendar.DateOf(midnight, offset));
        Assert.Equal(midnight, StoreCalendar.StartOf(Day2, offset));
        Assert.Equal(23, StoreCalendar.HourOf(lateEvening, offset));
    }
}
