using System.Globalization;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Auditing;

/// <summary>One entry of a filter drop-down.</summary>
public sealed record FilterOption<T>(T Value, string Name);

/// <summary>
/// The KPI-card shortcuts. Each one filters exactly the actions its card counts, so the card number and the
/// "N sự kiện khớp bộ lọc" line agree.
/// </summary>
public enum AuditQuickFilter
{
    None = 0,

    /// <summary>Failed logins + accounts locked.</summary>
    Security = 1,

    /// <summary>Voided invoices, customer returns and reprinted receipts.</summary>
    SalesExceptions = 2,

    /// <summary>Price changes, stock adjustments and stock counts.</summary>
    StockAndPrice = 3,
}

/// <summary>Filter values the screen shows in its drop-downs (search lives in the grid).</summary>
public sealed record AuditLogFilters(AuditCategory? Category, AuditSeverity? MinSeverity, string? Username, AuditQuickFilter QuickFilter)
{
    public static readonly AuditLogFilters None = new(null, null, null, AuditQuickFilter.None);

    /// <summary>Action keys of a quick filter; null for <see cref="AuditQuickFilter.None"/>.</summary>
    public static IReadOnlyCollection<string>? ActionsOf(AuditQuickFilter filter) => filter switch
    {
        AuditQuickFilter.Security => [AuditActions.LoginFailed, AuditActions.AccountLocked],
        AuditQuickFilter.SalesExceptions => [AuditActions.SaleVoided, AuditActions.SaleReturned, AuditActions.ReceiptReprinted],
        AuditQuickFilter.StockAndPrice => [AuditActions.PriceChanged, AuditActions.StockAdjusted, AuditActions.StockCounted],
        _ => null,
    };
}

/// <summary>What the grid asks for: its search box, sort and page.</summary>
public sealed record AuditGridRequest(string? Search, bool OldestFirst, int PageIndex, int PageSize);

public sealed record AuditStatCard(string Value, string Note, Tone Accent);

/// <summary>The four KPI cards (whole period, other filters ignored).</summary>
public sealed record AuditLogCards(AuditStatCard Total, AuditStatCard Security, AuditStatCard SalesExceptions, AuditStatCard StockAndPrice)
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static readonly AuditLogCards Empty = new(
        new("–", string.Empty, Tone.Info),
        new("–", string.Empty, Tone.Warning),
        new("–", string.Empty, Tone.Danger),
        new("–", string.Empty, Tone.Info));

    public static AuditLogCards From(AuditLogSummaryDto s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(
            new(N(s.Total), $"{N(s.Critical)} quan trọng · {N(s.Warning)} cần chú ý", Tone.Info),
            new(
                N(s.FailedLogins + s.AccountsLocked),
                s.AccountsLocked > 0 ? $"{N(s.AccountsLocked)} lần khóa tài khoản" : "Không có tài khoản bị khóa",
                s.AccountsLocked > 0 ? Tone.Danger : Tone.Warning),
            new(N(s.SalesExceptions), $"Hủy {N(s.SalesVoided)} · trả {N(s.Returns)} · in lại {N(s.Reprints)}", Tone.Danger),
            new(N(s.StockAndPrice), $"Đổi giá {N(s.PriceChanges)} · kho {N(s.StockAdjustments)}", Tone.Info));
    }

    public static string N(int value) => value.ToString("#,0", Vi);
}

/// <summary>The detail card for the selected entry.</summary>
public sealed record AuditLogDetail(
    string Title,
    Tone TitleTone,
    string Time,
    string User,
    string Category,
    string Severity,
    string Entity,
    IReadOnlyList<string> Lines,
    bool CanFilterUser,
    bool CanFilterEntity)
{
    public static readonly AuditLogDetail None = new("Chọn một sự kiện", Tone.Muted, "–", "–", "–", "–", "–", [], false, false);

    /// <param name="canFilterUser">False when the username is not in the user filter (e.g. "(không rõ)").</param>
    public static AuditLogDetail Of(AuditLogEntryDto entry, bool canFilterUser)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var lines = string.IsNullOrWhiteSpace(entry.Details)
            ? new[] { "(không có nội dung)" }
            : entry.Details.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new(
            entry.ActionName,
            ToneOf(entry.Severity),
            entry.Time.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            entry.UserId is { } id ? $"{entry.Username} (#{id})" : entry.Username,
            entry.CategoryName,
            entry.SeverityName,
            entry.Entity ?? "–",
            lines,
            canFilterUser,
            !string.IsNullOrEmpty(entry.EntityId));
    }

    /// <summary>Color of an entry's action/severity: red for critical, amber for warnings.</summary>
    public static Tone ToneOf(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => Tone.Danger,
        AuditSeverity.Warning => Tone.Warning,
        _ => Tone.Info,
    };
}
