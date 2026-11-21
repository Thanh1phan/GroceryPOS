using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Purchasing;

/// <summary>
/// Period of the Công nợ NCC documents (payments, returns and their KPI cards) as picked in the view:
/// store-local dates, both inclusive. The payables list is not filtered – debt is always "as of now".
/// </summary>
public sealed record DocumentPeriod(DateTime? From, DateTime? To)
{
    /// <summary>The default period: first day of the current month up to today.</summary>
    public static DocumentPeriod ThisMonth(DateTime today) => new(new DateTime(today.Year, today.Month, 1), today.Date);
}

/// <summary>A row of the "Phiếu chi" tab, with the date already in store time.</summary>
public sealed record SupplierPaymentRow(
    int Id,
    string Code,
    DateTime PaidAt,
    int SupplierId,
    string SupplierName,
    string MethodName,
    decimal Amount,
    decimal DebtAfter,
    string? Reference,
    bool IsCancelled,
    string? CancelReason)
{
    /// <summary>"Đã chi", or "Đã hủy: lý do".</summary>
    public string StatusName => IsCancelled
        ? $"Đã hủy{(string.IsNullOrWhiteSpace(CancelReason) ? string.Empty : $": {CancelReason}")}"
        : "Đã chi";

    /// <summary>Whole row muted for cancelled payments.</summary>
    public Tone RowTone => IsCancelled ? Tone.Muted : Tone.Normal;

    /// <summary>"Trạng thái" cell: red for a cancelled payment.</summary>
    public Tone StatusTone => IsCancelled ? Tone.Danger : Tone.Success;

    public static SupplierPaymentRow From(SupplierPaymentDto p, TimeSpan storeOffset)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new SupplierPaymentRow(
            p.Id, p.Code, p.PaidAt.ToOffset(storeOffset).DateTime, p.SupplierId, p.SupplierName, p.MethodName,
            p.Amount, p.DebtAfter, p.Reference, p.IsCancelled, p.CancelReason);
    }
}

/// <summary>A row of the "Trả hàng NCC" tab, with the date already in store time.</summary>
public sealed record SupplierReturnRow(
    int Id,
    string Code,
    DateTime ReturnDate,
    int SupplierId,
    string SupplierName,
    string GoodsReceiptCode,
    int LineCount,
    decimal Total,
    decimal RefundAmount,
    decimal DebtOffset)
{
    public static SupplierReturnRow From(SupplierReturnSummary r, TimeSpan storeOffset)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new SupplierReturnRow(
            r.Id, r.Code, r.ReturnDate.ToOffset(storeOffset).DateTime, r.SupplierId, r.SupplierName,
            r.GoodsReceiptCode ?? "–", r.LineCount, r.Total, r.RefundAmount, r.DebtOffset);
    }
}

/// <summary>
/// The four KPI cards of the Công nợ NCC screen. Each part is null when it could not be loaded (or, for returns,
/// when the user may not see them) so the card shows "–" with a reason instead of a misleading 0.
/// </summary>
public sealed record SupplierDebtSummary(
    IReadOnlyList<SupplierPayableDto>? Payables,
    IReadOnlyList<SupplierPaymentRow>? Payments,
    IReadOnlyList<SupplierReturnRow>? Returns,
    bool CanSeeReturns)
{
    public static readonly SupplierDebtSummary Empty = new(null, null, null, CanSeeReturns: true);

    public decimal TotalDebt => Payables?.Sum(p => p.DebtBalance) ?? 0;

    public decimal PaidAmount => Payments?.Where(p => !p.IsCancelled).Sum(p => p.Amount) ?? 0;

    public decimal ReturnedAmount => Returns?.Sum(r => r.Total) ?? 0;

    /// <summary>The supplier owed the most (payables are not assumed to be sorted).</summary>
    public SupplierPayableDto? Largest => Payables?.OrderByDescending(p => p.DebtBalance).ThenBy(p => p.Name, StringComparer.Ordinal).FirstOrDefault();

    public string TotalText => Payables is null ? "–" : Vnd.Format(TotalDebt);

    public string TotalNote => Payables switch
    {
        null => "Không tải được công nợ",
        { Count: 0 } => "Không còn nợ NCC",
        { Count: var n } => $"{Count(n)} nhà cung cấp",
    };

    public string PaidText => Payments is null ? "–" : Vnd.Format(PaidAmount);

    public string PaidNote
    {
        get
        {
            if (Payments is null)
            {
                return "Không tải được phiếu chi";
            }

            var valid = Payments.Count(p => !p.IsCancelled);
            var cancelled = Payments.Count - valid;
            return $"{Count(valid)} phiếu chi" + (cancelled > 0 ? $" · {Count(cancelled)} phiếu đã hủy" : string.Empty);
        }
    }

    public string ReturnedText => Returns is null ? "–" : Vnd.Format(ReturnedAmount);

    public string? ReturnedNote => Returns switch
    {
        null when !CanSeeReturns => "Cần quyền nhập hàng",
        null => "Không tải được phiếu trả",
        _ => $"{Count(Returns.Count)} phiếu · cấn trừ nợ {Vnd.Format(Returns.Sum(r => r.DebtOffset))}",
    };

    public string LargestText => Largest is { } top ? Vnd.Format(top.DebtBalance) : "–";

    public string? LargestNote => Largest?.Name;

    private static string Count(int value) => value.ToString("#,0", Vnd.Culture);
}
