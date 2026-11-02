using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Auditing;

public interface IAuditLogService
{
    /// <summary>One page of the audit trail (<c>auditlog.view</c>).</summary>
    Task<Result<AuditLogPageDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>KPI counts for a period (<c>auditlog.view</c>).</summary>
    Task<Result<AuditLogSummaryDto>> GetSummaryAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>Usernames for the user filter (<c>auditlog.view</c>).</summary>
    Task<Result<IReadOnlyList<string>>> ListUsernamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every entry matching <paramref name="query"/> (paging ignored, at most <see cref="AuditLogService.MaxExportRows"/>)
    /// as an Excel workbook. The export itself is audited as <c>auditlog.exported</c>.
    /// </summary>
    Task<Result<ReportFile>> ExportAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Read-only access to the audit trail for owners/managers: filter, page, summarize and export.</summary>
public sealed class AuditLogService : IAuditLogService
{
    public const int MaxExportRows = 20_000;
    private const string NoUser = "(không rõ)";

    private readonly IAuditLogRepository _logs;
    private readonly ICurrentUser _currentUser;
    private readonly IReportWorkbookWriter _writer;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly StoreProfile _store;
    private readonly SalesOptions _options;

    public AuditLogService(
        IAuditLogRepository logs,
        ICurrentUser currentUser,
        IReportWorkbookWriter writer,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        StoreProfile? store = null,
        SalesOptions? options = null)
    {
        _logs = logs;
        _currentUser = currentUser;
        _writer = writer;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _store = store ?? StoreProfile.Default;
        _options = options ?? SalesOptions.Default;
    }

    private TimeSpan Offset => _options.StoreUtcOffset;

    public async Task<Result<AuditLogPageDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if ((Authorize() ?? Validate(query.Period)) is { } error)
        {
            return error;
        }

        var pageSize = Math.Clamp(query.PageSize, 1, AuditLogQuery.MaxPageSize);
        var pageIndex = Math.Max(0, query.PageIndex);
        var filter = BuildFilter(query) with { Skip = pageIndex * pageSize, Take = pageSize };
        var result = await _logs.SearchAsync(filter, cancellationToken);

        // Past the last page (filters narrowed while on a later page): show the last page instead.
        if (result.Items.Count == 0 && result.TotalCount > 0 && pageIndex > 0)
        {
            pageIndex = (result.TotalCount - 1) / pageSize;
            result = await _logs.SearchAsync(filter with { Skip = pageIndex * pageSize }, cancellationToken);
        }

        return new AuditLogPageDto(result.Items.Select(ToDto).ToList(), result.TotalCount, pageIndex, pageSize);
    }

    public async Task<Result<AuditLogSummaryDto>> GetSummaryAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        if ((Authorize() ?? Validate(period)) is { } error)
        {
            return error;
        }

        var (from, to) = Range(period);
        var counts = await _logs.CountByActionAsync(from, to, cancellationToken);
        int Count(string action) => counts.TryGetValue(action, out var n) ? n : 0;
        int CountWhere(AuditSeverity severity) =>
            counts.Where(c => AuditCatalog.Describe(c.Key).Severity == severity).Sum(c => c.Value);

        return new AuditLogSummaryDto(
            period,
            Total: counts.Values.Sum(),
            Critical: CountWhere(AuditSeverity.Critical),
            Warning: CountWhere(AuditSeverity.Warning),
            FailedLogins: Count(AuditActions.LoginFailed),
            AccountsLocked: Count(AuditActions.AccountLocked),
            SalesVoided: Count(AuditActions.SaleVoided),
            Returns: Count(AuditActions.SaleReturned),
            Reprints: Count(AuditActions.ReceiptReprinted),
            PriceChanges: Count(AuditActions.PriceChanged),
            StockAdjustments: Count(AuditActions.StockAdjusted) + Count(AuditActions.StockCounted));
    }

    public async Task<Result<IReadOnlyList<string>>> ListUsernamesAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        return Result.Success(await _logs.ListUsernamesAsync(cancellationToken));
    }

    public async Task<Result<ReportFile>> ExportAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if ((Authorize() ?? Validate(query.Period)) is { } error)
        {
            return Result.Failure<ReportFile>(error);
        }

        var filter = BuildFilter(query) with { Skip = 0, Take = MaxExportRows };
        var result = await _logs.SearchAsync(filter, cancellationToken);
        if (result.TotalCount == 0)
        {
            return Result.Failure<ReportFile>(AuditLogErrors.NothingToExport);
        }

        if (result.TotalCount > MaxExportRows)
        {
            return Result.Failure<ReportFile>(AuditLogErrors.TooManyRows(result.TotalCount, MaxExportRows));
        }

        var now = _clock.GetUtcNow().ToOffset(Offset).DateTime;
        var session = _currentUser.Session;
        var header = new ReportHeader(_store.Name, session?.FullName ?? session?.Username ?? "?", now);
        var sheet = BuildSheet(result.Items.Select(ToDto).ToList(), query, header);
        var content = await _writer.WriteAsync([sheet], cancellationToken);

        var filterText = DescribeFilters(query);
        _audit.Record(
            AuditActions.AuditLogExported,
            details: $"{result.TotalCount} dòng – {ReportSheets.PeriodLine(query.Period)}{(filterText is null ? string.Empty : " – " + filterText)}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReportFile(FileName(query.Period) + _writer.FileExtension, content);
    }

    /// <summary>"NhatKy_20261101-20261102" (or "NhatKy_20261102" for one day).</summary>
    public static string FileName(ReportPeriod period)
    {
        var from = period.From.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var to = period.To.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return period.From == period.To ? $"NhatKy_{from}" : $"NhatKy_{from}-{to}";
    }

    /// <summary>Exported sheet: newest first (as filtered), time as text so seconds survive every viewer.</summary>
    public static ReportSheet BuildSheet(IReadOnlyList<AuditLogEntryDto> entries, AuditLogQuery query, ReportHeader header)
    {
        var subtitles = header.Lines(ReportSheets.PeriodLine(query.Period)).ToList();
        if (DescribeFilters(query) is { } filters)
        {
            subtitles.Insert(subtitles.Count - 1, "Bộ lọc: " + filters);
        }

        ReportColumn[] columns =
        [
            new("Thời gian", ReportColumnKind.Text, 20),
            new("Người dùng", ReportColumnKind.Text, 16),
            new("Nhóm", ReportColumnKind.Text, 22),
            new("Hành động", ReportColumnKind.Text, 26),
            new("Mức độ", ReportColumnKind.Text, 12),
            new("Đối tượng", ReportColumnKind.Text, 26),
            new("Chi tiết", ReportColumnKind.Text, 70),
        ];

        var rows = entries.Select(e => (IReadOnlyList<object?>)new object?[]
        {
            e.Time.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            e.Username,
            e.CategoryName,
            e.ActionName,
            e.SeverityName,
            e.Entity,
            e.Details,
        }).ToList();

        return new ReportSheet("Nhật ký", "NHẬT KÝ HỆ THỐNG", subtitles, columns, rows);
    }

    /// <summary>Human-readable active filters ("Nhóm: Bán hàng & trả hàng; Người dùng: thungan1"), null when none.</summary>
    public static string? DescribeFilters(AuditLogQuery query)
    {
        var parts = new List<string>();
        if (query.Category is { } category and not AuditCategory.Other)
        {
            parts.Add($"Nhóm: {AuditCatalog.CategoryName(category)}");
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            parts.Add($"Hành động: {AuditCatalog.Describe(query.Action.Trim()).Name}");
        }

        if (query.MinSeverity is { } severity and > AuditSeverity.Info)
        {
            parts.Add(severity == AuditSeverity.Critical ? "Chỉ sự kiện quan trọng" : "Từ mức cần chú ý");
        }

        if (!string.IsNullOrWhiteSpace(query.Username))
        {
            parts.Add($"Người dùng: {query.Username.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parts.Add($"Tìm: \"{query.Search.Trim()}\"");
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private AuditLogFilter BuildFilter(AuditLogQuery query)
    {
        var (from, to) = Range(query.Period);
        var search = query.Search?.Trim();
        if (search is { Length: > AuditLogQuery.MaxSearchLength })
        {
            search = search[..AuditLogQuery.MaxSearchLength];
        }

        return new AuditLogFilter(
            from,
            to,
            ResolveActions(query),
            string.IsNullOrWhiteSpace(query.Username) ? null : query.Username.Trim(),
            string.IsNullOrEmpty(search) ? null : search,
            query.OldestFirst);
    }

    /// <summary>Intersection of the category, action and severity filters; null = no action filter.</summary>
    public static IReadOnlyCollection<string>? ResolveActions(AuditLogQuery query)
    {
        IEnumerable<string>? actions = null;
        if (query.Category is { } category and not AuditCategory.Other)
        {
            actions = AuditCatalog.ActionsIn(category);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            actions = actions is null ? [action] : actions.Where(a => a == action);
        }

        if (query.MinSeverity is { } severity and > AuditSeverity.Info)
        {
            var allowed = AuditCatalog.ActionsAtLeast(severity);
            actions = actions is null ? allowed : actions.Intersect(allowed);
        }

        return actions?.Distinct().ToList();
    }

    private AuditLogEntryDto ToDto(AuditLog log)
    {
        var info = AuditCatalog.Describe(log.Action);
        return new AuditLogEntryDto(
            log.Id,
            log.Timestamp.ToOffset(Offset).DateTime,
            log.Action,
            info.Name,
            info.Category,
            AuditCatalog.CategoryName(info.Category),
            info.Severity,
            AuditCatalog.SeverityName(info.Severity),
            log.UserId,
            string.IsNullOrWhiteSpace(log.Username) ? NoUser : log.Username,
            log.EntityName,
            log.EntityId,
            AuditCatalog.DescribeEntity(log.EntityName, log.EntityId),
            log.Details);
    }

    private (DateTimeOffset From, DateTimeOffset To) Range(ReportPeriod period) =>
        (StoreCalendar.StartOf(period.From, Offset), StoreCalendar.StartOf(period.To.AddDays(1), Offset));

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.AuditLogView) ? null : AuditLogErrors.Denied;
    }

    private static Error? Validate(ReportPeriod? period)
    {
        if (period is null || period.From > period.To)
        {
            return ReportErrors.InvalidPeriod;
        }

        return period.Days > ReportService.MaxPeriodDays ? ReportErrors.PeriodTooLong(ReportService.MaxPeriodDays) : null;
    }
}
