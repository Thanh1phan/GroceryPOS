using GroceryPOS.Domain.Auditing;

namespace GroceryPOS.Application.Abstractions;

/// <summary>
/// Filter for the audit trail. <paramref name="From"/> is inclusive, <paramref name="To"/> exclusive.
/// <paramref name="Actions"/> null = every action; an empty list matches nothing. <paramref name="Search"/> is a
/// case-insensitive (ASCII) substring of the username, object id or details.
/// </summary>
public sealed record AuditLogFilter(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<string>? Actions = null,
    string? Username = null,
    string? Search = null,
    bool OldestFirst = false,
    int Skip = 0,
    int Take = 50);

public sealed record AuditLogSearchResult(IReadOnlyList<AuditLog> Items, int TotalCount);

/// <summary>Read side of the audit log (rows are written through <see cref="IAuditService"/> and never changed).</summary>
public interface IAuditLogRepository
{
    /// <summary>One page of matching entries (newest first unless <see cref="AuditLogFilter.OldestFirst"/>) and the total match count.</summary>
    Task<AuditLogSearchResult> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Number of entries per action key in [<paramref name="from"/>, <paramref name="to"/>).</summary>
    Task<IReadOnlyDictionary<string, int>> CountByActionAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Every distinct username that appears in the log (incl. failed logins of unknown names), sorted.</summary>
    Task<IReadOnlyList<string>> ListUsernamesAsync(CancellationToken cancellationToken = default);
}
