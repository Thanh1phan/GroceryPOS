using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Abstractions;

public sealed record ShiftFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? UserId = null,
    ShiftStatus? Status = null);

public interface IShiftRepository : IRepository<Shift>
{
    /// <summary>The user's open shift (with cash movements), if any. A user has at most one open shift.</summary>
    Task<Shift?> GetOpenByUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Most recently closed shift of any user (its cash left in the drawer is the next float).</summary>
    Task<Shift?> GetLastClosedAsync(CancellationToken cancellationToken = default);

    /// <summary>Highest shift code starting with <paramref name="prefix"/>, or null if none exists yet.</summary>
    Task<string?> GetLastCodeWithPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Shifts (with cash movements) opened in [From, To), newest first, optionally for one user/status.</summary>
    Task<IReadOnlyList<Shift>> SearchAsync(ShiftFilter filter, CancellationToken cancellationToken = default);
}
