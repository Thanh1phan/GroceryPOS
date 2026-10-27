using System.Globalization;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales.Receipts;

/// <summary>
/// Lays a customer return out as a refund slip (PHIẾU TRẢ HÀNG) for the receipt printer: original invoice, returned
/// items with their refund, written-off goods marked, refund method, loyalty points taken back/restored, reason and
/// signature lines. Same column widths and helpers as <see cref="ReceiptFormatter"/>. Pure and UI-free.
/// </summary>
public static class ReturnSlipFormatter
{
    private static readonly CultureInfo Vi = Vnd.Culture;

    /// <param name="slip">Outcome of <see cref="IReturnService.CreateAsync"/>.</param>
    /// <param name="store">Store header.</param>
    /// <param name="cashierName">Who took the return.</param>
    /// <param name="columns">48 for 80 mm paper, 32 for 58 mm.</param>
    /// <param name="storeUtcOffset">Store time zone (the time is printed in store time).</param>
    public static IReadOnlyList<ReceiptLine> Format(
        ReturnReceipt slip,
        StoreProfile store,
        string? cashierName,
        int columns,
        TimeSpan storeUtcOffset)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(store);
        if (columns < 24)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "A slip needs at least 24 columns.");
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

        // ---- Header -------------------------------------------------------------------------------
        AddCentered(store.Name.ToUpper(Vi), ReceiptLineStyle.Bold);
        AddCentered(store.Address);
        if (!string.IsNullOrWhiteSpace(store.Phone))
        {
            AddCentered($"ĐT: {store.Phone.Trim()}");
        }

        Add(new string('=', columns));
        AddCentered("PHIẾU TRẢ HÀNG", ReceiptLineStyle.Bold);
        AddPair("Số phiếu:", slip.Code);
        AddPair("Hóa đơn gốc:", slip.SaleCode);
        AddPair("Ngày:", slip.ReturnedAt.ToOffset(storeUtcOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(cashierName))
        {
            AddPair("Thu ngân:", cashierName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(slip.CustomerName))
        {
            AddPair("Khách hàng:", slip.CustomerName.Trim());
        }

        // ---- Items --------------------------------------------------------------------------------
        Add(new string('-', columns));
        AddPair("Hàng trả", "Hoàn tiền", ReceiptLineStyle.Bold);
        var number = 0;
        foreach (var line in slip.Lines)
        {
            number++;
            foreach (var row in ReceiptFormatter.Wrap($"{number}. {line.ProductName}", columns))
            {
                Add(row);
            }

            AddPair($"   {Quantity(line.Quantity)} {line.Unit}", Vnd.FormatNumber(line.RefundAmount));
            if (!line.Restock)
            {
                foreach (var row in ReceiptFormatter.Wrap("   (hàng hỏng, không nhập kho)", columns))
                {
                    Add(row);
                }
            }
        }

        // ---- Totals -------------------------------------------------------------------------------
        Add(new string('-', columns));
        Add($"Số lượng trả: {Quantity(slip.Lines.Sum(l => l.Quantity))}");
        AddPair("TỔNG TIỀN HOÀN", Vnd.Format(slip.RefundTotal), ReceiptLineStyle.Bold);
        AddPair("Hình thức hoàn:", SaleLabels.Of(slip.RefundMethod));
        if (slip.SaleFullyReturned)
        {
            AddCentered("(hóa đơn đã được trả hết)");
        }

        // ---- Loyalty ------------------------------------------------------------------------------
        if (slip.PointsReversed > 0 || slip.PointsRestored > 0 || slip.CustomerPointsBalance is not null)
        {
            Add(new string('-', columns));
            if (slip.PointsReversed > 0)
            {
                AddPair("Điểm tích lũy bị trừ", "-" + Count(slip.PointsReversed));
            }

            if (slip.PointsRestored > 0)
            {
                AddPair("Điểm đã dùng được hoàn", "+" + Count(slip.PointsRestored));
            }

            if (slip.CustomerPointsBalance is { } balance)
            {
                AddPair("Điểm hiện có", Count(balance));
            }
        }

        // ---- Reason & signatures ------------------------------------------------------------------
        Add(new string('-', columns));
        foreach (var row in ReceiptFormatter.Wrap($"Lý do: {slip.Reason.Trim()}", columns))
        {
            Add(row);
        }

        Add(new string('=', columns));
        var half = columns / 2;
        Add(ReceiptFormatter.Center("Khách hàng", half) + ReceiptFormatter.Center("Thu ngân", columns - half));
        Add(string.Empty);
        Add(string.Empty);
        Add(string.Empty);
        AddCentered("Vui lòng giữ phiếu này để đối chiếu.");
        return output;
    }

    private static string Quantity(decimal quantity) => quantity.ToString("#,0.###", Vi);

    private static string Count(int value) => value.ToString("#,0", Vi);
}
