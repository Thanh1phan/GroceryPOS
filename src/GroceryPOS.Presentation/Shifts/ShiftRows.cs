using System.Globalization;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Shifts;

/// <summary>Filters of the shift history: store-local dates (both inclusive) and "Chỉ ca đang mở".</summary>
public sealed record ShiftHistoryFilter(DateTime? From, DateTime? To, bool OnlyOpen = false)
{
    /// <summary>The default period: the last seven days including today.</summary>
    public static ShiftHistoryFilter LastSevenDays(DateTime today) => new(today.Date.AddDays(-6), today.Date);
}

/// <summary>A row of the shift history, with times already in store time.</summary>
public sealed record ShiftRow(
    int ShiftId,
    string Code,
    int UserId,
    string UserName,
    ShiftStatus Status,
    string StatusName,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal? NetRevenue,
    decimal? Variance)
{
    public bool IsOpen => Status == ShiftStatus.Open;

    public static ShiftRow From(ShiftListItemDto s, TimeSpan storeOffset)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ShiftRow(
            s.ShiftId,
            s.Code,
            s.UserId,
            s.UserName ?? $"#{s.UserId}",
            s.Status,
            s.StatusName,
            s.OpenedAt.ToOffset(storeOffset).DateTime,
            s.ClosedAt?.ToOffset(storeOffset).DateTime,
            s.NetRevenue,
            s.Variance);
    }

    /// <summary>"Trạng thái" cell: open shifts stand out (they still need closing).</summary>
    public Tone StatusTone => IsOpen ? Tone.Info : Tone.Normal;

    /// <summary>"Chênh lệch" cell: red when cash was missing, amber when there was too much, green when it matched.</summary>
    public Tone VarianceTone => Variance switch
    {
        null => Tone.Muted,
        < 0 => Tone.Danger,
        > 0 => Tone.Warning,
        _ => Tone.Success,
    };
}

/// <summary>Value and note of one KPI card of the current shift.</summary>
public sealed record ShiftCard(string Value, string? Note)
{
    public static readonly ShiftCard Empty = new("–", null);
}

/// <summary>
/// The header of the Ca làm việc screen (badge, title, subtitle, KPI cards and which actions apply), computed from
/// the signed-in user's drawer so the view only copies texts.
/// </summary>
public sealed record CurrentShiftState(
    string BadgeText,
    Tone BadgeTone,
    string Title,
    string Subtitle,
    bool CanOpen,
    bool IsOpen,
    ShiftCard Revenue,
    ShiftCard Invoices,
    ShiftCard Cash,
    ShiftCard NonCash)
{
    /// <summary>A shift left open this long is flagged in amber (forgotten to close at the end of the day).</summary>
    public static readonly TimeSpan LongOpenThreshold = TimeSpan.FromHours(12);

    /// <summary>The shift could not be loaded: no action but Làm mới applies.</summary>
    public static CurrentShiftState Failed(string message) => new(
        "Lỗi", Tone.Danger, "Không tải được ca làm việc", message, false, false,
        ShiftCard.Empty, ShiftCard.Empty, ShiftCard.Empty, ShiftCard.Empty);

    /// <param name="current">The user's drawer as returned by <see cref="IShiftService.GetCurrentAsync"/>.</param>
    /// <param name="storeOffset">Store UTC offset for displayed times.</param>
    /// <param name="now">Current time, to flag a shift that has been open for too long.</param>
    public static CurrentShiftState From(CurrentShiftDto current, TimeSpan storeOffset, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Current is not { } shift)
        {
            return new CurrentShiftState(
                "Chưa mở ca",
                Tone.Muted,
                "Bạn chưa mở ca",
                current.SuggestedOpeningFloat > 0
                    ? $"Ca trước để lại két {Vnd.Format(current.SuggestedOpeningFloat)}. Mở ca để bắt đầu bán hàng."
                    : "Mở ca để bắt đầu bán hàng.",
                CanOpen: true,
                IsOpen: false,
                ShiftCard.Empty,
                ShiftCard.Empty,
                ShiftCard.Empty,
                ShiftCard.Empty);
        }

        var totals = shift.Totals;
        var openedAt = shift.OpenedAt.ToOffset(storeOffset);
        var openFor = now - shift.OpenedAt;
        var longOpen = openFor >= LongOpenThreshold;
        var subtitle = $"{shift.UserName} · mở lúc {openedAt.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture)} · tiền đầu ca {Vnd.Format(shift.OpeningFloat)}";
        if (longOpen)
        {
            subtitle += $" · đã mở {(int)openFor.TotalHours} giờ, nhớ kết ca cuối ngày";
        }

        return new CurrentShiftState(
            longOpen ? "Mở quá lâu" : "Đang mở",
            longOpen ? Tone.Warning : Tone.Info,
            $"Ca {shift.Code}",
            subtitle,
            CanOpen: false,
            IsOpen: true,
            new ShiftCard(
                Vnd.Format(totals.NetRevenue),
                totals.DiscountTotal > 0 ? $"Đã giảm giá {Vnd.Format(totals.DiscountTotal)}" : null),
            new ShiftCard(
                totals.SalesCount.ToString("#,0", Vnd.Culture),
                totals.VoidCount + totals.ReturnCount > 0 ? $"{totals.VoidCount} hủy · {totals.ReturnCount} trả hàng" : null),
            new ShiftCard(
                Vnd.Format(shift.ExpectedCash),
                shift.PayInTotal + shift.PayOutTotal > 0
                    ? $"Nộp {Vnd.FormatNumber(shift.PayInTotal)} · chi {Vnd.FormatNumber(shift.PayOutTotal)}"
                    : null),
            new ShiftCard(
                Vnd.Format(totals.TransferSales + totals.CardSales),
                $"CK {Vnd.FormatNumber(totals.TransferSales)} · thẻ {Vnd.FormatNumber(totals.CardSales)}"));
    }
}
