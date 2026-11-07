using System.Globalization;
using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Reports.Export;

/// <summary>
/// Page geometry for <see cref="ReportPrintPlan"/>, in any one unit (the WinForms printer uses 1/100 inch).
/// Table sizes (<see cref="CharWidth"/>, row heights) are at 100 % and shrink with the plan's scale; the title block,
/// the continuation header and the footer keep their size.
/// </summary>
public sealed record ReportPageMetrics(
    double PrintableWidth,
    double PrintableHeight,
    double CharWidth,
    double RowHeight,
    double HeaderRowHeight,
    double TitleBlockHeight,
    double PageHeaderHeight,
    double FooterHeight,
    double MinScale = 0.72)
{
    /// <summary>A4 at 1/100 inch with 1.2 cm margins and a 9 pt table font.</summary>
    public static ReportPageMetrics A4(bool landscape, int subtitleCount)
    {
        const double shortSide = 827 - (2 * 47);
        const double longSide = 1169 - (2 * 47);
        return new ReportPageMetrics(
            PrintableWidth: landscape ? longSide : shortSide,
            PrintableHeight: landscape ? shortSide : longSide,
            CharWidth: 6.4,
            RowHeight: 19,
            HeaderRowHeight: 34,
            TitleBlockHeight: 34 + (Math.Max(0, subtitleCount) * 16) + 10,
            PageHeaderHeight: 26,
            FooterHeight: 24);
    }
}

/// <summary>One printed page of a sheet: which columns (always including the key columns) and which rows.</summary>
public sealed record ReportPrintPage(
    int Band,
    int BandCount,
    IReadOnlyList<int> Columns,
    int FirstRow,
    int RowCount,
    bool ShowTotals,
    bool ShowTitleBlock);

/// <summary>
/// Paginates a <see cref="ReportSheet"/> for paper (Báo cáo → In / PDF). The table is shrunk to the page width down
/// to <see cref="ReportPageMetrics.MinScale"/>; wider sheets are split into column bands that each repeat the key
/// columns (date, rank, code, name), printed band after band ("down, then over" like Excel). Every band starts with
/// the title block, later pages get a one-line header and every page repeats the column headers. The totals row
/// follows the last data row of each band, on a new page when the last one is full. UI-free so the maths is tested.
/// </summary>
public sealed class ReportPrintPlan
{
    public const double DefaultTextWidth = 14;
    public const double DefaultNumberWidth = 12;

    private ReportPrintPlan(ReportSheet sheet, double scale, IReadOnlyList<double> widths, IReadOnlyList<int> keyColumns, IReadOnlyList<ReportPrintPage> pages)
    {
        Sheet = sheet;
        Scale = scale;
        ColumnWidths = widths;
        KeyColumns = keyColumns;
        Pages = pages;
    }

    public ReportSheet Sheet { get; }

    /// <summary>Table scale (≤ 1): font size and row heights are multiplied by it.</summary>
    public double Scale { get; }

    /// <summary>Printed width of every sheet column, already scaled, in the metrics unit.</summary>
    public IReadOnlyList<double> ColumnWidths { get; }

    /// <summary>Columns repeated on every band; empty when the sheet fits in one band.</summary>
    public IReadOnlyList<int> KeyColumns { get; }

    public IReadOnlyList<ReportPrintPage> Pages { get; }

    public int BandCount => Pages.Count == 0 ? 0 : Pages[0].BandCount;

    public static ReportPrintPlan Create(ReportSheet sheet, ReportPageMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(metrics);
        if (metrics.PrintableWidth <= 0 || metrics.PrintableHeight <= 0 || metrics.CharWidth <= 0 || metrics.RowHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Khổ giấy không hợp lệ.");
        }

        var units = sheet.Columns.Select(UnitWidth).ToArray();
        var natural = units.Sum() * metrics.CharWidth;
        var minScale = Math.Clamp(metrics.MinScale, 0.1, 1);
        var scale = Math.Min(1, metrics.PrintableWidth / natural);

        List<IReadOnlyList<int>> bands;
        IReadOnlyList<int> keys;
        if (scale >= minScale)
        {
            bands = [Enumerable.Range(0, units.Length).ToList()];
            keys = [];
        }
        else
        {
            scale = minScale;
            var available = metrics.PrintableWidth / (metrics.CharWidth * scale);
            keys = KeyColumnsOf(sheet, units, available * 0.45);
            bands = SplitIntoBands(units, keys, available);
        }

        var widths = units.Select(u => u * metrics.CharWidth * scale).ToList();
        var pages = new List<ReportPrintPage>();
        for (var band = 0; band < bands.Count; band++)
        {
            Paginate(sheet, metrics, scale, band, bands, pages);
        }

        return new ReportPrintPlan(sheet, scale, widths, keys, pages);
    }

    /// <summary>"Trang 2/5".</summary>
    public static string PageLabel(int pageNumber, int pageCount) => $"Trang {pageNumber}/{pageCount}";

    /// <summary>"Phần 2/3: Doanh thu thuần → Thẻ" for split sheets, null when the sheet prints in one band.</summary>
    public string? BandCaption(ReportPrintPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.BandCount <= 1)
        {
            return null;
        }

        var own = page.Columns.Where(c => !KeyColumns.Contains(c)).ToList();
        var first = Sheet.Columns[own[0]].Header;
        var last = Sheet.Columns[own[^1]].Header;
        var range = own.Count == 1 ? first : $"{first} → {last}";
        return $"Phần {page.Band + 1}/{page.BandCount}: {range}";
    }

    /// <summary>Printed width of one page's table.</summary>
    public double TableWidth(ReportPrintPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.Columns.Sum(c => ColumnWidths[c]);
    }

    public static double UnitWidth(ReportColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (column.Width > 0)
        {
            return column.Width;
        }

        return column.Kind == ReportColumnKind.Text ? DefaultTextWidth : DefaultNumberWidth;
    }

    private static void Paginate(
        ReportSheet sheet, ReportPageMetrics m, double scale, int band, IReadOnlyList<IReadOnlyList<int>> bands, List<ReportPrintPage> pages)
    {
        var rowHeight = m.RowHeight * scale;
        var tableTop = m.HeaderRowHeight * scale;
        int Capacity(bool title)
        {
            var body = m.PrintableHeight - (title ? m.TitleBlockHeight : m.PageHeaderHeight) - m.FooterHeight - tableTop;
            return Math.Max(1, (int)Math.Floor((body + 0.001) / rowHeight));
        }

        var hasTotals = sheet.Totals is not null;
        var next = 0;
        var title = true;
        while (true)
        {
            var capacity = Capacity(title);
            var take = Math.Min(capacity, sheet.Rows.Count - next);
            var last = next + take >= sheet.Rows.Count;
            var totalsHere = last && hasTotals && take < capacity;
            pages.Add(new ReportPrintPage(band, bands.Count, bands[band], next, take, totalsHere, title));
            next += take;
            title = false;

            if (last && (!hasTotals || totalsHere))
            {
                return;
            }

            if (last)
            {
                // The last page was full: the totals row goes on its own page.
                pages.Add(new ReportPrintPage(band, bands.Count, bands[band], next, 0, true, false));
                return;
            }
        }
    }

    private static List<int> KeyColumnsOf(ReportSheet sheet, double[] units, double maxWidth)
    {
        var keys = new List<int>();
        var width = 0d;
        for (var i = 0; i < sheet.Columns.Count - 1; i++)
        {
            var kind = sheet.Columns[i].Kind;
            var isKey = kind is ReportColumnKind.Text or ReportColumnKind.Date || (i == 0 && kind == ReportColumnKind.Integer);
            if (!isKey || width + units[i] > maxWidth)
            {
                break;
            }

            keys.Add(i);
            width += units[i];
        }

        if (keys.Count == 0)
        {
            keys.Add(0);
        }

        return keys;
    }

    private static List<IReadOnlyList<int>> SplitIntoBands(double[] units, IReadOnlyList<int> keys, double available)
    {
        var keyWidth = keys.Sum(k => units[k]);
        var bands = new List<IReadOnlyList<int>>();
        var current = new List<int>(keys);
        var width = keyWidth;
        for (var i = 0; i < units.Length; i++)
        {
            if (keys.Contains(i))
            {
                continue;
            }

            if (current.Count > keys.Count && width + units[i] > available + 0.001)
            {
                bands.Add(current);
                current = new List<int>(keys);
                width = keyWidth;
            }

            current.Add(i);
            width += units[i];
        }

        if (current.Count > keys.Count || bands.Count == 0)
        {
            bands.Add(current);
        }

        return bands;
    }
}

/// <summary>Cell text and alignment for printed reports (vi-VN: 1.250.000, 12,5 %, 01/11/2026).</summary>
public static class ReportCellText
{
    public static string Format(object? value, ReportColumnKind kind)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case DateOnly date:
                return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            case DateTime dateTime:
                return dateTime.ToString(kind == ReportColumnKind.Date ? "dd/MM/yyyy" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            case IConvertible when IsNumber(value):
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return kind switch
                {
                    ReportColumnKind.Money => Vnd.FormatNumber(number),
                    ReportColumnKind.Integer => number.ToString("#,0", Vnd.Culture),
                    ReportColumnKind.Quantity => number.ToString("#,0.###", Vnd.Culture),
                    ReportColumnKind.Percent => number.ToString("0.0", Vnd.Culture) + " %",
                    _ => number.ToString("#,0.###", Vnd.Culture),
                };
            }

            default:
                return Convert.ToString(value, Vnd.Culture) ?? string.Empty;
        }
    }

    /// <summary>Numbers right, dates centred, text left – for the column and its header.</summary>
    public static ReportCellAlignment AlignmentOf(ReportColumnKind kind) => kind switch
    {
        ReportColumnKind.Text => ReportCellAlignment.Left,
        ReportColumnKind.Date => ReportCellAlignment.Center,
        _ => ReportCellAlignment.Right,
    };

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

public enum ReportCellAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}
