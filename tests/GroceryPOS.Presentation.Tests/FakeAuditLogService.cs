using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeAuditLogService : IAuditLogService
{
    public List<AuditLogEntryDto> Entries { get; } = new();
    public List<string> Usernames { get; } = new() { "admin", "quanly", "thungan1" };
    public List<AuditLogQuery> Searches { get; } = new();
    public List<ReportPeriod> Summaries { get; } = new();
    public List<AuditLogQuery> Exports { get; } = new();
    public Error? SearchError { get; set; }
    public Error? SummaryError { get; set; }
    public Error? UsersError { get; set; }
    public Error? ExportError { get; set; }

    /// <summary>When set, each summary call waits for the next gate in the queue (to finish calls out of order).</summary>
    public Queue<TaskCompletionSource> SummaryGates { get; } = new();

    public Func<ReportPeriod, AuditLogSummaryDto> Summary { get; set; } =
        p => new AuditLogSummaryDto(p, Total: 12, Critical: 2, Warning: 5, FailedLogins: 3, AccountsLocked: 0, SalesVoided: 1, Returns: 2, Reprints: 0, PriceChanges: 1, StockAdjustments: 2);

    public Task<Result<AuditLogPageDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        Searches.Add(query);
        if (SearchError is { } e)
        {
            return Task.FromResult(Result.Failure<AuditLogPageDto>(e));
        }

        return Task.FromResult(Result.Success(new AuditLogPageDto(Entries.ToList(), Entries.Count, query.PageIndex, query.PageSize)));
    }

    public async Task<Result<AuditLogSummaryDto>> GetSummaryAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        Summaries.Add(period);
        if (SummaryGates.Count > 0)
        {
            await SummaryGates.Dequeue().Task;
        }

        return SummaryError is { } e ? Result.Failure<AuditLogSummaryDto>(e) : Result.Success(Summary(period));
    }

    public Task<Result<IReadOnlyList<string>>> ListUsernamesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(UsersError is { } e
            ? Result.Failure<IReadOnlyList<string>>(e)
            : Result.Success<IReadOnlyList<string>>(Usernames.ToList()));

    public Task<Result<ReportFile>> ExportAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        Exports.Add(query);
        return Task.FromResult(ExportError is { } e
            ? Result.Failure<ReportFile>(e)
            : Result.Success(new ReportFile(AuditLogService.FileName(query.Period) + ".xlsx", [1, 2, 3])));
    }

    public static AuditLogEntryDto Entry(
        int id,
        string action,
        string username = "quanly",
        AuditSeverity severity = AuditSeverity.Info,
        string? entityId = null,
        string? details = null) =>
        new(id, new DateTime(2026, 11, 20, 9, 30, 15), action, "Tên " + action, AuditCategory.Sales, "Bán hàng & trả hàng",
            severity, AuditCatalog.SeverityName(severity), username == "(không rõ)" ? null : 7, username,
            entityId is null ? null : "Sale", entityId, entityId is null ? null : "Hóa đơn " + entityId, details);
}
