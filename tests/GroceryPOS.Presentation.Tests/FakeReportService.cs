using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeReportService : IReportService
{
    public DashboardDto? Dashboard { get; set; }
    public Error? FailWith { get; set; }
    public int DashboardCalls { get; private set; }

    /// <summary>Awaited inside the dashboard query when set (to overlap two loads); cleared after use.</summary>
    public Func<Task>? BeforeDashboardReturns { get; set; }

    public async Task<Result<DashboardDto>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        DashboardCalls++;
        var dto = Dashboard;
        var fail = FailWith;
        if (BeforeDashboardReturns is { } wait)
        {
            BeforeDashboardReturns = null;
            await wait();
        }

        return fail is { } e ? Result.Failure<DashboardDto>(e) : Result.Success(dto ?? throw new InvalidOperationException("No dashboard set."));
    }

    // ---- Báo cáo ------------------------------------------------------------------------------------------

    public List<string> Calls { get; } = new();
    public Func<ReportPeriod, RevenueReportDto>? Revenue { get; set; }
    public BestSellerReportDto? BestSellers { get; set; }
    public ProfitReportDto? Profit { get; set; }
    public InventoryValueReportDto? Inventory { get; set; }

    /// <summary>Failure per report ("revenue", "previous", "best", "profit", "inventory").</summary>
    public Dictionary<string, Error> Failures { get; } = new();

    /// <summary>Awaited before a report query returns, once (to overlap loads).</summary>
    public Func<Task>? BeforeReportReturns { get; set; }

    public List<BestSellerQuery> BestSellerQueries { get; } = new();

    /// <summary>The first revenue call of a load is the period itself, the second the previous period.</summary>
    public async Task<Result<RevenueReportDto>> GetRevenueAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var previous = Calls.Count > 0 && Calls[^1] == $"revenue:{Describe(PeriodAfter(period))}";
        Calls.Add($"revenue:{Describe(period)}");
        await Wait();
        var key = previous ? "previous" : "revenue";
        return Failures.TryGetValue(key, out var e)
            ? Result.Failure<RevenueReportDto>(e)
            : Result.Success((Revenue ?? throw new InvalidOperationException("No revenue set."))(period));
    }

    public async Task<Result<BestSellerReportDto>> GetBestSellersAsync(BestSellerQuery query, CancellationToken cancellationToken = default)
    {
        Calls.Add("best");
        BestSellerQueries.Add(query);
        await Wait();
        return Failures.TryGetValue("best", out var e)
            ? Result.Failure<BestSellerReportDto>(e)
            : Result.Success(BestSellers ?? throw new InvalidOperationException("No best sellers set."));
    }

    public async Task<Result<ProfitReportDto>> GetProfitByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        Calls.Add($"profit:{Describe(period)}");
        await Wait();
        return Failures.TryGetValue("profit", out var e)
            ? Result.Failure<ProfitReportDto>(e)
            : Result.Success(Profit ?? throw new InvalidOperationException("No profit set."));
    }

    public async Task<Result<InventoryValueReportDto>> GetInventoryValueAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        Calls.Add("inventory");
        await Wait();
        return Failures.TryGetValue("inventory", out var e)
            ? Result.Failure<InventoryValueReportDto>(e)
            : Result.Success(Inventory ?? throw new InvalidOperationException("No inventory set."));
    }

    public static string Describe(ReportPeriod period) => $"{period.From:MMdd}-{period.To:MMdd}";

    private static ReportPeriod PeriodAfter(ReportPeriod previous) =>
        new(previous.To.AddDays(1), previous.To.AddDays(previous.Days));

    private async Task Wait()
    {
        if (BeforeReportReturns is { } wait)
        {
            BeforeReportReturns = null;
            await wait();
        }
    }
}

internal sealed class FakeReportExportService : IReportExportService
{
    public List<ReportExportRequest> Exports { get; } = new();
    public List<ReportExportRequest> Prepared { get; } = new();
    public List<(ReportOutput Output, string? Target)> Printed { get; } = new();
    public Error? FailWith { get; set; }

    public Task<Result<ReportFile>> ExportAsync(ReportExportRequest request, CancellationToken cancellationToken = default)
    {
        Exports.Add(request);
        return Task.FromResult(FailWith is { } e ? Result.Failure<ReportFile>(e) : Result.Success(new ReportFile("BaoCao.xlsx", [1, 2, 3])));
    }

    public Task<Result<ReportDocument>> PrepareAsync(ReportExportRequest request, CancellationToken cancellationToken = default)
    {
        Prepared.Add(request);
        return Task.FromResult(FailWith is { } e
            ? Result.Failure<ReportDocument>(e)
            : Result.Success(new ReportDocument("BaoCao_DoanhThu", "Kỳ", [], "Tạp hóa", new DateTime(2026, 11, 20, 10, 0, 0))));
    }

    public Task RecordPrintedAsync(ReportDocument document, ReportOutput output, string? target = null, CancellationToken cancellationToken = default)
    {
        Printed.Add((output, target));
        return Task.CompletedTask;
    }
}
