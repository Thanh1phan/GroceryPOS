using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Shifts;

/// <summary>How the cashier enters the counted cash when closing a shift.</summary>
public enum CashCountMode
{
    /// <summary>Number of notes per denomination (kiểm đếm theo mệnh giá).</summary>
    ByDenomination = 0,

    /// <summary>One typed total.</summary>
    TypedTotal = 1,
}

/// <summary>
/// UI-free state of the close-shift count sheet (phiếu kiểm két): notes per denomination or a typed total,
/// the expected cash from the live shift report, variance, cash left for the next shift and the blocker
/// that keeps "Kết ca" disabled. Mirrors the domain rules of <c>Shift.Close</c> so the cashier sees problems
/// before submitting; <see cref="BuildRequest"/> produces the <see cref="CloseShiftRequest"/>.
/// </summary>
public sealed class CashCountSheet
{
    private readonly Dictionary<decimal, int> _notes;
    private decimal _typedTotal;
    private decimal _cashLeftInDrawer;

    public CashCountSheet(ShiftSummaryDto shift, decimal varianceNoteThreshold = 5_000)
    {
        ArgumentNullException.ThrowIfNull(shift);
        Shift = shift;
        VarianceNoteThreshold = Math.Max(1, varianceNoteThreshold);
        _notes = CashCount.Denominations.ToDictionary(d => d, _ => 0);
    }

    public CashCountSheet(ShiftSummaryDto shift, SalesOptions options)
        : this(shift, (options ?? SalesOptions.Default).ShiftVarianceNoteThreshold)
    {
    }

    /// <summary>Raised whenever a count, the mode, the cash left or the note changes.</summary>
    public event EventHandler? Changed;

    public ShiftSummaryDto Shift { get; }

    public decimal VarianceNoteThreshold { get; }

    public CashCountMode Mode { get; private set; }

    /// <summary>Denominations largest first, as printed on the sheet.</summary>
    public IReadOnlyList<decimal> Denominations => CashCount.Denominations;

    /// <summary>Cash the system expects in the drawer (float + cash sales − refunds ± pay-ins/outs).</summary>
    public decimal Expected => Shift.ExpectedCash;

    public string? Note { get; private set; }

    public int CountOf(decimal denomination) => _notes.TryGetValue(denomination, out var count) ? count : 0;

    /// <summary>Line amount of one denomination row (mệnh giá × số tờ).</summary>
    public decimal AmountOf(decimal denomination) => denomination * CountOf(denomination);

    /// <summary>Total number of notes counted.</summary>
    public int NoteCount => _notes.Values.Sum();

    /// <summary>Counted cash in the active mode.</summary>
    public decimal Counted => Mode == CashCountMode.ByDenomination ? _notes.Sum(n => n.Key * n.Value) : _typedTotal;

    /// <summary>Counted − expected: negative = thiếu, positive = thừa.</summary>
    public decimal Variance => Counted - Expected;

    public string VarianceLabel => Variance switch
    {
        < 0 => "Thiếu tiền",
        > 0 => "Thừa tiền",
        _ => "Khớp",
    };

    /// <summary>Whether the variance is large enough that a note explaining it is required.</summary>
    public bool NoteRequired => Math.Abs(Variance) >= VarianceNoteThreshold;

    public decimal CashLeftInDrawer => _cashLeftInDrawer;

    /// <summary>Cash handed to the manager/owner: counted − left in the drawer.</summary>
    public decimal CashHandedOver => Counted - _cashLeftInDrawer;

    /// <summary>Whether anything has been entered yet (an untouched sheet must not close at 0 ₫ by accident).</summary>
    public bool HasCount => Mode == CashCountMode.ByDenomination ? NoteCount > 0 : _typedTotal > 0;

    /// <summary>Why the shift cannot be closed yet, or null when it can.</summary>
    public string? Blocker
    {
        get
        {
            if (!HasCount && Expected > 0)
            {
                return "Vui lòng đếm tiền mặt trong két.";
            }

            if (_cashLeftInDrawer > Counted)
            {
                return "Tiền để lại két không được lớn hơn tiền mặt thực đếm.";
            }

            if (NoteRequired && Note is null)
            {
                return $"{VarianceLabel} {Vnd.Format(Math.Abs(Variance))} – vui lòng ghi chú lý do.";
            }

            return null;
        }
    }

    public bool CanClose => Blocker is null;

    public void SetMode(CashCountMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        // Carry the denomination total over so switching to "typed" does not lose the count.
        if (mode == CashCountMode.TypedTotal && _typedTotal == 0)
        {
            _typedTotal = _notes.Sum(n => n.Key * n.Value);
        }

        Mode = mode;
        OnChanged();
    }

    /// <summary>Sets the number of notes of a denomination (negative counts become 0).</summary>
    public void SetCount(decimal denomination, int count)
    {
        if (!_notes.ContainsKey(denomination))
        {
            throw new ArgumentOutOfRangeException(nameof(denomination), denomination, "Mệnh giá không hợp lệ.");
        }

        _notes[denomination] = Math.Max(0, count);
        OnChanged();
    }

    public void SetTypedTotal(decimal amount)
    {
        _typedTotal = Math.Max(0, Vnd.Round(amount));
        OnChanged();
    }

    public void SetCashLeftInDrawer(decimal amount)
    {
        _cashLeftInDrawer = Math.Max(0, Vnd.Round(amount));
        OnChanged();
    }

    /// <summary>Leaves the whole counted amount in the drawer for the next shift.</summary>
    public void LeaveAll() => SetCashLeftInDrawer(Counted);

    public void SetNote(string? note)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        OnChanged();
    }

    /// <summary>Resets every count to zero (keeps the mode, cash left and note).</summary>
    public void Clear()
    {
        foreach (var denomination in _notes.Keys.ToList())
        {
            _notes[denomination] = 0;
        }

        _typedTotal = 0;
        OnChanged();
    }

    /// <summary>The close request for <see cref="IShiftService.CloseAsync"/>, or the blocker as a validation error.</summary>
    public Result<CloseShiftRequest> BuildRequest()
    {
        if (Blocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        return Mode == CashCountMode.ByDenomination
            ? new CloseShiftRequest(
                Shift.ShiftId,
                Notes: _notes.Where(n => n.Value > 0).ToDictionary(n => n.Key, n => n.Value),
                CashLeftInDrawer: _cashLeftInDrawer,
                Note: Note)
            : new CloseShiftRequest(Shift.ShiftId, CountedCash: _typedTotal, CashLeftInDrawer: _cashLeftInDrawer, Note: Note);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
