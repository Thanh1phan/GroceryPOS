using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Sales;

/// <summary>Park / list / resume / discard carts at the counter (lưu tạm đơn).</summary>
public interface IParkedOrderService
{
    /// <summary>Saves the cart under <paramref name="label"/> (or a default one) and clears it for the next customer.</summary>
    Task<Result<ParkedOrder>> ParkAsync(PosCart cart, string? label = null, CancellationToken cancellationToken = default);

    /// <summary>Parked orders, oldest first (the customer who has waited longest is on top).</summary>
    Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a parked order into the (empty) cart with today's prices and the member's current points, then removes
    /// it from the parked list. Products no longer sold are dropped; every difference is reported in the notices.
    /// </summary>
    Task<Result<ParkedOrderResume>> ResumeAsync(Guid id, PosCart cart, CancellationToken cancellationToken = default);

    /// <summary>Deletes a parked order the customer will not come back for.</summary>
    Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class ParkedOrderService : IParkedOrderService
{
    private readonly IParkedOrderStore _store;
    private readonly IProductRepository _products;
    private readonly ICustomerRepository _customers;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public ParkedOrderService(
        IParkedOrderStore store,
        IProductRepository products,
        ICustomerRepository customers,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _store = store;
        _products = products;
        _customers = customers;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ParkedOrder>> ParkAsync(PosCart cart, string? label = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (cart.IsEmpty)
        {
            return Error.Validation("Giỏ hàng đang trống – không có gì để lưu tạm.");
        }

        var existing = await _store.ListAsync(cancellationToken);
        if (existing.Count >= ParkedOrderRules.MaxParked)
        {
            return Error.Conflict(
                $"Đã có {existing.Count} đơn lưu tạm (tối đa {ParkedOrderRules.MaxParked}). Vui lòng gọi lại hoặc xóa bớt đơn cũ.");
        }

        var session = _currentUser.Session!;
        var order = new ParkedOrder(
            Guid.NewGuid(),
            ParkedOrderRules.LabelFor(label, cart.Customer?.Name, existing.Select(o => o.Label)),
            _clock.GetUtcNow(),
            session.UserId,
            session.FullName,
            cart.Lines.Select(l => new ParkedLine(l.Product.Id, l.Code, l.Name, l.Unit, l.UnitPrice, l.Quantity, l.Discount)).ToList(),
            cart.Customer?.Id,
            cart.Customer?.Name,
            cart.Customer?.Phone,
            cart.InvoiceDiscount,
            cart.Note,
            // Without points: they are redeemed again on resume.
            Math.Max(0, cart.Subtotal - cart.InvoiceDiscountAmount));

        await _store.SaveAsync(order, cancellationToken);
        cart.Clear();
        return order;
    }

    public Task<IReadOnlyList<ParkedOrder>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    public async Task<Result<ParkedOrderResume>> ResumeAsync(Guid id, PosCart cart, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cart);
        if (Authorize() is { } denied)
        {
            return denied;
        }

        if (!cart.IsEmpty)
        {
            return Error.Conflict("Đang có đơn trên quầy. Vui lòng thanh toán, lưu tạm hoặc hủy đơn đó trước khi gọi lại đơn khác.");
        }

        var order = await _store.GetAsync(id, cancellationToken);
        if (order is null)
        {
            return Error.NotFound("Đơn lưu tạm không còn (có thể đã được gọi lại hoặc xóa).");
        }

        var ids = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await _products.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id);

        var notices = new List<string>();
        var lines = new List<(PosProductDto, decimal, DiscountInput?)>();
        var reportedPrice = new HashSet<int>();
        foreach (var line in order.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || !product.IsActive)
            {
                notices.Add(ParkedOrderRules.ProductGone(line));
                continue;
            }

            if (product.SellPrice != line.UnitPrice && reportedPrice.Add(product.Id))
            {
                notices.Add(ParkedOrderRules.PriceChanged(line, product.SellPrice));
            }

            lines.Add((PosLookupService.ToDto(product), line.Quantity, line.Discount));
        }

        PosCustomer? customer = null;
        if (order.CustomerId is { } customerId)
        {
            var found = await _customers.GetByIdAsync(customerId, cancellationToken);
            if (found is { IsActive: true })
            {
                customer = PosCustomer.From(found);
            }
            else
            {
                notices.Add($"Khách hàng \"{order.CustomerName}\" không còn hoạt động nên đã được bỏ chọn.");
            }
        }

        if (lines.Count == 0)
        {
            await _store.RemoveAsync(order.Id, cancellationToken);
            return Error.Validation(
                $"Không còn sản phẩm nào của \"{order.Label}\" bán được – đơn lưu tạm đã được xóa. " + string.Join(" ", notices));
        }

        if (!_currentUser.HasPermission(Permissions.SalesDiscount)
            && (order.InvoiceDiscount is { IsNone: false } || lines.Any(l => l.Item3 is { IsNone: false })))
        {
            // Parked by someone allowed to discount; the cashier resuming it is not – checkout would refuse anyway.
            lines = lines.Select(l => (l.Item1, l.Item2, (DiscountInput?)null)).ToList();
            order = order with { InvoiceDiscount = null };
            notices.Add("Bạn không có quyền giảm giá nên các khoản giảm giá của đơn đã được bỏ.");
        }

        cart.Load(lines, customer, order.InvoiceDiscount, order.Note);
        if (!await _store.RemoveAsync(order.Id, cancellationToken))
        {
            cart.Clear();
            return Error.NotFound("Đơn lưu tạm vừa được gọi lại ở nơi khác.");
        }

        return new ParkedOrderResume(order, lines.Count, notices);
    }

    public async Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } denied)
        {
            return denied;
        }

        return await _store.RemoveAsync(id, cancellationToken)
            ? Result.Success()
            : Error.NotFound("Đơn lưu tạm không còn (có thể đã được gọi lại hoặc xóa).");
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SalesCreate) ? null : Error.Forbidden();
    }
}
