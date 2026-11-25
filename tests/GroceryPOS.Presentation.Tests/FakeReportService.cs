using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;

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

    public Task<Result<RevenueReportDto>> GetRevenueAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<BestSellerReportDto>> GetBestSellersAsync(BestSellerQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<ProfitReportDto>> GetProfitByCategoryAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<InventoryValueReportDto>> GetInventoryValueAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
