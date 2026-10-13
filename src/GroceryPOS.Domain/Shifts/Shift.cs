using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Domain.Shifts;

/// <summary>
/// A cashier shift (ca bán hàng) and its cash drawer. Opened with a counted float, it collects
/// manual pay-ins/pay-outs while open; at closing the drawer is counted and compared with the
/// expected cash: <c>float + cash sales − void cash − cash refunds + pay-ins − pay-outs</c>.
/// Part of the counted cash may be left in the drawer as the next shift's float; the rest is handed over.
/// </summary>
public sealed class Shift : AuditableEntity
{
    private readonly List<CashMovement> _cashMovements = new();

    private Shift()
    {
    }

    public Shift(string code, int userId, DateTimeOffset openedAt, decimal openingFloat, string? note = null)
    {
        Code = Guard.NotEmpty(code, "shift.code", "Mã ca không được để trống", 32).ToUpperInvariant();
        if (userId <= 0)
        {
            throw new DomainException("shift.user", "Thiếu thông tin nhân viên mở ca.");
        }

        if (openingFloat < 0 || openingFloat != Money.Round(openingFloat))
        {
            throw new DomainException("shift.float", "Tiền đầu ca phải là số tiền hợp lệ, không âm.");
        }

        UserId = userId;
        OpenedAt = openedAt;
        OpeningFloat = openingFloat;
        OpeningNote = Guard.Optional(note, 500);
        Status = ShiftStatus.Open;
    }

    public string Code { get; private set; } = string.Empty;
    public int UserId { get; private set; }
    public ShiftStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>Tiền đầu ca – cash counted into the drawer at opening.</summary>
    public decimal OpeningFloat { get; private set; }

    public string? OpeningNote { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>User who closed the shift (the cashier, or a manager force-closing it).</summary>
    public int? ClosedBy { get; private set; }

    /// <summary>Counter activity frozen at closing (null while open).</summary>
    public ShiftTotals? Totals { get; private set; }

    /// <summary>Tiền mặt theo hệ thống at closing.</summary>
    public decimal? ExpectedCash { get; private set; }

    /// <summary>Tiền mặt thực đếm at closing.</summary>
    public decimal? CountedCash { get; private set; }

    /// <summary>Tiền để lại két – float for the next shift.</summary>
    public decimal? CashLeftInDrawer { get; private set; }

    public string? CloseNote { get; private set; }

    public IReadOnlyList<CashMovement> CashMovements => _cashMovements;

    public bool IsOpen => Status == ShiftStatus.Open;

    public decimal PayInTotal => _cashMovements.Where(m => m.Type == CashMovementType.PayIn).Sum(m => m.Amount);

    public decimal PayOutTotal => _cashMovements.Where(m => m.Type == CashMovementType.PayOut).Sum(m => m.Amount);

    /// <summary>Chênh lệch: counted − expected (negative = thiếu tiền, positive = thừa tiền).</summary>
    public decimal? Variance => CountedCash - ExpectedCash;

    /// <summary>Tiền nộp về: counted cash taken out of the drawer at closing.</summary>
    public decimal? CashHandedOver => CountedCash - CashLeftInDrawer;

    /// <summary>Cash that should be in the drawer given the counter activity so far.</summary>
    public decimal ExpectedCashWith(ShiftTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return OpeningFloat + totals.NetCash + PayInTotal - PayOutTotal;
    }

    /// <summary>Records cash put into or taken out of the drawer. A reason is always required.</summary>
    public CashMovement AddCashMovement(CashMovementType type, decimal amount, string reason, DateTimeOffset at, int userId)
    {
        EnsureOpen();
        var movement = new CashMovement(type, amount, reason, at, userId);
        _cashMovements.Add(movement);
        return movement;
    }

    /// <summary>
    /// Closes the shift: freezes <paramref name="totals"/>, computes expected cash and variance.
    /// A variance of at least <paramref name="varianceNoteThreshold"/> (absolute) requires an explanation.
    /// </summary>
    public void Close(
        ShiftTotals totals,
        decimal countedCash,
        decimal cashLeftInDrawer,
        string? note,
        DateTimeOffset at,
        int userId,
        decimal varianceNoteThreshold)
    {
        ArgumentNullException.ThrowIfNull(totals);
        EnsureOpen();
        if (userId <= 0)
        {
            throw new DomainException("shift.user", "Thiếu thông tin người kết ca.");
        }

        if (at < OpenedAt)
        {
            throw new DomainException("shift.time", "Thời điểm kết ca không được trước thời điểm mở ca.");
        }

        if (countedCash < 0 || countedCash != Money.Round(countedCash))
        {
            throw new DomainException("shift.counted", "Tiền mặt thực đếm phải là số tiền hợp lệ, không âm.");
        }

        if (cashLeftInDrawer < 0 || cashLeftInDrawer != Money.Round(cashLeftInDrawer))
        {
            throw new DomainException("shift.left", "Tiền để lại két phải là số tiền hợp lệ, không âm.");
        }

        if (cashLeftInDrawer > countedCash)
        {
            throw new DomainException("shift.left", "Tiền để lại két không được lớn hơn tiền mặt thực đếm.");
        }

        var expected = ExpectedCashWith(totals);
        var trimmedNote = Guard.Optional(note, 500);
        if (Math.Abs(countedCash - expected) >= Math.Max(1, varianceNoteThreshold) && trimmedNote is null)
        {
            throw new DomainException("shift.variance_note", "Tiền mặt bị chênh lệch, vui lòng ghi chú lý do.");
        }

        Totals = totals;
        ExpectedCash = expected;
        CountedCash = countedCash;
        CashLeftInDrawer = cashLeftInDrawer;
        CloseNote = trimmedNote;
        ClosedAt = at;
        ClosedBy = userId;
        Status = ShiftStatus.Closed;
    }

    private void EnsureOpen()
    {
        if (Status != ShiftStatus.Open)
        {
            throw new DomainException("shift.closed", "Ca đã kết thúc.");
        }
    }
}

/// <summary>Manual drawer movement (phiếu thu/chi tiền mặt trong ca).</summary>
public sealed class CashMovement : Entity
{
    private CashMovement()
    {
    }

    internal CashMovement(CashMovementType type, decimal amount, string reason, DateTimeOffset at, int userId)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainException("cash.type", "Loại thu/chi không hợp lệ.");
        }

        if (amount <= 0 || amount != Money.Round(amount))
        {
            throw new DomainException("cash.amount", "Số tiền phải lớn hơn 0.");
        }

        Type = type;
        Amount = amount;
        Reason = Guard.NotEmpty(reason, "cash.reason", "Vui lòng nhập lý do thu/chi", 200);
        Timestamp = at;
        UserId = userId;
    }

    public int ShiftId { get; private set; }
    public CashMovementType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset Timestamp { get; private set; }
    public int UserId { get; private set; }

    /// <summary>Signed effect on the drawer.</summary>
    public decimal SignedAmount => Type == CashMovementType.PayIn ? Amount : -Amount;
}
