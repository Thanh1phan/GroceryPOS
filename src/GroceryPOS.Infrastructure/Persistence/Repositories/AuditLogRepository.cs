using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read-only queries over <c>AuditLogs</c> (untracked). Text search uses SQL <c>LIKE</c> so it runs in the database:
/// case-insensitive for ASCII on SQLite and with the column collation on SQL Server; wildcards typed by the user are escaped.
/// </summary>
internal sealed class AuditLogRepository : IAuditLogRepository
{
    private const string Escape = "\\";
    private readonly GroceryDbContext _context;

    public AuditLogRepository(GroceryDbContext context)
    {
        _context = context;
    }

    private IQueryable<AuditLog> Logs => _context.AuditLogs.AsNoTracking();

    public async Task<AuditLogSearchResult> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Logs.Where(a => a.Timestamp >= filter.From && a.Timestamp < filter.To);

        if (filter.Actions is { } actions)
        {
            if (actions.Count == 0)
            {
                return new AuditLogSearchResult([], 0);
            }

            var keys = actions.ToList();
            query = query.Where(a => keys.Contains(a.Action));
        }

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            var username = filter.Username.Trim();
            query = query.Where(a => a.Username == username);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(a =>
                EF.Functions.Like(a.Username!, pattern, Escape) ||
                EF.Functions.Like(a.EntityId!, pattern, Escape) ||
                EF.Functions.Like(a.Details!, pattern, Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0 || filter.Take <= 0)
        {
            return new AuditLogSearchResult([], total);
        }

        var ordered = filter.OldestFirst
            ? query.OrderBy(a => a.Timestamp).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);
        var items = await ordered.Skip(Math.Max(0, filter.Skip)).Take(filter.Take).ToListAsync(cancellationToken);
        return new AuditLogSearchResult(items, total);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByActionAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var counts = await Logs
            .Where(a => a.Timestamp >= from && a.Timestamp < to)
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => c.Action, c => c.Count, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> ListUsernamesAsync(CancellationToken cancellationToken = default)
    {
        var names = await Logs
            .Where(a => a.Username != null && a.Username != "")
            .Select(a => a.Username!)
            .Distinct()
            .ToListAsync(cancellationToken);
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Makes <c>%</c>, <c>_</c> and the escape character match literally.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace(Escape, Escape + Escape).Replace("%", Escape + "%").Replace("_", Escape + "_");
}
