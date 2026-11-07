using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using GroceryPOS.Application.Reports.Export;

namespace GroceryPOS.App.Printing;

/// <summary>
/// Prints prepared report sheets on A4 (portrait or landscape): title block with store / period / generated-by lines,
/// repeated column headers, zebra rows, bold totals row and a "Trang x/y" footer. Pagination – scale to fit, column
/// bands with repeated key columns, totals placement – comes from the tested <see cref="ReportPrintPlan"/>; this class
/// only draws. Each sheet starts on a new page and pages are numbered across the whole document.
/// </summary>
internal sealed class ReportPrintDocument : PrintDocument
{
    private const string FontFamilyName = "Segoe UI";
    private const int MarginHundredths = 47; // 1.2 cm
    private const float BaseFontSize = 9f;
    private const float CellPadding = 5f;

    private static readonly Color Ink = Color.FromArgb(0x11, 0x18, 0x27);
    private static readonly Color Muted = Color.FromArgb(0x4B, 0x55, 0x63);
    private static readonly Color Rule = Color.FromArgb(0xCB, 0xD5, 0xE1);
    private static readonly Color HeaderFill = Color.FromArgb(0xE2, 0xE8, 0xF0);
    private static readonly Color ZebraFill = Color.FromArgb(0xF8, 0xFA, 0xFC);
    private static readonly Color TotalsFill = Color.FromArgb(0xEE, 0xF2, 0xF7);

    private readonly ReportDocument _report;
    private readonly List<(ReportPrintPlan Plan, ReportPrintPage Page)> _pages = [];
    private int _next;
    private int _last;

    public ReportPrintDocument(ReportDocument report, bool landscape)
    {
        _report = report;
        Landscape = landscape;
        DocumentName = report.FileStem;
        OriginAtMargins = false;

        DefaultPageSettings.Landscape = landscape;
        DefaultPageSettings.Margins = new Margins(MarginHundredths, MarginHundredths, MarginHundredths, MarginHundredths);
        TrySelectA4();
        Paginate();
    }

    public bool Landscape { get; }

    public int PageCount => _pages.Count;

    /// <summary>Scale of the narrowest sheet – shown in the preview status line.</summary>
    public double MinScale => _pages.Count == 0 ? 1 : _pages.Min(p => p.Plan.Scale);

    /// <summary>True when some sheet had to be split into column bands.</summary>
    public bool HasBands => _pages.Any(p => p.Page.BandCount > 1);

    /// <summary>Same report, same orientation, new instance – for printing to another printer or a PDF file.</summary>
    public ReportPrintDocument Clone() => new(_report, Landscape);

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _next = 0;
        _last = _pages.Count - 1;
        if (e.PrintAction != PrintAction.PrintToPreview && PrinterSettings.PrintRange == PrintRange.SomePages)
        {
            // "Trang 2 – 4" from the printer dialog: PrintDocument leaves page selection to us.
            _next = Math.Clamp(PrinterSettings.FromPage - 1, 0, Math.Max(0, _pages.Count - 1));
            _last = Math.Clamp(PrinterSettings.ToPage - 1, _next, Math.Max(0, _pages.Count - 1));
        }
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        var g = e.Graphics!;
        g.PageUnit = GraphicsUnit.Display;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.None;

        if (_pages.Count == 0)
        {
            e.HasMorePages = false;
            return;
        }

        var (plan, page) = _pages[_next];
        DrawPage(g, e.MarginBounds, plan, page, _next + 1);
        _next++;
        e.HasMorePages = _next <= _last;
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        base.OnEndPrint(e);
        _next = 0;
    }

    private void TrySelectA4()
    {
        try
        {
            foreach (PaperSize size in PrinterSettings.PaperSizes)
            {
                if (size.Kind == PaperKind.A4)
                {
                    DefaultPageSettings.PaperSize = size;
                    return;
                }
            }
        }
        catch (InvalidPrinterException)
        {
            // No printer installed: keep the defaults, the preview reports the problem.
        }
    }

    private void Paginate()
    {
        double width;
        double height;
        try
        {
            var settings = DefaultPageSettings;
            width = settings.Bounds.Width - settings.Margins.Left - settings.Margins.Right;
            height = settings.Bounds.Height - settings.Margins.Top - settings.Margins.Bottom;
        }
        catch (InvalidPrinterException)
        {
            width = 0;
            height = 0;
        }

        foreach (var sheet in _report.Sheets)
        {
            var metrics = ReportPageMetrics.A4(Landscape, sheet.Subtitles.Count);
            if (width > 100 && height > 100)
            {
                metrics = metrics with { PrintableWidth = width, PrintableHeight = height };
            }

            var plan = ReportPrintPlan.Create(sheet, metrics);
            foreach (var page in plan.Pages)
            {
                _pages.Add((plan, page));
            }
        }
    }

    private void DrawPage(Graphics g, Rectangle bounds, ReportPrintPlan plan, ReportPrintPage page, int pageNumber)
    {
        var sheet = plan.Sheet;
        var metrics = ReportPageMetrics.A4(Landscape, sheet.Subtitles.Count);
        var scale = (float)plan.Scale;
        float x = bounds.Left;
        float y = bounds.Top;
        var bandCaption = plan.BandCaption(page);

        using var muted = new SolidBrush(Muted);
        using var ink = new SolidBrush(Ink);
        using var rulePen = new Pen(Rule, 1f);
        using var left = Format(StringAlignment.Near);
        using var right = Format(StringAlignment.Far);

        // ---- Title block or continuation header --------------------------------------------------------
        if (page.ShowTitleBlock)
        {
            using var title = new Font(FontFamilyName, 14f, FontStyle.Bold, GraphicsUnit.Point);
            using var subtitle = new Font(FontFamilyName, 9f, FontStyle.Regular, GraphicsUnit.Point);
            g.DrawString(sheet.Title, title, ink, new RectangleF(x, y, bounds.Width, 32), left);
            if (bandCaption is not null)
            {
                g.DrawString(bandCaption, subtitle, muted, new RectangleF(x, y + 6, bounds.Width, 20), right);
            }

            var lineY = y + 34;
            foreach (var line in sheet.Subtitles)
            {
                g.DrawString(line, subtitle, muted, new RectangleF(x, lineY, bounds.Width, 16), left);
                lineY += 16;
            }

            y += (float)metrics.TitleBlockHeight;
        }
        else
        {
            using var small = new Font(FontFamilyName, 8.5f, FontStyle.Bold, GraphicsUnit.Point);
            var heading = $"{sheet.Title} – {_report.PeriodLine}";
            g.DrawString(heading, small, muted, new RectangleF(x, y, bounds.Width, 18), left);
            if (bandCaption is not null)
            {
                g.DrawString(bandCaption, small, muted, new RectangleF(x, y, bounds.Width, 18), right);
            }

            y += (float)metrics.PageHeaderHeight;
        }

        // ---- Table ------------------------------------------------------------------------------------------
        var fontSize = Math.Max(5.5f, BaseFontSize * scale);
        using var cellFont = new Font(FontFamilyName, fontSize, FontStyle.Regular, GraphicsUnit.Point);
        using var boldFont = new Font(FontFamilyName, fontSize, FontStyle.Bold, GraphicsUnit.Point);
        var rowHeight = (float)(metrics.RowHeight * scale);
        var headerHeight = (float)(metrics.HeaderRowHeight * scale);
        var padding = CellPadding * scale;
        var tableWidth = (float)plan.TableWidth(page);

        // Header row (wraps to two lines).
        using (var fill = new SolidBrush(HeaderFill))
        {
            g.FillRectangle(fill, x, y, tableWidth, headerHeight);
        }

        var cx = x;
        foreach (var c in page.Columns)
        {
            var w = (float)plan.ColumnWidths[c];
            var column = sheet.Columns[c];
            using var header = Format(Align(column.Kind), wrap: true);
            g.DrawString(column.Header, boldFont, ink, new RectangleF(cx + padding, y, Math.Max(1, w - (2 * padding)), headerHeight), header);
            cx += w;
        }

        var tableTop = y;
        y += headerHeight;

        // Body rows.
        for (var i = 0; i < page.RowCount; i++)
        {
            var rowIndex = page.FirstRow + i;
            if (rowIndex % 2 == 1)
            {
                using var zebra = new SolidBrush(ZebraFill);
                g.FillRectangle(zebra, x, y, tableWidth, rowHeight);
            }

            DrawRow(g, plan, page, sheet.Rows[rowIndex], x, y, rowHeight, padding, cellFont, ink);
            g.DrawLine(rulePen, x, y + rowHeight, x + tableWidth, y + rowHeight);
            y += rowHeight;
        }

        if (page.RowCount == 0 && sheet.Rows.Count == 0)
        {
            using var italic = new Font(FontFamilyName, fontSize, FontStyle.Italic, GraphicsUnit.Point);
            g.DrawString("Không có dữ liệu trong kỳ này.", italic, muted, new RectangleF(x + padding, y, tableWidth, rowHeight), left);
            y += rowHeight;
        }

        if (page.ShowTotals && sheet.Totals is not null)
        {
            using (var fill = new SolidBrush(TotalsFill))
            {
                g.FillRectangle(fill, x, y, tableWidth, rowHeight);
            }

            using var strong = new Pen(Ink, 1.5f);
            g.DrawLine(strong, x, y, x + tableWidth, y);
            DrawRow(g, plan, page, sheet.Totals, x, y, rowHeight, padding, boldFont, ink);
            y += rowHeight;
        }

        // Outer frame and column separators.
        g.DrawRectangle(rulePen, x, tableTop, tableWidth, y - tableTop);
        cx = x;
        for (var i = 0; i < page.Columns.Count - 1; i++)
        {
            cx += (float)plan.ColumnWidths[page.Columns[i]];
            g.DrawLine(rulePen, cx, tableTop, cx, y);
        }

        // ---- Footer -----------------------------------------------------------------------------------------
        using var footerFont = new Font(FontFamilyName, 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        var footerY = bounds.Bottom - (float)metrics.FooterHeight + 8;
        g.DrawLine(rulePen, bounds.Left, footerY - 4, bounds.Right, footerY - 4);
        var printed = _report.GeneratedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        g.DrawString($"GroceryPOS · {_report.StoreName} · lập lúc {printed}", footerFont, muted, new RectangleF(bounds.Left, footerY, bounds.Width, 16), left);
        g.DrawString(ReportPrintPlan.PageLabel(pageNumber, _pages.Count), footerFont, muted, new RectangleF(bounds.Left, footerY, bounds.Width, 16), right);
    }

    private static void DrawRow(
        Graphics g, ReportPrintPlan plan, ReportPrintPage page, IReadOnlyList<object?> cells,
        float x, float y, float height, float padding, Font font, Brush brush)
    {
        var cx = x;
        foreach (var c in page.Columns)
        {
            var w = (float)plan.ColumnWidths[c];
            var kind = plan.Sheet.Columns[c].Kind;
            var value = cells[c];
            var text = ReportCellText.Format(value, kind);
            if (text.Length > 0)
            {
                // Labels in numeric columns (e.g. "Tổng cộng" under Ngày) read better left-aligned.
                var alignment = value is string ? StringAlignment.Near : Align(kind);
                using var format = Format(alignment);
                g.DrawString(text, font, brush, new RectangleF(cx + padding, y, Math.Max(1, w - (2 * padding)), height), format);
            }

            cx += w;
        }
    }

    private static StringAlignment Align(ReportColumnKind kind) => ReportCellText.AlignmentOf(kind) switch
    {
        ReportCellAlignment.Right => StringAlignment.Far,
        ReportCellAlignment.Center => StringAlignment.Center,
        _ => StringAlignment.Near,
    };

    private static StringFormat Format(StringAlignment alignment, bool wrap = false)
    {
        var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Center,
            Trimming = wrap ? StringTrimming.EllipsisWord : StringTrimming.EllipsisCharacter,
        };
        if (!wrap)
        {
            format.FormatFlags |= StringFormatFlags.NoWrap;
        }

        return format;
    }
}
