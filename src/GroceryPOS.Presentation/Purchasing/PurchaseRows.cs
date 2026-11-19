using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Purchasing;

/// <summary>
/// Filters of the Nhập hàng list as picked in the view: a date range (store-local dates, both inclusive),
/// an optional status and an optional supplier (null = all).
/// </summary>
public sealed record PurchaseFilter(DateTime? From, DateTime? To, GoodsReceiptStatus? Status = null, int? SupplierId = null)
{
    /// <summary>The default period: first day of the current month up to today.</summary>
    public static PurchaseFilter ThisMonth(DateTime today) => new(new DateTime(today.Year, today.Month, 1), today.Date);
}

/// <summary>One entry of the supplier filter; <see cref="Id"/> 0 is "Tất cả NCC".</summary>
public sealed record SupplierFilterOption(int Id, string Name)
{
    public static readonly SupplierFilterOption All = new(0, "Tất cả NCC");
}

/// <summary>One entry of the status filter; null status is "Tất cả trạng thái".</summary>
public sealed record StatusFilterOption(GoodsReceiptStatus? Status, string Name)
{
    public static IReadOnlyList<StatusFilterOption> All { get; } =
    [
        new(null, "Tất cả trạng thái"),
        new(GoodsReceiptStatus.Draft, GoodsReceiptStatus.Draft.DisplayName()),
        new(GoodsReceiptStatus.Completed, GoodsReceiptStatus.Completed.DisplayName()),
        new(GoodsReceiptStatus.Cancelled, GoodsReceiptStatus.Cancelled.DisplayName()),
    ];
}

/// <summary>A row of the receipt list, with the date already in store time.</summary>
public sealed record PurchaseReceiptRow(
    int Id,
    string Code,
    DateTime ReceiptDate,
    GoodsReceiptStatus Status,
    string StatusName,
    int SupplierId,
    string SupplierName,
    int LineCount,
    decimal Total,
    decimal PaidAmount,
    decimal DebtAmount)
{
    public bool IsDraft => Status == GoodsReceiptStatus.Draft;

    public static PurchaseReceiptRow From(GoodsReceiptSummary r, TimeSpan storeOffset)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new PurchaseReceiptRow(
            r.Id, r.Code, r.ReceiptDate.ToOffset(storeOffset).DateTime, r.Status, r.StatusName,
            r.SupplierId, r.SupplierName, r.LineCount, r.Total, r.PaidAmount, r.DebtAmount);
    }

    /// <summary>Whole row muted for cancelled receipts.</summary>
    public Tone RowTone => Status == GoodsReceiptStatus.Cancelled ? Tone.Muted : Tone.Normal;

    /// <summary>"Trạng thái" cell: drafts stand out (they still need finishing).</summary>
    public Tone StatusTone => Status switch
    {
        GoodsReceiptStatus.Draft => Tone.Info,
        GoodsReceiptStatus.Cancelled => Tone.Muted,
        _ => Tone.Normal,
    };

    /// <summary>"Ghi nợ" cell: amber when a completed receipt left debt with the supplier.</summary>
    public Tone DebtTone => Status switch
    {
        GoodsReceiptStatus.Completed when DebtAmount > 0 => Tone.Warning,
        GoodsReceiptStatus.Cancelled => Tone.Muted,
        _ => Tone.Normal,
    };
}

/// <summary>
/// The four KPI cards of the Nhập hàng screen. Amount, debt and count cover completed receipts of the period;
/// open drafts are counted whatever the period (a forgotten draft from last month still needs attention).
/// </summary>
public sealed record PurchaseSummary(
    decimal ReceivedAmount,
    decimal PaidAmount,
    decimal NewDebt,
    decimal TotalSupplierDebt,
    int CompletedCount,
    int SupplierCount,
    int? OpenDrafts,
    DateTime? OldestDraft)
{
    public static readonly PurchaseSummary Empty = new(0, 0, 0, 0, 0, 0, 0, null);

    public string AmountText => Vnd.Format(ReceivedAmount);

    public string AmountNote => $"Đã trả {Vnd.Format(PaidAmount)}";

    public string DebtText => Vnd.Format(NewDebt);

    public string DebtNote => $"Tổng công nợ NCC {Vnd.Format(TotalSupplierDebt)}";

    public string CountText => Count(CompletedCount);

    public string CountNote => $"{Count(SupplierCount)} nhà cung cấp";

    /// <summary>"–" when the drafts could not be loaded (instead of a misleading 0).</summary>
    public string DraftsText => OpenDrafts is { } n ? Count(n) : "–";

    public string DraftsNote => OpenDrafts switch
    {
        null => "Không tải được phiếu tạm",
        0 => "Không có phiếu tồn đọng",
        _ => OldestDraft is { } oldest ? $"Cũ nhất: {oldest:dd/MM/yyyy}" : string.Empty,
    };

    /// <param name="receipts">Rows of the period (any status).</param>
    /// <param name="drafts">All open drafts, or null when they could not be loaded.</param>
    /// <param name="suppliers">All suppliers (for the total debt).</param>
    public static PurchaseSummary From(
        IEnumerable<PurchaseReceiptRow> receipts, IReadOnlyList<PurchaseReceiptRow>? drafts, IEnumerable<SupplierDto> suppliers)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(suppliers);
        var completed = receipts.Where(r => r.Status == GoodsReceiptStatus.Completed).ToList();
        return new PurchaseSummary(
            completed.Sum(r => r.Total),
            completed.Sum(r => r.PaidAmount),
            completed.Sum(r => r.DebtAmount),
            suppliers.Sum(s => s.DebtBalance),
            completed.Count,
            completed.Select(r => r.SupplierId).Distinct().Count(),
            drafts?.Count,
            drafts is { Count: > 0 } ? drafts.Min(d => d.ReceiptDate) : null);
    }

    private static string Count(int value) => value.ToString("#,0", Vnd.Culture);
}
