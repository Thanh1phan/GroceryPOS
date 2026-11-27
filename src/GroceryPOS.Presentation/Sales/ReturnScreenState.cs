using System.Globalization;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Sales;

/// <summary>
/// Everything the Trả hàng screen shows around the line grid: invoice badge and header, the refund summary card, the
/// message under it (blocker or summary) and which buttons work. Computed from the <see cref="ReturnSheet"/> so the
/// view only copies values.
/// </summary>
public sealed record ReturnScreenState(
    bool HasInvoice,
    string? BadgeText,
    Tone BadgeTone,
    string InvoiceInfo,
    string LineCount,
    string WrittenOff,
    string Refund,
    string Points,
    string Message,
    Tone MessageTone,
    bool CanSubmit,
    bool CanReturnAll,
    PaymentMethod RefundMethod)
{
    public const string NoInvoiceMessage = "Quét hoặc nhập số hóa đơn của khách.";

    public static ReturnScreenState Empty { get; } = new(
        HasInvoice: false,
        BadgeText: null,
        BadgeTone: Tone.Muted,
        InvoiceInfo: "Chưa chọn hóa đơn.",
        LineCount: "0",
        WrittenOff: Vnd.Format(0),
        Refund: Vnd.Format(0),
        Points: string.Empty,
        Message: NoInvoiceMessage,
        MessageTone: Tone.Warning,
        CanSubmit: false,
        CanReturnAll: false,
        RefundMethod: PaymentMethod.Cash);

    /// <param name="hasOpenShift">Whether the cashier has an open shift; null = unknown (no shift permission, load failed).</param>
    /// <param name="busy">A refund is being saved: Hoàn tiền stays off.</param>
    public static ReturnScreenState From(ReturnSheet? sheet, SalesOptions options, bool? hasOpenShift, bool busy)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (sheet is null)
        {
            return Empty;
        }

        var sale = sheet.Sale;
        var (badge, badgeTone) = sale.IsFullyReturned
            ? ("Đã trả hết", Tone.Muted)
            : sale.IsWithinReturnWindow
                ? ("Trong hạn đổi trả", Tone.Success)
                : ("Quá hạn đổi trả", sale.CanReturn ? Tone.Warning : Tone.Danger);

        var parts = new List<string>
        {
            sale.Code,
            sale.SaleDate.ToOffset(options.StoreUtcOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            sale.CustomerName ?? "Khách lẻ",
            $"Tổng {Vnd.Format(sale.Total)}",
        };
        if (sale.RefundedTotal > 0)
        {
            parts.Add($"đã hoàn {Vnd.Format(sale.RefundedTotal)}");
        }

        var blocker = sheet.Blocker ?? ShiftBlocker(sheet, options, hasOpenShift);
        return new ReturnScreenState(
            HasInvoice: true,
            BadgeText: badge,
            BadgeTone: badgeTone,
            InvoiceInfo: string.Join(" · ", parts),
            LineCount: $"{sheet.SelectedLineCount} dòng · SL {sheet.SelectedQuantity.ToString("#,0.###", Vnd.Culture)}",
            WrittenOff: Vnd.Format(sheet.WrittenOffRefund),
            Refund: Vnd.Format(sheet.RefundTotal),
            Points: DescribePoints(sheet),
            Message: blocker ?? sheet.Summary + ".",
            MessageTone: blocker is null ? Tone.Muted : Tone.Warning,
            CanSubmit: blocker is null && !busy,
            CanReturnAll: sheet.CanReturnAnything && !busy,
            RefundMethod: sheet.RefundMethod);
    }

    /// <summary>Cash refunds come out of the drawer, so they need an open shift when the store requires shifts.</summary>
    public static string? ShiftBlocker(ReturnSheet sheet, SalesOptions options, bool? hasOpenShift)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);
        return sheet.RefundMethod == PaymentMethod.Cash && options.RequireOpenShift && hasOpenShift == false
            ? "Bạn chưa mở ca nên không thể hoàn tiền mặt. Mở ca ở màn hình Ca làm việc hoặc hoàn bằng chuyển khoản."
            : null;
    }

    private static string DescribePoints(ReturnSheet sheet)
    {
        if (sheet.Sale.CustomerId is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (sheet.PointsToReverse > 0)
        {
            parts.Add($"Trừ {sheet.PointsToReverse.ToString("#,0", Vnd.Culture)} điểm đã tích");
        }

        if (sheet.PointsToRestore > 0)
        {
            parts.Add($"{(parts.Count == 0 ? "Hoàn" : "hoàn")} lại {sheet.PointsToRestore.ToString("#,0", Vnd.Culture)} điểm đã dùng");
        }

        return parts.Count == 0 ? "Không thay đổi điểm tích lũy." : string.Join(", ", parts) + ".";
    }
}

/// <summary>What the line buttons (+1, −1, Trả hết dòng, Số lượng, Hàng hỏng) do for the selected line.</summary>
public sealed record ReturnLineActions(bool CanEdit, string RestockCaption)
{
    public static ReturnLineActions For(ReturnSheet? sheet, ReturnSheetLine? line, bool busy = false) => new(
        CanEdit: !busy && sheet is { CanReturnAnything: true } && line is { IsExhausted: false },
        RestockCaption: line is { IsSelected: true, Restock: false } ? "Nhập lại kho (F6)" : "Hàng hỏng (F6)");
}

/// <summary>Status, history and confirmation texts of the Trả hàng screen.</summary>
public static class ReturnTexts
{
    public static string WindowHint(SalesOptions options) =>
        $"Khách được đổi trả trong {options.ReturnWindowDays} ngày kể từ ngày mua.";

    /// <summary>The status line after an invoice was loaded, and its tone.</summary>
    public static (string Text, Tone Tone) DescribeInvoice(ReturnableSaleDto sale, SalesOptions options)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (sale.IsFullyReturned)
        {
            return ("Hóa đơn này đã được trả hết hàng.", Tone.Warning);
        }

        if (!sale.IsWithinReturnWindow)
        {
            return sale.CanReturn
                ? ($"Hóa đơn đã quá {options.ReturnWindowDays} ngày – bạn đang trả hàng với quyền quản lý.", Tone.Warning)
                : ($"Hóa đơn đã quá {options.ReturnWindowDays} ngày đổi trả. Vui lòng nhờ quản lý thực hiện.", Tone.Warning);
        }

        return ("Chọn sản phẩm khách trả: Enter/F4 nhập số lượng, Space trả hết dòng, F6 đánh dấu hàng hỏng.", Tone.Muted);
    }

    /// <summary>"Đã trả trước đó" card: one line per earlier return of the invoice.</summary>
    public static string History(IReadOnlyList<SaleReturnSummaryDto> returns, TimeSpan storeOffset)
    {
        ArgumentNullException.ThrowIfNull(returns);
        return returns.Count == 0
            ? "Chưa có phiếu trả nào cho hóa đơn này."
            : string.Join(
                Environment.NewLine,
                returns.Select(r =>
                    $"{r.Code} · {r.ReturnedAt.ToOffset(storeOffset).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)} · " +
                    $"{Vnd.Format(r.RefundTotal)} ({SaleLabels.Of(r.RefundMethod)}) – {r.Reason}"));
    }

    /// <summary>Success line after a return, e.g. "✔ TH…: hoàn 35.000 ₫ (Tiền mặt) cho HĐ … · trừ 3 điểm".</summary>
    public static string Done(ReturnReceipt slip)
    {
        ArgumentNullException.ThrowIfNull(slip);
        var text = $"✔ {slip.Code}: hoàn {Vnd.Format(slip.RefundTotal)} ({SaleLabels.Of(slip.RefundMethod)}) cho HĐ {slip.SaleCode}";
        if (slip.PointsReversed > 0)
        {
            text += $" · trừ {slip.PointsReversed.ToString("#,0", Vnd.Culture)} điểm";
        }

        if (slip.PointsRestored > 0)
        {
            text += $" · hoàn lại {slip.PointsRestored.ToString("#,0", Vnd.Culture)} điểm đã dùng";
        }

        if (slip.WrittenOffCost > 0)
        {
            text += $" · hủy hàng giá vốn {Vnd.Format(slip.WrittenOffCost)}";
        }

        return text;
    }
}
