using System.Globalization;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Presentation.Auditing;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Reports;

/// <summary>The Báo cáo screen as seen by its presenter.</summary>
public interface IReportsView : IView
{
    /// <summary>
    /// Fills the category filter of Hàng bán chạy and selects <paramref name="selected"/> without reporting it back as a
    /// user change; <paramref name="visible"/> is false when the categories could not be listed (no <c>products.view</c>).
    /// </summary>
    void ShowCategories(IReadOnlyList<FilterOption<int?>> options, int? selected, bool visible);

    /// <summary>The line next to the period picker ("01/11 – 30/11/2026 · cập nhật lúc 10:00", errors in red).</summary>
    void ShowStatus(string text, Tone tone);

    /// <summary>The period picker is off on Giá trị tồn kho (current stock, not a period).</summary>
    void ShowPeriodEnabled(bool enabled);

    /// <summary>Shows or hides the loading overlay over the tabs.</summary>
    void ShowLoading(bool loading);

    void ShowRevenue(RevenueBoard board);

    void ShowBestSellers(BestSellerBoard board);

    void ShowProfit(ProfitBoard board);

    void ShowInventory(InventoryBoard board);

    Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message);

    /// <summary>Asks where to save the workbook and writes it; the saved path, or null when cancelled or failed.</summary>
    Task<string?> SaveExcelAsync(string suggestedFileName, byte[] content);

    /// <summary>
    /// Opens the A4 print preview (modal). <paramref name="onOutput"/> runs each time the document was printed or saved
    /// as PDF (with the PDF path).
    /// </summary>
    void ShowPrintPreview(ReportDocument document, Func<ReportOutput, string?, Task> onOutput);
}

/// <summary>
/// Báo cáo logic (<c>reports.view</c>): one period for Doanh thu, Hàng bán chạy and Lợi nhuận theo danh mục, plus Giá trị
/// tồn kho (current stock). Only the visible tab loads; a tab whose inputs changed reloads when shown, one load runs at a
/// time and the newest inputs win (an answer for older inputs is dropped). A failed load keeps the figures when they
/// were for the same inputs (amber "đang hiển thị số liệu lúc …"), otherwise clears the tab so no other period's numbers
/// stay under the new period. Xuất Excel / In – PDF for the current tab or all tabs, one at a time.
/// </summary>
public sealed class ReportsPresenter : Presenter<IReportsView>
{
    public const string Title = "Báo cáo";
    public const ProductRanking DefaultRankBy = ProductRanking.Quantity;
    public const int DefaultTop = 20;
    public const string InventoryHint = "Giá trị tồn kho tính tại thời điểm hiện tại, không theo kỳ.";

    public static readonly IReadOnlyList<FilterOption<ProductRanking>> RankOptions =
    [
        new(ProductRanking.Quantity, "Theo số lượng"),
        new(ProductRanking.Revenue, "Theo doanh thu"),
        new(ProductRanking.Profit, "Theo lợi nhuận"),
    ];

    public static readonly IReadOnlyList<FilterOption<int>> TopOptions =
    [
        new(10, "Top 10"),
        new(20, "Top 20"),
        new(50, "Top 50"),
        new(100, "Top 100"),
    ];

    public static readonly FilterOption<int?> AllCategories = new(null, "Tất cả danh mục");

    private const int TabCount = 4;

    private readonly TimeProvider _clock;
    private readonly int[] _versions = new int[TabCount];
    private readonly int[] _loadedVersions = [-1, -1, -1, -1];
    private readonly int[] _failedVersions = [-1, -1, -1, -1];
    private readonly RequestKey?[] _shownKeys = new RequestKey?[TabCount];
    private readonly DateTime[] _shownAt = new DateTime[TabCount];
    private readonly (string Text, Tone Tone)[] _statuses = Enumerable.Repeat((string.Empty, Tone.Muted), TabCount).ToArray();
    private IReadOnlyList<FilterOption<int?>> _categories = [AllCategories];
    private bool _categoriesVisible = true;
    private string _periodText = string.Empty;
    private bool _loading;
    private bool _busy;

    public ReportsPresenter(IServiceScopeFactory scopeFactory, TimeProvider clock)
        : base(scopeFactory)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ReportPeriod? Period { get; private set; }

    public ReportTab Tab { get; private set; } = ReportTab.Revenue;

    public ProductRanking RankBy { get; private set; } = DefaultRankBy;

    public int Top { get; private set; } = DefaultTop;

    /// <summary>The best-seller category (null = all; always null while the filter is hidden).</summary>
    public int? CategoryId { get; private set; }

    public IReadOnlyList<FilterOption<int?>> Categories => _categories;

    public bool IsLoading => _loading;

    /// <summary>True while an export or print is being prepared.</summary>
    public bool IsBusy => _busy;

    /// <summary>True when <paramref name="tab"/> shows figures for its current inputs.</summary>
    public bool IsLoaded(ReportTab tab) => _loadedVersions[(int)tab] == _versions[(int)tab];

    private DateTime Now => _clock.GetLocalNow().DateTime;

    protected override void OnAttached()
    {
        View.ShowPeriodEnabled(true);
        View.ShowRevenue(RevenueBoard.Empty);
        View.ShowBestSellers(BestSellerBoard.Empty);
        View.ShowProfit(ProfitBoard.Empty);
        View.ShowInventory(InventoryBoard.Empty);
        View.ShowStatus("Đang tải báo cáo…", Tone.Muted);
    }

    /// <summary>First load: the category filter, then the visible tab for <paramref name="period"/>.</summary>
    public async Task LoadAsync(ReportPeriod period, string periodText)
    {
        SetPeriod(period, periodText);
        await LoadCategoriesAsync();
        InvalidateAll(includingInventory: true);
        await LoadCurrentAsync();
    }

    /// <summary>The period changed: the period tabs are reloaded (the visible one now, the others when shown).</summary>
    public async Task SetPeriodAsync(ReportPeriod period, string periodText)
    {
        var changed = Period != period;
        SetPeriod(period, periodText);
        if (changed)
        {
            InvalidateAll(includingInventory: false);
        }

        await LoadCurrentAsync();
    }

    /// <summary>F5: re-lists the categories and reloads every tab (inventory too).</summary>
    public async Task RefreshAsync(ReportPeriod period, string periodText)
    {
        SetPeriod(period, periodText);
        await LoadCategoriesAsync();
        InvalidateAll(includingInventory: true);
        await LoadCurrentAsync();
    }

    /// <summary>The user opened <paramref name="tab"/>: shows its status and loads it when stale (a failed load is retried).</summary>
    public async Task SelectTabAsync(ReportTab tab)
    {
        Tab = tab;
        View.ShowPeriodEnabled(tab != ReportTab.InventoryValue);
        _failedVersions[(int)tab] = -1;
        ShowTabStatus();
        await LoadCurrentAsync();
    }

    /// <summary>A best-seller filter changed (rank, top N, category).</summary>
    public async Task SetBestSellerFiltersAsync(ProductRanking rankBy, int top, int? categoryId)
    {
        if (!_categoriesVisible || _categories.All(c => c.Value != categoryId))
        {
            categoryId = null;
        }

        if (rankBy == RankBy && top == Top && categoryId == CategoryId)
        {
            return;
        }

        RankBy = rankBy;
        Top = top <= 0 ? DefaultTop : top;
        CategoryId = categoryId;
        Invalidate(ReportTab.BestSellers);
        await LoadCurrentAsync();
    }

    /// <summary>What Xuất Excel / In use: the current tab, or every tab when <paramref name="all"/>.</summary>
    public ReportExportRequest BuildRequest(bool all)
    {
        var period = Period ?? throw new InvalidOperationException("No period yet – call LoadAsync first.");
        IReadOnlyList<ReportKind> kinds = all
            ? [ReportKind.Revenue, ReportKind.BestSellers, ReportKind.ProfitByCategory, ReportKind.InventoryValue]
            : [(ReportKind)(int)Tab];
        var categoryName = CategoryId is null ? null : _categories.FirstOrDefault(c => c.Value == CategoryId)?.Name;
        return new ReportExportRequest(kinds, period, RankBy, Top, CategoryId, categoryName);
    }

    /// <summary>Xuất Excel (Ctrl+E) / Xuất tất cả: builds the workbook (audited) and asks where to save it.</summary>
    public async Task ExportAsync(bool all)
    {
        if (_busy || Period is null)
        {
            return;
        }

        _busy = true;
        try
        {
            var request = BuildRequest(all);
            var result = await View.RunBusyAsync(
                () => UseAsync<IReportExportService, Result<ReportFile>>(s => s.ExportAsync(request)),
                "Đang tạo file Excel…");
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowWarning(result.Error.Message, Title);
                return;
            }

            var path = await View.SaveExcelAsync(result.Value.FileName, result.Value.Content);
            if (path is not null && ViewAlive)
            {
                View.ToastSuccess($"Đã lưu {Path.GetFileName(path)}.", "Xuất báo cáo");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>In / PDF (Ctrl+P, all tabs with Ctrl+Shift+P): prepares the sheets and opens the print preview.</summary>
    public async Task PrintAsync(bool all)
    {
        if (_busy || Period is null)
        {
            return;
        }

        _busy = true;
        ReportDocument document;
        try
        {
            var request = BuildRequest(all);
            var result = await View.RunBusyAsync(
                () => UseAsync<IReportExportService, Result<ReportDocument>>(s => s.PrepareAsync(request)),
                "Đang chuẩn bị bản in…");
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowWarning(result.Error.Message, Title);
                return;
            }

            document = result.Value;
        }
        finally
        {
            _busy = false;
        }

        View.ShowPrintPreview(document, (output, target) => RecordOutputAsync(document, output, target));
    }

    /// <summary>Audits a printed report or a saved PDF (a fresh scope per entry while the preview stays open).</summary>
    public async Task RecordOutputAsync(ReportDocument document, ReportOutput output, string? target)
    {
        ArgumentNullException.ThrowIfNull(document);
        await UseAsync<IReportExportService, bool>(async s =>
        {
            await s.RecordPrintedAsync(document, output, target).ConfigureAwait(true);
            return true;
        });

        if (ViewAlive)
        {
            View.ToastSuccess(
                output == ReportOutput.Pdf
                    ? $"Đã lưu PDF {Path.GetFileName(target ?? string.Empty)}".TrimEnd() + "."
                    : $"Đã gửi {document.Names} tới máy in.",
                "In báo cáo");
        }
    }

    // ---- Loading ------------------------------------------------------------------------------------------

    private void SetPeriod(ReportPeriod period, string periodText)
    {
        ArgumentNullException.ThrowIfNull(period);
        Period = period;
        _periodText = periodText ?? string.Empty;
    }

    private void Invalidate(ReportTab tab) => _versions[(int)tab]++;

    private void InvalidateAll(bool includingInventory)
    {
        foreach (var tab in Enum.GetValues<ReportTab>())
        {
            if (tab != ReportTab.InventoryValue || includingInventory)
            {
                Invalidate(tab);
            }
        }
    }

    private async Task LoadCategoriesAsync()
    {
        var result = await UseAsync<ICategoryService, Result<IReadOnlyList<CategoryDto>>>(s => s.ListAsync());
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            // No products.view (or no answer): rank across all categories only.
            _categories = [AllCategories];
            _categoriesVisible = false;
        }
        else
        {
            _categories =
            [
                AllCategories,
                .. result.Value
                    .OrderBy(c => c.Name, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true))
                    .Select(c => new FilterOption<int?>(c.Id, c.Name)),
            ];
            _categoriesVisible = true;
        }

        if (CategoryId is not null && (!_categoriesVisible || _categories.All(c => c.Value != CategoryId)))
        {
            // The chosen category is gone (or the filter is hidden): rank across all categories again.
            CategoryId = null;
            Invalidate(ReportTab.BestSellers);
        }

        View.ShowCategories(_categories, CategoryId, _categoriesVisible);
    }

    private async Task LoadCurrentAsync()
    {
        if (_loading || Period is null || !ViewAlive)
        {
            return; // a running load picks up the newest inputs when it finishes
        }

        _loading = true;
        View.ShowLoading(true);
        try
        {
            while (ViewAlive)
            {
                var tab = Tab;
                var version = _versions[(int)tab];
                if (_loadedVersions[(int)tab] == version || _failedVersions[(int)tab] == version)
                {
                    break;
                }

                await LoadTabAsync(tab, version);
            }
        }
        finally
        {
            _loading = false;
            if (ViewAlive)
            {
                View.ShowLoading(false);
            }
        }
    }

    private async Task LoadTabAsync(ReportTab tab, int version)
    {
        var period = Period!;
        var key = KeyFor(tab);
        string? error = tab switch
        {
            ReportTab.Revenue => await LoadRevenueAsync(period, tab, version),
            ReportTab.BestSellers => await LoadBestSellersAsync(new BestSellerQuery(period, RankBy, Top, CategoryId), tab, version),
            ReportTab.ProfitByCategory => await LoadProfitAsync(period, tab, version),
            _ => await LoadInventoryAsync(tab, version),
        };

        if (!ViewAlive || _versions[(int)tab] != version)
        {
            return; // the inputs changed meanwhile: the loop loads them
        }

        var now = Now;
        var index = (int)tab;
        if (error is null)
        {
            _loadedVersions[index] = version;
            _shownKeys[index] = key;
            _shownAt[index] = now;
            _statuses[index] = tab == ReportTab.InventoryValue
                ? ($"Tồn kho lúc {Time(now)} · tính tại thời điểm hiện tại, không theo kỳ.", Tone.Muted)
                : ($"{_periodText} · cập nhật lúc {Time(now)}", Tone.Muted);
        }
        else
        {
            _failedVersions[index] = version;
            if (_shownKeys[index] == key)
            {
                // Same inputs as on screen (e.g. F5): keep the figures and say how old they are.
                _statuses[index] = ($"Không cập nhật được ({error}) · đang hiển thị số liệu lúc {Time(_shownAt[index])}", Tone.Warning);
            }
            else
            {
                ClearTab(tab);
                _shownKeys[index] = null;
                _statuses[index] = (error, Tone.Danger);
            }
        }

        if (tab == Tab)
        {
            ShowTabStatus();
        }
    }

    private bool Current(ReportTab tab, int version) => ViewAlive && _versions[(int)tab] == version;

    private async Task<string?> LoadRevenueAsync(ReportPeriod period, ReportTab tab, int version)
    {
        var (report, previous) = await UseAsync<IReportService, (Result<RevenueReportDto>, Result<RevenueReportDto>?)>(async s =>
        {
            var current = await s.GetRevenueAsync(period).ConfigureAwait(true);
            if (current.IsFailure)
            {
                return (current, null);
            }

            return (current, await s.GetRevenueAsync(ReportFigures.PreviousPeriod(period)).ConfigureAwait(true));
        });

        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (Current(tab, version))
        {
            View.ShowRevenue(RevenueBoard.From(report.Value, previous is { IsSuccess: true } p ? p.Value : null));
        }

        return null;
    }

    private async Task<string?> LoadBestSellersAsync(BestSellerQuery query, ReportTab tab, int version)
    {
        var report = await UseAsync<IReportService, Result<BestSellerReportDto>>(s => s.GetBestSellersAsync(query));
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (Current(tab, version))
        {
            View.ShowBestSellers(BestSellerBoard.From(report.Value));
        }

        return null;
    }

    private async Task<string?> LoadProfitAsync(ReportPeriod period, ReportTab tab, int version)
    {
        var report = await UseAsync<IReportService, Result<ProfitReportDto>>(s => s.GetProfitByCategoryAsync(period));
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (Current(tab, version))
        {
            View.ShowProfit(ProfitBoard.From(report.Value));
        }

        return null;
    }

    private async Task<string?> LoadInventoryAsync(ReportTab tab, int version)
    {
        var report = await UseAsync<IReportService, Result<InventoryValueReportDto>>(s => s.GetInventoryValueAsync());
        if (report.IsFailure)
        {
            return report.Error.Message;
        }

        if (Current(tab, version))
        {
            View.ShowInventory(InventoryBoard.From(report.Value));
        }

        return null;
    }

    private void ClearTab(ReportTab tab)
    {
        switch (tab)
        {
            case ReportTab.Revenue:
                View.ShowRevenue(RevenueBoard.Empty);
                break;
            case ReportTab.BestSellers:
                View.ShowBestSellers(BestSellerBoard.Empty);
                break;
            case ReportTab.ProfitByCategory:
                View.ShowProfit(ProfitBoard.Empty);
                break;
            default:
                View.ShowInventory(InventoryBoard.Empty);
                break;
        }
    }

    private void ShowTabStatus()
    {
        var (text, tone) = _statuses[(int)Tab];
        if (string.IsNullOrEmpty(text))
        {
            (text, tone) = Tab == ReportTab.InventoryValue ? (InventoryHint, Tone.Muted) : ("Đang tải báo cáo…", Tone.Muted);
        }

        View.ShowStatus(text, tone);
    }

    private RequestKey KeyFor(ReportTab tab) => tab switch
    {
        ReportTab.InventoryValue => new RequestKey(null, default, 0, null),
        ReportTab.BestSellers => new RequestKey(Period, RankBy, Top, CategoryId),
        _ => new RequestKey(Period, default, 0, null),
    };

    private static string Time(DateTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The inputs a tab's figures were loaded for.</summary>
    private sealed record RequestKey(ReportPeriod? Period, ProductRanking RankBy, int Top, int? CategoryId);
}
