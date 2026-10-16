using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Infrastructure.Persistence;

namespace GroceryPOS.Infrastructure.Auditing;

/// <summary>
/// Adds <see cref="AuditLog"/> rows to the scoped <see cref="GroceryDbContext"/>; they are written by the same
/// <see cref="IUnitOfWork.SaveChangesAsync"/> as the change they describe, so a failed save leaves no orphan entry.
/// </summary>
internal sealed class AuditService : IAuditService
{
    private const int MaxDetailsLength = 4000;

    private readonly GroceryDbContext _context;
    private readonly TimeProvider _clock;
    private readonly ICurrentUser? _currentUser;

    public AuditService(GroceryDbContext context, TimeProvider clock, ICurrentUser? currentUser = null)
    {
        _context = context;
        _clock = clock;
        _currentUser = currentUser;
    }

    public void Record(string action, string? entityName = null, string? entityId = null, string? details = null)
    {
        var session = _currentUser?.Session;
        Add(new AuditLog(_clock.GetUtcNow(), action, session?.UserId, session?.Username ?? SystemUser,
            entityName, entityId, Truncate(details)));
    }

    public void RecordFor(int? userId, string? username, string action, string? details = null) =>
        Add(new AuditLog(_clock.GetUtcNow(), action, userId, username, details: Truncate(details)));

    private const string SystemUser = "system";

    private void Add(AuditLog entry) => _context.AuditLogs.Add(entry);

    private static string? Truncate(string? details) =>
        details is { Length: > MaxDetailsLength } ? details[..(MaxDetailsLength - 1)] + "…" : details;
}
