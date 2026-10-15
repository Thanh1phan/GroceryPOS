using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Reports;

public interface IReportService
{
    /// <summary>Báo cáo doanh thu theo ngày (<c>reports.view</c>).</summary>
    Task<Result<RevenueReportDto>> GetRevenueAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>Hàng bán chạy (<c>reports.view</c>).</summary>
    Task<Result<BestSellerReportDto>> GetBestSellersAsync(BestSellerQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lợi nhuận theo danh mục (<c>reports.view</c>).</summary>
    Task<Result<ProfitReportDto>> GetProfitByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>Giá trị tồn kho hiện tại theo danh mục (<c>reports.view</c> or <c>inventory.view</c>).</summary>
    Task<Result<InventoryValueReportDto>> GetInventoryValueAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>Trang tổng quan for today (<c>dashboard.view</c>; profit only with <c>reports.view</c>).</summary>
    Task<Result<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only reports built from completed sales, customer returns and the current catalog.
/// Revenue rules: a sale counts on its completion day (store time); voided sales are excluded;
/// a return reduces revenue, VAT and profit on the day it was taken. Restocked returns give their
/// cost back; written-off returns do not (the goods are lost), so they reduce profit by the full refund.
/// </summary>
public sealed class ReportService : IReportService
{
    public const int MaxPeriodDays = 366;
    public const int MaxTop = 100;
    public const string UnknownCategoryName = "Chưa phân loại";

    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public ReportService(
        ISaleRepository sales,
        ISaleReturnRepository returns,
        IProductRepository products,
        ICategoryRepository categories,
        ICurrentUser currentUser,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _sales = sales;
        _returns = returns;
        _products = products;
        _categories = categories;
        _currentUser = currentUser;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    private TimeSpan Offset => _options.StoreUtcOffset;

    public async Task<Result<RevenueReportDto>> GetRevenueAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        if ((Authorize(Permissions.ReportsView) ?? Validate(period)) is { } error)
        {
            return error;
        }

        var activity = await LoadAsync(period, cancellationToken);
        var byDay = activity.ByDay();
        var days = new List<RevenueRowDto>(period.Days);
        for (var day = period.From; day <= period.To; day = day.AddDays(1))
        {
            days.Add((byDay.TryGetValue(day, out var dayActivity) ? dayActivity : Activity.Empty).ToRevenueRow(day));
        }

        return new RevenueReportDto(period, days, activity.ToRevenueRow(null));
    }

    public async Task<Result<BestSellerReportDto>> GetBestSellersAsync(BestSellerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if ((Authorize(Permissions.ReportsView) ?? Validate(query.Period)) is { } error)
        {
            return error;
        }

        var activity = await LoadAsync(query.Period, cancellationToken);
        var rows = await ProductRowsAsync(activity, cancellationToken);
        var totalRevenue = rows.Sum(r => r.Revenue);
        if (query.CategoryId is { } categoryId)
        {
            rows = rows.Where(r => r.CategoryId == categoryId).ToList();
        }

        var ranked = Rank(rows, query.RankBy)
            .Take(Math.Clamp(query.Top, 1, MaxTop))
            .Select((r, i) => r with { Rank = i + 1, RevenueSharePercent = ReportMath.Percent(r.Revenue, totalRevenue) })
            .ToList();

        return new BestSellerReportDto(query.Period, query.RankBy, ranked, totalRevenue, rows.Count);
    }

    public async Task<Result<ProfitReportDto>> GetProfitByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        if ((Authorize(Permissions.ReportsView) ?? Validate(period)) is { } error)
        {
            return error;
        }

        var activity = await LoadAsync(period, cancellationToken);
        var products = await ProductRowsAsync(activity, cancellationToken);
        var totalRevenue = products.Sum(p => p.Revenue);

        var rows = products
            .GroupBy(p => (p.CategoryId, p.CategoryName))
            .Select(g => new CategoryProfitRowDto(
                g.Key.CategoryId,
                g.Key.CategoryName,
                g.Count(),
                g.Sum(p => p.Revenue),
                g.Sum(p => p.RevenueExclVat),
                g.Sum(p => p.Cost),
                g.Sum(p => p.Profit),
                ReportMath.Percent(g.Sum(p => p.Revenue), totalRevenue)))
            .OrderByDescending(r => r.Profit)
            .ThenBy(r => r.CategoryName, NameComparer)
            .ToList();

        var totals = new CategoryProfitRowDto(
            0,
            "Tổng cộng",
            products.Count,
            totalRevenue,
            rows.Sum(r => r.RevenueExclVat),
            rows.Sum(r => r.Cost),
            rows.Sum(r => r.Profit),
            totalRevenue == 0 ? 0 : 100);

        return new ProfitReportDto(period, rows, totals);
    }

    public async Task<Result<InventoryValueReportDto>> GetInventoryValueAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        if (!_currentUser.HasPermission(Permissions.ReportsView) && !_currentUser.HasPermission(Permissions.InventoryView))
        {
            return ReportErrors.Denied;
        }

        var products = (await _products.ListAsync(cancellationToken)).Where(p => includeInactive || p.IsActive).ToList();
        var categoryNames = await CategoryNamesAsync(cancellationToken);

        var rows = products
            .GroupBy(p => p.CategoryId)
            .Select(g => InventoryRow(g.Key, CategoryName(categoryNames, g.Key), g.ToList()))
            .OrderByDescending(r => r.CostValue)
            .ThenBy(r => r.CategoryName, NameComparer)
            .ToList();

        var totals = InventoryRow(0, "Tổng cộng", products);
        return new InventoryValueReportDto(_clock.GetUtcNow(), rows, totals);
    }

    public async Task<Result<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.DashboardView, ReportErrors.DashboardDenied) is { } error)
        {
            return error;
        }

        var today = StoreCalendar.DateOf(_clock.GetUtcNow(), Offset);
        var week = new ReportPeriod(today.AddDays(-6), today);
        var activity = await LoadAsync(week, cancellationToken);
        var byDay = activity.ByDay();
        var todayActivity = byDay.GetValueOrDefault(today) ?? Activity.Empty;
        var yesterdayActivity = byDay.GetValueOrDefault(today.AddDays(-1)) ?? Activity.Empty;
        var todayRow = todayActivity.ToRevenueRow(today);
        var yesterdayRevenue = yesterdayActivity.ToRevenueRow(today.AddDays(-1)).NetRevenue;

        var last7 = new List<DailyPointDto>(7);
        for (var day = week.From; day <= week.To; day = day.AddDays(1))
        {
            var row = (byDay.GetValueOrDefault(day) ?? Activity.Empty).ToRevenueRow(day);
            last7.Add(new DailyPointDto(day, row.NetRevenue, row.InvoiceCount));
        }

        var hourly = Enumerable.Range(0, 24)
            .Select(hour =>
            {
                var sales = todayActivity.Sales.Where(s => StoreCalendar.HourOf(s.CompletedAt!.Value, Offset) == hour).ToList();
                return new HourlyPointDto(hour, sales.Sum(s => s.Total), sales.Count);
            })
            .ToList();

        var topProducts = Rank(await ProductRowsAsync(todayActivity, cancellationToken), ProductRanking.Revenue)
            .Where(r => r.Revenue > 0)
            .Take(5)
            .Select(r => new DashboardTopProductDto(r.ProductId, r.ProductCode, r.ProductName, r.Unit, r.Quantity, r.Revenue))
            .ToList();

        var lowStock = await _products.ListLowStockAsync(cancellationToken);
        var lowStockItems = lowStock
            .OrderByDescending(p => p.StockQuantity <= 0)
            .ThenByDescending(p => p.MinStockLevel - p.StockQuantity)
            .ThenBy(p => p.Name, NameComparer)
            .Take(5)
            .Select(p => new LowStockAlert(p.Id, p.Code, p.Name, p.StockQuantity, p.MinStockLevel))
            .ToList();

        return new DashboardDto(
            today,
            todayRow.NetRevenue,
            todayRow.InvoiceCount,
            todayRow.AverageBasket,
            todayRow.RefundTotal,
            _currentUser.HasPermission(Permissions.ReportsView) ? todayRow.GrossProfit : null,
            yesterdayRevenue,
            yesterdayRevenue == 0 ? null : ReportMath.Percent(todayRow.NetRevenue - yesterdayRevenue, yesterdayRevenue),
            last7,
            hourly,
            topProducts,
            lowStock.Count(p => p.StockQuantity <= 0),
            lowStock.Count(p => p.StockQuantity > 0),
            lowStockItems);
    }

    private static StringComparer NameComparer { get; } = StringComparer.Create(Vnd.Culture, ignoreCase: true);

    private static IEnumerable<ProductSalesRowDto> Rank(IEnumerable<ProductSalesRowDto> rows, ProductRanking rankBy)
    {
        var ordered = rankBy switch
        {
            ProductRanking.Revenue => rows.OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Quantity),
            ProductRanking.Profit => rows.OrderByDescending(r => r.Profit).ThenByDescending(r => r.Revenue),
            _ => rows.OrderByDescending(r => r.Quantity).ThenByDescending(r => r.Revenue),
        };

        return ordered.ThenBy(r => r.ProductName, NameComparer);
    }

    private static InventoryValueRowDto InventoryRow(int categoryId, string categoryName, IReadOnlyCollection<Product> products)
    {
        decimal cost = 0, retail = 0, retailExclVat = 0;
        foreach (var product in products.Where(p => p.StockQuantity > 0))
        {
            cost += Money.Round(product.StockQuantity * product.CostPrice);
            var shelf = Money.Round(product.StockQuantity * product.SellPrice);
            retail += shelf;
            retailExclVat += shelf - Money.VatPortionOf(shelf, product.VatRate);
        }

        return new InventoryValueRowDto(
            categoryId,
            categoryName,
            products.Count,
            cost,
            retail,
            retailExclVat,
            products.Count(p => p.StockQuantity <= 0),
            products.Count(p => p.StockQuantity > 0 && p.IsLowStock),
            products.Count(p => p.StockQuantity < 0));
    }

    /// <summary>Per-product totals of an activity window (sales minus returns), with current catalog category.</summary>
    private async Task<List<ProductSalesRowDto>> ProductRowsAsync(Activity activity, CancellationToken cancellationToken)
    {
        var stats = new Dictionary<int, ProductStat>();
        ProductStat StatFor(int productId, string code, string name, string unit)
        {
            if (!stats.TryGetValue(productId, out var stat))
            {
                stat = new ProductStat(code, name, unit);
                stats[productId] = stat;
            }

            return stat;
        }

        foreach (var sale in activity.Sales)
        {
            foreach (var line in sale.Lines)
            {
                var stat = StatFor(line.ProductId, line.ProductCode, line.ProductName, line.Unit);
                stat.Quantity += line.Quantity;
                stat.Revenue += line.FinalAmount;
                stat.Vat += line.VatAmount;
                stat.Cost += line.CostAmount;
                stat.Invoices.Add(sale.Id);
            }
        }

        foreach (var line in activity.Returns.SelectMany(r => r.Lines))
        {
            var stat = StatFor(line.ProductId, line.ProductCode, line.ProductName, line.Unit);
            stat.Quantity -= line.Quantity;
            stat.Revenue -= line.RefundAmount;
            stat.Vat -= line.VatAmount;
            if (line.Restock)
            {
                stat.Cost -= line.CostAmount;
            }
        }

        if (stats.Count == 0)
        {
            return new List<ProductSalesRowDto>();
        }

        var products = (await _products.GetByIdsAsync(stats.Keys, cancellationToken)).ToDictionary(p => p.Id);
        var categoryNames = await CategoryNamesAsync(cancellationToken);

        return stats
            .Select(kv =>
            {
                var categoryId = products.TryGetValue(kv.Key, out var product) ? product.CategoryId : 0;
                var s = kv.Value;
                var exclVat = s.Revenue - s.Vat;
                return new ProductSalesRowDto(
                    0,
                    kv.Key,
                    s.Code,
                    s.Name,
                    s.Unit,
                    categoryId,
                    CategoryName(categoryNames, categoryId),
                    s.Quantity,
                    s.Revenue,
                    exclVat,
                    s.Cost,
                    exclVat - s.Cost,
                    s.Invoices.Count,
                    0);
            })
            .ToList();
    }

    private async Task<Dictionary<int, string>> CategoryNamesAsync(CancellationToken cancellationToken) =>
        (await _categories.ListAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);

    private static string CategoryName(Dictionary<int, string> names, int categoryId) =>
        names.TryGetValue(categoryId, out var name) ? name : UnknownCategoryName;

    private async Task<Activity> LoadAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var from = StoreCalendar.StartOf(period.From, Offset);
        var to = StoreCalendar.StartOf(period.To.AddDays(1), Offset);
        var sales = await _sales.ListCompletedInPeriodAsync(from, to, cancellationToken);
        var returns = await _returns.ListInPeriodAsync(from, to, cancellationToken);
        return new Activity(
            sales.Where(s => s.Status == SaleStatus.Completed).ToList(),
            sales.Where(s => s.Status == SaleStatus.Voided).ToList(),
            returns,
            Offset);
    }

    private Error? Authorize(string permission, Error? denied = null)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(permission) ? null : denied ?? ReportErrors.Denied;
    }

    private static Error? Validate(ReportPeriod? period)
    {
        if (period is null || period.From > period.To)
        {
            return ReportErrors.InvalidPeriod;
        }

        return period.Days > MaxPeriodDays ? ReportErrors.PeriodTooLong(MaxPeriodDays) : null;
    }

    private sealed class ProductStat(string code, string name, string unit)
    {
        public string Code { get; } = code;
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public decimal Quantity { get; set; }
        public decimal Revenue { get; set; }
        public decimal Vat { get; set; }
        public decimal Cost { get; set; }
        public HashSet<int> Invoices { get; } = new();
    }

    /// <summary>Completed sales, voided sales and returns of a window, already loaded.</summary>
    private sealed record Activity(
        IReadOnlyList<Sale> Sales,
        IReadOnlyList<Sale> Voided,
        IReadOnlyList<SaleReturn> Returns,
        TimeSpan Offset)
    {
        public static Activity Empty { get; } = new(Array.Empty<Sale>(), Array.Empty<Sale>(), Array.Empty<SaleReturn>(), TimeSpan.Zero);

        public Dictionary<DateOnly, Activity> ByDay()
        {
            var sales = Sales.ToLookup(s => StoreCalendar.DateOf(s.CompletedAt!.Value, Offset));
            var voided = Voided.ToLookup(s => StoreCalendar.DateOf(s.CompletedAt!.Value, Offset));
            var returns = Returns.ToLookup(r => StoreCalendar.DateOf(r.ReturnedAt, Offset));
            return sales.Select(g => g.Key)
                .Concat(voided.Select(g => g.Key))
                .Concat(returns.Select(g => g.Key))
                .Distinct()
                .ToDictionary(
                    day => day,
                    day => new Activity(sales[day].ToList(), voided[day].ToList(), returns[day].ToList(), Offset));
        }

        public RevenueRowDto ToRevenueRow(DateOnly? date)
        {
            var salesTotal = Sales.Sum(s => s.Total);
            var refunds = Returns.Sum(r => r.RefundTotal);
            var vat = Sales.Sum(s => s.VatTotal) - Returns.Sum(r => r.VatTotal);
            var netRevenue = salesTotal - refunds;
            var cost = Sales.Sum(s => s.CostTotal) - Returns.Sum(r => r.RestockedCost);

            return new RevenueRowDto(
                date,
                Sales.Count,
                Voided.Count,
                Sales.Sum(s => s.GrossAmount),
                Sales.Sum(s => s.TotalDiscount),
                salesTotal,
                Returns.Count,
                refunds,
                netRevenue,
                vat,
                netRevenue - vat,
                cost,
                netRevenue - vat - cost,
                Returns.Sum(r => r.WrittenOffCost),
                Sales.Sum(s => s.CashPaid - s.ChangeDue),
                Sales.Sum(s => PaidBy(s, PaymentMethod.BankTransfer)),
                Sales.Sum(s => PaidBy(s, PaymentMethod.Card)));
        }

        private static decimal PaidBy(Sale sale, PaymentMethod method) =>
            sale.Payments.Where(p => p.Method == method).Sum(p => p.Amount);
    }
}
