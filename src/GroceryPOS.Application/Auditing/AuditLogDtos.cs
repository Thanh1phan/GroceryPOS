using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Reports;

namespace GroceryPOS.Application.Auditing;

/// <summary>
/// Audit log screen query. Filters combine with AND: <paramref name="Category"/> and <paramref name="Action"/> narrow
/// the action keys, <paramref name="MinSeverity"/> keeps only actions of that severity or higher,
/// <paramref name="Username"/> is an exact username and <paramref name="Search"/> a substring of the username,
/// object id or details. <paramref name="PageIndex"/> is zero-based and clamped to the last page.
/// </summary>
public sealed record AuditLogQuery(
    ReportPeriod Period,
    AuditCategory? Category = null,
    string? Action = null,
    AuditSeverity? MinSeverity = null,
    string? Username = null,
    string? Search = null,
    bool OldestFirst = false,
    int PageIndex = 0,
    int PageSize = AuditLogQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 100;
}

/// <summary>One audit entry ready for display; <see cref="Time"/> is store-local.</summary>
public sealed record AuditLogEntryDto(
    int Id,
    DateTime Time,
    string Action,
    string ActionName,
    AuditCategory Category,
    string CategoryName,
    AuditSeverity Severity,
    string SeverityName,
    int? UserId,
    string Username,
    string? EntityName,
    string? EntityId,
    string? Entity,
    string? Details);

public sealed record AuditLogPageDto(IReadOnlyList<AuditLogEntryDto> Items, int TotalCount, int PageIndex, int PageSize);

/// <summary>Counts for the KPI cards over a whole period (other filters ignored).</summary>
public sealed record AuditLogSummaryDto(
    ReportPeriod Period,
    int Total,
    int Critical,
    int Warning,
    int FailedLogins,
    int AccountsLocked,
    int SalesVoided,
    int Returns,
    int Reprints,
    int PriceChanges,
    int StockAdjustments)
{
    public int SalesExceptions => SalesVoided + Returns + Reprints;

    public int StockAndPrice => PriceChanges + StockAdjustments;
}

public static class AuditLogErrors
{
    public static readonly Error Denied = Error.Forbidden("Bạn không có quyền xem nhật ký hệ thống.");

    public static Error TooManyRows(int count, int max) =>
        Error.Validation(
            $"Có {count.ToString("#,0", Vnd.Culture)} dòng nhật ký khớp bộ lọc, vượt quá giới hạn xuất {max.ToString("#,0", Vnd.Culture)} dòng. " +
            "Vui lòng thu hẹp khoảng thời gian hoặc bộ lọc.");

    public static readonly Error NothingToExport = Error.Validation("Không có dòng nhật ký nào khớp bộ lọc để xuất.");
}
