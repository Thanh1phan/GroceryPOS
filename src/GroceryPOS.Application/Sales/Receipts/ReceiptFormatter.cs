using System.Globalization;
using System.Text;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Sales.Receipts;

public enum ReceiptLineStyle
{
    Normal = 0,

    /// <summary>Printed in bold (store name, title, grand total).</summary>
    Bold = 1,
}

/// <summary>One printed row, already padded to the receipt width (monospace).</summary>
public sealed record ReceiptLine(string Text, ReceiptLineStyle Style = ReceiptLineStyle.Normal)
{
    public bool IsBold => Style == ReceiptLineStyle.Bold;
}

/// <summary>
/// Lays a <see cref="ReceiptData"/> out as fixed-width text for thermal printers: 48 columns on 80 mm paper,
/// 32 columns on 58 mm. Pure and UI-free so the same layout feeds the print preview, the printer and tests.
/// Text is normalized to NFC so every Vietnamese letter takes exactly one column.
/// </summary>
public static class ReceiptFormatter
{
    public const int Columns80Mm = 48;
    public const int Columns58Mm = 32;

    private static readonly CultureInfo Vi = Vnd.Culture;

    public static int ColumnsFor(int paperWidthMm) => paperWidthMm <= 58 ? Columns58Mm : Columns80Mm;

    public static IReadOnlyList<ReceiptLine> Format(ReceiptData receipt, int columns = Columns80Mm)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (columns < 24)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, "A receipt needs at least 24 columns.");
        }

        var output = new List<ReceiptLine>();
        void Add(string text, ReceiptLineStyle style = ReceiptLineStyle.Normal) => output.Add(new ReceiptLine(text.PadRight(columns), style));
        void AddCentered(string? text, ReceiptLineStyle style = ReceiptLineStyle.Normal)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (var line in Wrap(text, columns))
            {
                Add(Center(line, columns), style);
            }
        }

        void AddPair(string left, string right, ReceiptLineStyle style = ReceiptLineStyle.Normal)
        {
            foreach (var line in LeftRight(left, right, columns))
            {
                Add(line, style);
            }
        }

        var store = receipt.Store;

        // ---- Header -------------------------------------------------------------------------------
        AddCentered(store.Name.ToUpper(Vi), ReceiptLineStyle.Bold);
        AddCentered(store.Address);
        var contact = string.Join(" - ", new[]
        {
            string.IsNullOrWhiteSpace(store.Phone) ? null : $"ĐT: {store.Phone.Trim()}",
            string.IsNullOrWhiteSpace(store.TaxCode) ? null : $"MST: {store.TaxCode.Trim()}",
        }.Where(p => p is not null));
        AddCentered(contact);
        Add(Rule('=', columns));
        AddCentered("HÓA ĐƠN BÁN LẺ", ReceiptLineStyle.Bold);
        if (receipt.IsReprint)
        {
            AddCentered("** BẢN IN LẠI **", ReceiptLineStyle.Bold);
        }

        if (receipt.Status == SaleStatus.Voided)
        {
            AddCentered("*** HÓA ĐƠN ĐÃ HỦY ***", ReceiptLineStyle.Bold);
        }

        AddPair($"Số: {receipt.Code}", receipt.IssuedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        AddPair("Thu ngân:", receipt.CashierName);
        if (receipt.Member is { } member)
        {
            AddPair("Khách hàng:", member.MaskedPhone is null ? member.Name : $"{member.Name} ({member.MaskedPhone})");
        }

        // ---- Items --------------------------------------------------------------------------------
        Add(Rule('-', columns));
        AddPair("Mặt hàng", "Thành tiền");
        Add(Rule('-', columns));
        for (var i = 0; i < receipt.Items.Count; i++)
        {
            var item = receipt.Items[i];
            var number = $"{i + 1}. ";
            var nameLines = Wrap(item.Name, columns - number.Length);
            for (var n = 0; n < nameLines.Count; n++)
            {
                Add((n == 0 ? number : new string(' ', number.Length)) + nameLines[n]);
            }

            var indent = new string(' ', number.Length);
            AddPair($"{indent}{Quantity(item.Quantity)} {item.Unit} x {Vnd.FormatNumber(item.UnitPrice)}", Vnd.FormatNumber(item.GrossAmount));
            if (item.DiscountAmount > 0)
            {
                AddPair($"{indent}{item.DiscountLabel ?? "Giảm giá"}", Minus(item.DiscountAmount));
            }
        }

        // ---- Totals -------------------------------------------------------------------------------
        Add(Rule('-', columns));
        AddPair($"Tổng tiền hàng ({receipt.Items.Count} món)", Vnd.FormatNumber(receipt.GrossAmount));
        if (receipt.LineDiscountTotal > 0)
        {
            AddPair("Giảm giá mặt hàng", Minus(receipt.LineDiscountTotal));
        }

        if (receipt.InvoiceDiscountAmount > 0)
        {
            var label = receipt.InvoiceDiscountLabel is { } text && text.Contains('%')
                ? $"Giảm giá hóa đơn ({text["Giảm ".Length..]})"
                : "Giảm giá hóa đơn";
            AddPair(label, Minus(receipt.InvoiceDiscountAmount));
        }

        if (receipt.PointsDiscountAmount > 0)
        {
            var points = receipt.Member?.PointsRedeemed ?? 0;
            AddPair(points > 0 ? $"Đổi {points.ToString("#,0", Vi)} điểm" : "Đổi điểm", Minus(receipt.PointsDiscountAmount));
        }

        AddPair("TỔNG THANH TOÁN", Vnd.Format(receipt.Total), ReceiptLineStyle.Bold);
        foreach (var payment in receipt.Payments)
        {
            AddPair(payment.Label, Vnd.FormatNumber(payment.Amount));
            if (!string.IsNullOrWhiteSpace(payment.Reference))
            {
                AddPair("  Mã GD:", payment.Reference.Trim());
            }
        }

        if (receipt.Change > 0)
        {
            AddPair("Tiền thừa trả khách", Vnd.FormatNumber(receipt.Change));
        }

        // ---- VAT ----------------------------------------------------------------------------------
        var taxed = receipt.VatBreakdown.Where(v => v.VatRate > 0).ToList();
        if (taxed.Count > 0)
        {
            Add(Rule('-', columns));
            Add("Giá đã bao gồm thuế GTGT:");
            foreach (var vat in taxed)
            {
                AddPair($"  Thuế {Percent(vat.VatRate)} trên {Vnd.FormatNumber(vat.AmountInclVat)}", Vnd.FormatNumber(vat.VatAmount));
            }
        }

        // ---- Loyalty & note -----------------------------------------------------------------------
        if (receipt.Member is { } m && (m.PointsEarned > 0 || m.PointsRedeemed > 0 || m.PointsBalance is not null))
        {
            Add(Rule('-', columns));
            if (m.PointsEarned > 0)
            {
                AddPair("Điểm tích lũy:", $"+{m.PointsEarned.ToString("#,0", Vi)}");
            }

            if (m.PointsRedeemed > 0)
            {
                AddPair("Điểm đã dùng:", $"-{m.PointsRedeemed.ToString("#,0", Vi)}");
            }

            if (m.PointsBalance is { } balance)
            {
                AddPair("Điểm hiện có:", balance.ToString("#,0", Vi));
            }
        }

        if (!string.IsNullOrWhiteSpace(receipt.Note))
        {
            Add(Rule('-', columns));
            foreach (var line in Wrap($"Ghi chú: {receipt.Note.Trim()}", columns))
            {
                Add(line);
            }
        }

        // ---- Footer -------------------------------------------------------------------------------
        Add(Rule('=', columns));
        foreach (var footer in (store.ReceiptFooter ?? string.Empty).Split('\n'))
        {
            AddCentered(footer.Trim());
        }

        if (receipt.IsReprint && receipt.PrintedAt is { } printedAt)
        {
            AddCentered($"In lại lúc {printedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}");
        }

        return output;
    }

    /// <summary>Plain text (one row per line), e.g. for logging or a text-only printer driver.</summary>
    public static string ToText(IEnumerable<ReceiptLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text.TrimEnd()));

    /// <summary>Centers <paramref name="text"/> in <paramref name="width"/> columns (extra space goes right).</summary>
    public static string Center(string text, int width)
    {
        text = Normalize(text).Trim();
        if (text.Length >= width)
        {
            return text[..width];
        }

        var left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }

    /// <summary>
    /// Left text and right-aligned value on one row. When both do not fit, the left text wraps and the value
    /// goes on its last row (or on a row of its own when that last piece is still too long).
    /// </summary>
    public static IReadOnlyList<string> LeftRight(string left, string right, int width)
    {
        left = Normalize(left).TrimEnd();
        right = Normalize(right).Trim();
        if (right.Length >= width)
        {
            var rows = Wrap(left, width).Select(l => l.PadRight(width)).ToList();
            rows.Add(right.Length > width ? right[..width] : right);
            return rows;
        }

        if (left.Length + 1 + right.Length <= width)
        {
            return new[] { left + new string(' ', width - left.Length - right.Length) + right };
        }

        var indent = left.Length - left.TrimStart().Length;
        var available = width - right.Length - 1;
        var wrapped = Wrap(left, width);
        var result = wrapped.Take(wrapped.Count - 1).Select(l => l.PadRight(width)).ToList();
        var last = wrapped[^1];
        if (last.Length > available)
        {
            result.Add(last.PadRight(width));
            last = new string(' ', Math.Min(indent, available));
        }

        result.Add(last + new string(' ', width - last.Length - right.Length) + right);
        return result;
    }

    /// <summary>Word-wraps to <paramref name="width"/> columns, keeping leading indentation; over-long words are split.</summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        text = Normalize(text).TrimEnd();
        if (text.Length <= width)
        {
            return new[] { text };
        }

        var indent = text[..(text.Length - text.TrimStart().Length)];
        if (indent.Length >= width / 2)
        {
            indent = string.Empty;
        }

        var lines = new List<string>();
        var current = new StringBuilder(indent);
        foreach (var rawWord in text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = rawWord;
            while (true)
            {
                var hasContent = current.Length > indent.Length;
                var needed = word.Length + (hasContent ? 1 : 0);
                if (current.Length + needed <= width)
                {
                    if (hasContent)
                    {
                        current.Append(' ');
                    }

                    current.Append(word);
                    break;
                }

                if (hasContent)
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(indent);
                    continue;
                }

                // Word longer than a whole row: hard split.
                var take = width - current.Length;
                current.Append(word, 0, take);
                lines.Add(current.ToString());
                current.Clear().Append(indent);
                word = word[take..];
            }
        }

        if (current.Length > indent.Length)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static string Rule(char c, int width) => new(c, width);

    private static string Minus(decimal amount) => "-" + Vnd.FormatNumber(amount);

    private static string Quantity(decimal quantity) => quantity.ToString("#,0.###", Vi);

    private static string Percent(decimal rate) => rate.ToString("0.##", Vi) + "%";

    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace('\t', ' ').Replace("\r", string.Empty).Replace("\n", " ").Normalize(NormalizationForm.FormC);
}
