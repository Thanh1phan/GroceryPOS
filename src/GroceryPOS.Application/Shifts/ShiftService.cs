using System.Globalization;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Shifts;

public interface IShiftService
{
    /// <summary>The signed-in user's open shift with a live report, plus the suggested float for opening one.</summary>
    Task<Result<CurrentShiftDto>> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a shift for the signed-in user (one open shift per user).</summary>
    Task<Result<ShiftSummaryDto>> OpenAsync(OpenShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pay-in / pay-out on the signed-in user's open shift. Pay-outs cannot exceed the cash in the drawer.</summary>
    Task<Result<ShiftSummaryDto>> RecordCashMovementAsync(CashMovementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Shift report (live while open). Other users' shifts need <c>shifts.supervise</c>.</summary>
    Task<Result<ShiftSummaryDto>> GetSummaryAsync(int shiftId, CancellationToken cancellationToken = default);

    /// <summary>Counts the drawer and closes the shift. Closing another user's shift needs <c>shifts.supervise</c>.</summary>
    Task<Result<ShiftSummaryDto>> CloseAsync(CloseShiftRequest request, CancellationToken cancellationToken = default);

    /// <summary>Shift history; without <c>shifts.supervise</c> only the user's own shifts are listed.</summary>
    Task<Result<IReadOnlyList<ShiftListItemDto>>> ListAsync(ShiftFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cashier shifts and cash drawer (ca bán hàng, két tiền). Counter totals are computed from what the
/// shift's user recorded between opening and closing: completed sales, voids and customer returns.
/// </summary>
public sealed class ShiftService : IShiftService
{
    private readonly IShiftRepository _shifts;
    private readonly ISaleRepository _sales;
    private readonly ISaleReturnRepository _returns;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly SalesOptions _options;

    public ShiftService(
        IShiftRepository shifts,
        ISaleRepository sales,
        ISaleReturnRepository returns,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IAuditService audit,
        TimeProvider clock,
        SalesOptions? options = null)
    {
        _shifts = shifts;
        _sales = sales;
        _returns = returns;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _options = options ?? SalesOptions.Default;
    }

    public async Task<Result<CurrentShiftDto>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        var session = _currentUser.Session!;
        var shift = await _shifts.GetOpenByUserAsync(session.UserId, cancellationToken);
        var summary = shift is null ? null : await SummarizeAsync(shift, cancellationToken);
        var suggested = (await _shifts.GetLastClosedAsync(cancellationToken))?.CashLeftInDrawer ?? 0;
        return new CurrentShiftDto(summary, suggested);
    }

    public async Task<Result<ShiftSummaryDto>> OpenAsync(OpenShiftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } error)
        {
            return error;
        }

        var session = _currentUser.Session!;
        if (await _shifts.GetOpenByUserAsync(session.UserId, cancellationToken) is { } open)
        {
            return ShiftErrors.AlreadyOpen(open.Code);
        }

        var now = _clock.GetUtcNow();
        Shift shift;
        try
        {
            shift = new Shift(await NextCodeAsync(now, cancellationToken), session.UserId, now, request.OpeningFloat, request.Note);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _shifts.Add(shift);
        _audit.Record(AuditActions.ShiftOpened, nameof(Shift), shift.Code, $"Tiền đầu ca {Vnd.Format(shift.OpeningFloat)}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummarizeAsync(shift, cancellationToken);
    }

    public async Task<Result<ShiftSummaryDto>> RecordCashMovementAsync(CashMovementRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } error)
        {
            return error;
        }

        var session = _currentUser.Session!;
        var shift = await _shifts.GetOpenByUserAsync(session.UserId, cancellationToken);
        if (shift is null)
        {
            return ShiftErrors.NoOpenShift;
        }

        var now = _clock.GetUtcNow();
        if (request.Type == CashMovementType.PayOut)
        {
            var available = shift.ExpectedCashWith(await ComputeTotalsAsync(shift, now, cancellationToken));
            if (request.Amount > available)
            {
                return ShiftErrors.InsufficientCash(Math.Max(0, available));
            }
        }

        CashMovement movement;
        try
        {
            movement = shift.AddCashMovement(request.Type, request.Amount, request.Reason, now, session.UserId);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(
            AuditActions.CashMoved,
            nameof(Shift),
            shift.Code,
            $"{ShiftLabels.Of(movement.Type)} {Vnd.Format(movement.Amount)} – Lý do: {movement.Reason}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummarizeAsync(shift, cancellationToken);
    }

    public async Task<Result<ShiftSummaryDto>> GetSummaryAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        var shift = await _shifts.GetByIdAsync(shiftId, cancellationToken);
        if (shift is null)
        {
            return ShiftErrors.ShiftNotFound;
        }

        if (!CanAccess(shift))
        {
            return ShiftErrors.NotYourShift;
        }

        return await SummarizeAsync(shift, cancellationToken);
    }

    public async Task<Result<ShiftSummaryDto>> CloseAsync(CloseShiftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Authorize() is { } error)
        {
            return error;
        }

        var session = _currentUser.Session!;
        var shift = await _shifts.GetByIdAsync(request.ShiftId, cancellationToken);
        if (shift is null)
        {
            return ShiftErrors.ShiftNotFound;
        }

        if (!CanAccess(shift))
        {
            return ShiftErrors.NotYourShift;
        }

        if (!shift.IsOpen)
        {
            return ShiftErrors.AlreadyClosed;
        }

        decimal counted;
        try
        {
            switch (request)
            {
                case { Notes: { } notes, CountedCash: { } typed }:
                    counted = CashCount.Total(notes);
                    if (counted != typed)
                    {
                        return ShiftErrors.CountMismatch(typed, counted);
                    }

                    break;
                case { Notes: { } notes }:
                    counted = CashCount.Total(notes);
                    break;
                case { CountedCash: { } typed }:
                    counted = typed;
                    break;
                default:
                    return ShiftErrors.CountMissing;
            }

            var now = _clock.GetUtcNow();
            var totals = await ComputeTotalsAsync(shift, now, cancellationToken);
            shift.Close(totals, counted, request.CashLeftInDrawer, request.Note, now, session.UserId, _options.ShiftVarianceNoteThreshold);
        }
        catch (DomainException ex)
        {
            return SaleErrors.From(ex);
        }

        _audit.Record(AuditActions.ShiftClosed, nameof(Shift), shift.Code, DescribeClose(shift, session.UserId));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummarizeAsync(shift, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ShiftListItemDto>>> ListAsync(ShiftFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (Authorize() is { } error)
        {
            return error;
        }

        if (!_currentUser.HasPermission(Permissions.ShiftsSupervise))
        {
            filter = filter with { UserId = _currentUser.Session!.UserId };
        }

        var shifts = await _shifts.SearchAsync(filter, cancellationToken);
        var names = await UserNamesAsync(cancellationToken);
        return shifts
            .Select(s => new ShiftListItemDto(
                s.Id,
                s.Code,
                s.UserId,
                names.GetValueOrDefault(s.UserId),
                s.Status,
                ShiftLabels.Of(s.Status),
                s.OpenedAt,
                s.ClosedAt,
                s.Totals?.NetRevenue,
                s.Variance))
            .ToList();
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.ShiftsManage) || _currentUser.HasPermission(Permissions.ShiftsSupervise)
            ? null
            : Error.Forbidden("Bạn không có quyền quản lý ca bán hàng.");
    }

    private bool CanAccess(Shift shift) =>
        shift.UserId == _currentUser.Session!.UserId || _currentUser.HasPermission(Permissions.ShiftsSupervise);

    /// <summary>Counter activity of the shift's user from opening until <paramref name="to"/> (inclusive).</summary>
    private async Task<ShiftTotals> ComputeTotalsAsync(Shift shift, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var sales = await _sales.ListCompletedByCashierAsync(shift.UserId, shift.OpenedAt, to, cancellationToken);
        var voids = await _sales.ListVoidedByAsync(shift.UserId, shift.OpenedAt, to, cancellationToken);
        var returns = await _returns.ListByUserAsync(shift.UserId, shift.OpenedAt, to, cancellationToken);

        return new ShiftTotals(
            SalesCount: sales.Count,
            SalesTotal: sales.Sum(s => s.Total),
            DiscountTotal: sales.Sum(s => s.TotalDiscount),
            CashSales: sales.Sum(DrawerCash),
            TransferSales: sales.Sum(s => PaidBy(s, PaymentMethod.BankTransfer)),
            CardSales: sales.Sum(s => PaidBy(s, PaymentMethod.Card)),
            VoidCount: voids.Count,
            VoidTotal: voids.Sum(s => s.Total),
            VoidCash: voids.Sum(DrawerCash),
            ReturnCount: returns.Count,
            CashRefunds: returns.Where(r => r.RefundMethod == PaymentMethod.Cash).Sum(r => r.RefundTotal),
            TransferRefunds: returns.Where(r => r.RefundMethod != PaymentMethod.Cash).Sum(r => r.RefundTotal));
    }

    /// <summary>Cash a sale left in the drawer: cash received minus change (change is always given in cash).</summary>
    private static decimal DrawerCash(Sale sale) => sale.CashPaid - sale.ChangeDue;

    private static decimal PaidBy(Sale sale, PaymentMethod method) =>
        sale.Payments.Where(p => p.Method == method).Sum(p => p.Amount);

    private async Task<ShiftSummaryDto> SummarizeAsync(Shift shift, CancellationToken cancellationToken)
    {
        var totals = shift.Totals ?? await ComputeTotalsAsync(shift, _clock.GetUtcNow(), cancellationToken);
        var userName = (await _users.GetByIdAsync(shift.UserId, cancellationToken))?.FullName;
        return new ShiftSummaryDto(
            shift.Id,
            shift.Code,
            shift.UserId,
            userName,
            shift.Status,
            ShiftLabels.Of(shift.Status),
            shift.OpenedAt,
            shift.ClosedAt,
            shift.OpeningFloat,
            totals,
            shift.PayInTotal,
            shift.PayOutTotal,
            shift.ExpectedCash ?? shift.ExpectedCashWith(totals),
            shift.CountedCash,
            shift.Variance,
            shift.CashLeftInDrawer,
            shift.CashHandedOver,
            shift.OpeningNote,
            shift.CloseNote,
            shift.CashMovements
                .OrderBy(m => m.Timestamp)
                .Select(m => new CashMovementDto(m.Id, m.Type, ShiftLabels.Of(m.Type), m.Amount, m.Reason, m.Timestamp))
                .ToList());
    }

    private async Task<Dictionary<int, string>> UserNamesAsync(CancellationToken cancellationToken) =>
        (await _users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.FullName);

    private async Task<string> NextCodeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var prefix = DailyCode.PrefixFor(_options.ShiftPrefix, now.ToOffset(_options.StoreUtcOffset));
        var last = await _shifts.GetLastCodeWithPrefixAsync(prefix, cancellationToken);
        return DailyCode.Next(prefix, last);
    }

    private static string DescribeClose(Shift shift, int closedBy)
    {
        var totals = shift.Totals!;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{totals.SalesCount} HĐ, doanh thu thuần {Vnd.Format(totals.NetRevenue)}; tiền mặt hệ thống {Vnd.Format(shift.ExpectedCash!.Value)}, " +
            $"thực đếm {Vnd.Format(shift.CountedCash!.Value)}, chênh lệch {Vnd.Format(shift.Variance!.Value)}, " +
            $"để lại két {Vnd.Format(shift.CashLeftInDrawer!.Value)}");
        if (closedBy != shift.UserId)
        {
            text += " (kết ca hộ)";
        }

        return shift.CloseNote is null ? text : $"{text} – Ghi chú: {shift.CloseNote}";
    }
}
