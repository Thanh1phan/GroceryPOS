using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GroceryPOS.Infrastructure.Persistence;

/// <summary>
/// Stamps <see cref="AuditableEntity"/> rows with who/when on insert and update. Uses the signed-in user's
/// username, or <c>system</c> when nobody is signed in (seeding, migrations, background jobs).
/// </summary>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public const string SystemUser = "system";

    private readonly ICurrentUser? _currentUser;
    private readonly TimeProvider _clock;

    public AuditableEntityInterceptor(TimeProvider clock, ICurrentUser? currentUser = null)
    {
        _clock = clock;
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var user = _currentUser?.Session?.Username ?? SystemUser;
        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = user;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = user;
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;
            }
        }
    }
}
