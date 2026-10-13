using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Shifts;

/// <summary>Opening a shift: the float counted into the drawer.</summary>
public sealed record OpenShiftRequest(decimal OpeningFloat, string? Note = null);

/// <summary>Pay-in / pay-out on the current user's open shift.</summary>
public sealed record CashMovementRequest(CashMovementType Type, decimal Amount, string Reason);

/// <summary>
/// Closing a shift. The counted cash is either typed as one amount (<see cref="CountedCash"/>)
/// or counted by denomination (<see cref="Notes"/>: denomination → number of notes); when both are
/// given they must agree.
/// </summary>
public sealed record CloseShiftRequest(
    int ShiftId,
    decimal? CountedCash = null,
    IReadOnlyDictionary<decimal, int>? Notes = null,
    decimal CashLeftInDrawer = 0,
    string? Note = null);

public sealed record CashMovementDto(
    int Id,
    CashMovementType Type,
    string TypeName,
    decimal Amount,
    string Reason,
    DateTimeOffset Timestamp);

/// <summary>
/// Shift report: live (báo cáo giữa ca) while open, frozen (báo cáo kết ca) once closed.
/// <see cref="CountedCash"/>, <see cref="Variance"/> and <see cref="CashHandedOver"/> are null while open.
/// </summary>
public sealed record ShiftSummaryDto(
    int ShiftId,
    string Code,
    int UserId,
    string? UserName,
    ShiftStatus Status,
    string StatusName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningFloat,
    ShiftTotals Totals,
    decimal PayInTotal,
    decimal PayOutTotal,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? Variance,
    decimal? CashLeftInDrawer,
    decimal? CashHandedOver,
    string? OpeningNote,
    string? CloseNote,
    IReadOnlyList<CashMovementDto> CashMovements)
{
    /// <summary>Thiếu / Thừa / Khớp – for the variance badge.</summary>
    public string? VarianceLabel => Variance switch
    {
        null => null,
        < 0 => "Thiếu tiền",
        > 0 => "Thừa tiền",
        _ => "Khớp",
    };
}

/// <summary>State of the current user's drawer when the POS screen opens.</summary>
/// <param name="Current">Open shift report, or null when the user must open a shift first.</param>
/// <param name="SuggestedOpeningFloat">Cash the last closed shift left in the drawer (pre-fills the open dialog).</param>
public sealed record CurrentShiftDto(ShiftSummaryDto? Current, decimal SuggestedOpeningFloat);

/// <summary>Row in the shift history list.</summary>
public sealed record ShiftListItemDto(
    int ShiftId,
    string Code,
    int UserId,
    string? UserName,
    ShiftStatus Status,
    string StatusName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal? NetRevenue,
    decimal? Variance);
