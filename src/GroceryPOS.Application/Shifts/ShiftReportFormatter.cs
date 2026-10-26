using System.Globalization;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Domain.Shifts;

namespace GroceryPOS.Application.Shifts;

/// <summary>
/// Lays a shift report out as fixed-width rows for the receipt printer: "BÁO CÁO GIỮA CA" while the shift is open
/// (X-report), "BÁO CÁO KẾT CA" once closed (Z-report). Same column widths and helpers as <see cref="ReceiptFormatter"/>,
/// so the receipt preview and print document are reused. Pure and UI-free.
/// </summary>
public static class ShiftReportFormatter
{
    private static readonly CultureInfo Vi = Vnd.Culture;

    /// <param name="shift">Live or closed shift report.</param>
    /// <param name="store">Store header.</param>
    /// <param name="columns">48 for 80 mm paper, 32 for 58 mm.</param>
    /// <param name="storeUtcOffset">Store time zone (times are printed in store time).</param>
    /// <param name="printedAt">Print time, shown at the bottom.</param>
    public static IReadOnlyList<ReceiptLine> Format(
        ShiftSummaryDto shift,
        StoreProfile store,
        int columns,
        TimeSpan storeUtcOffset,
        DateTimeOffset printedAt)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(store);
        if (columns < 24)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "A report needs at least 24 columns.");
        }

        var output = new List<ReceiptLine>();
        void Add(string text, ReceiptLineStyle style = ReceiptLineStyle.Normal) => output.Add(new ReceiptLine(text.PadRight(columns), style));
        void AddCentered(string? text, ReceiptLineStyle style = ReceiptLineStyle.Normal)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (var line in ReceiptFormatter.Wrap(text, columns))
            {
                Add(ReceiptFormatter.Center(line, columns), style);
            }
        }

        void AddPair(string left, string right, ReceiptLineStyle style = ReceiptLineStyle.Normal)
        {
            foreach (var line in ReceiptFormatter.LeftRight(left, right, columns))
            {
                Add(line, style);
            }
        }

        void AddWrapped(string text)
        {
            foreach (var line in ReceiptFormatter.Wrap(text, columns))
            {
                Add(line);
            }
        }

        string Time(DateTimeOffset value) => value.ToOffset(storeUtcOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var closed = shift.Status == ShiftStatus.Closed;
        var totals = shift.Totals;

        // ---- Header -------------------------------------------------------------------------------
        AddCentered(store.Name.ToUpper(Vi), ReceiptLineStyle.Bold);
        AddCentered(store.Address);
        Add(new string('=', columns));
        AddCentered(closed ? "BÁO CÁO KẾT CA" : "BÁO CÁO GIỮA CA", ReceiptLineStyle.Bold);
        if (!closed)
        {
            AddCentered("(ca đang mở – số liệu tạm tính)");
        }

        AddPair("Mã ca:", shift.Code);
        AddPair("Thu ngân:", shift.UserName ?? $"#{shift.UserId}");
        AddPair("Mở ca:", Time(shift.OpenedAt));
        AddPair(closed ? "Kết ca:" : "Đến lúc:", Time(shift.ClosedAt ?? printedAt));

        // ---- Sales --------------------------------------------------------------------------------
        Add(new string('-', columns));
        Add("DOANH THU", ReceiptLineStyle.Bold);
        AddPair($"Bán hàng ({Count(totals.SalesCount)} HĐ)", Vnd.FormatNumber(totals.SalesTotal));
        if (totals.DiscountTotal > 0)
        {
            AddPair("  Đã giảm giá", Vnd.FormatNumber(totals.DiscountTotal));
        }

        if (totals.VoidCount > 0)
        {
            AddPair($"Hủy hóa đơn ({Count(totals.VoidCount)})", Minus(totals.VoidTotal));
        }

        var refunds = totals.CashRefunds + totals.TransferRefunds;
        if (totals.ReturnCount > 0 || refunds > 0)
        {
            AddPair($"Trả hàng ({Count(totals.ReturnCount)} phiếu)", Minus(refunds));
        }

        AddPair("DOANH THU THUẦN", Vnd.Format(totals.NetRevenue), ReceiptLineStyle.Bold);

        // ---- Tenders ------------------------------------------------------------------------------
        Add(new string('-', columns));
        Add("THEO HÌNH THỨC THANH TOÁN", ReceiptLineStyle.Bold);
        AddPair("Tiền mặt (đã trừ tiền thừa)", Vnd.FormatNumber(totals.CashSales));
        AddPair("Chuyển khoản", Vnd.FormatNumber(totals.TransferSales));
        AddPair("Thẻ", Vnd.FormatNumber(totals.CardSales));
        if (totals.TransferRefunds > 0)
        {
            AddPair("Hoàn tiền chuyển khoản", Minus(totals.TransferRefunds));
        }

        // ---- Drawer -------------------------------------------------------------------------------
        Add(new string('-', columns));
        Add("TIỀN MẶT TRONG KÉT", ReceiptLineStyle.Bold);
        AddPair("Tiền đầu ca", Vnd.FormatNumber(shift.OpeningFloat));
        AddPair("+ Thu tiền mặt bán hàng", Vnd.FormatNumber(totals.CashSales));
        if (totals.VoidCash > 0)
        {
            AddPair("- Trả lại do hủy HĐ", Minus(totals.VoidCash));
        }

        if (totals.CashRefunds > 0)
        {
            AddPair("- Hoàn tiền trả hàng", Minus(totals.CashRefunds));
        }

        if (shift.PayInTotal > 0)
        {
            AddPair("+ Nộp tiền vào két", Vnd.FormatNumber(shift.PayInTotal));
        }

        if (shift.PayOutTotal > 0)
        {
            AddPair("- Chi tiền từ két", Minus(shift.PayOutTotal));
        }

        AddPair("Tiền mặt theo hệ thống", Vnd.Format(shift.ExpectedCash), ReceiptLineStyle.Bold);
        if (shift.CountedCash is { } counted)
        {
            AddPair("Tiền mặt thực đếm", Vnd.Format(counted), ReceiptLineStyle.Bold);
            var variance = shift.Variance ?? 0;
            AddPair($"Chênh lệch ({shift.VarianceLabel})", Signed(variance));
            AddPair("Để lại két cho ca sau", Vnd.FormatNumber(shift.CashLeftInDrawer ?? 0));
            AddPair("Bàn giao", Vnd.FormatNumber(shift.CashHandedOver ?? 0), ReceiptLineStyle.Bold);
        }

        // ---- Pay-ins / pay-outs -------------------------------------------------------------------
        if (shift.CashMovements.Count > 0)
        {
            Add(new string('-', columns));
            Add("THU / CHI NGOÀI BÁN HÀNG", ReceiptLineStyle.Bold);
            foreach (var movement in shift.CashMovements)
            {
                var sign = movement.Type == CashMovementType.PayIn ? "+" : "-";
                var time = movement.Timestamp.ToOffset(storeUtcOffset).ToString("HH:mm", CultureInfo.InvariantCulture);
                AddPair($"{time} {movement.Reason}", sign + Vnd.FormatNumber(movement.Amount));
            }
        }

        // ---- Notes & signatures -------------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(shift.OpeningNote) || !string.IsNullOrWhiteSpace(shift.CloseNote))
        {
            Add(new string('-', columns));
            if (!string.IsNullOrWhiteSpace(shift.OpeningNote))
            {
                AddWrapped($"Ghi chú mở ca: {shift.OpeningNote.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(shift.CloseNote))
            {
                AddWrapped($"Ghi chú kết ca: {shift.CloseNote.Trim()}");
            }
        }

        Add(new string('=', columns));
        if (closed)
        {
            var half = columns / 2;
            Add(ReceiptFormatter.Center("Thu ngân", half) + ReceiptFormatter.Center("Người nhận", columns - half));
            Add(string.Empty);
            Add(string.Empty);
            Add(string.Empty);
        }

        AddCentered($"In lúc {Time(printedAt)}");
        return output;
    }

    private static string Count(int value) => value.ToString("#,0", Vi);

    private static string Minus(decimal amount) => amount == 0 ? "0" : "-" + Vnd.FormatNumber(amount);

    private static string Signed(decimal amount) => amount > 0 ? "+" + Vnd.FormatNumber(amount) : Vnd.FormatNumber(amount);
}
