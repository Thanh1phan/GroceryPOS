using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Catalog;

public interface IPriceHistoryService
{
    /// <summary>
    /// Price changes of one product, newest first. Requires <c>products.view</c> (changing a price requires
    /// <c>prices.change</c>, checked separately at write time by <see cref="IProductService"/>).
    /// </summary>
    Task<Result<IReadOnlyList<PriceHistoryEntry>>> ListByProductAsync(
        int productId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
}

public sealed class PriceHistoryService : IPriceHistoryService
{
    private readonly IProductRepository _products;
    private readonly IPriceHistoryRepository _history;
    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;

    public PriceHistoryService(IProductRepository products, IPriceHistoryRepository history, IUserRepository users, ICurrentUser currentUser)
    {
        _products = products;
        _history = history;
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PriceHistoryEntry>>> ListByProductAsync(
        int productId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        if (Authorize(Permissions.ProductsView) is { } denied)
        {
            return denied;
        }

        if (await _products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return ProductErrors.NotFound(productId);
        }

        var entries = await _history.ListByProductAsync(productId, from, to, cancellationToken);
        var userIds = entries.Where(e => e.ChangedByUserId.HasValue).Select(e => e.ChangedByUserId!.Value).Distinct().ToHashSet();
        var usernamesById = userIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _users.ListAsync(cancellationToken)).Where(u => userIds.Contains(u.Id)).ToDictionary(u => u.Id, u => u.Username);

        IReadOnlyList<PriceHistoryEntry> items = entries
            .Select(e => new PriceHistoryEntry(
                e.Id,
                e.ChangedAt,
                e.OldCostPrice,
                e.NewCostPrice,
                e.OldSellPrice,
                e.NewSellPrice,
                e.ChangedByUserId.HasValue && usernamesById.TryGetValue(e.ChangedByUserId.Value, out var username) ? username : null,
                e.Source.DisplayName(),
                e.Note))
            .ToList();
        return Result.Success(items);
    }

    private Error? Authorize(string permission, string? deniedMessage = null)
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(permission)
            ? null
            : deniedMessage is null ? Error.Forbidden() : Error.Forbidden(deniedMessage);
    }
}
