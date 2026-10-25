using System.Drawing.Printing;
using GroceryPOS.Application.Sales.Receipts;

namespace GroceryPOS.App.Printing;

/// <summary>
/// Prints the fixed-width lines from <see cref="ReceiptFormatter"/> on thermal roll paper (80 or 58 mm).
/// The page is as tall as the receipt, so roll printers cut once; on sheet printers long receipts continue
/// on the next page. The monospace font is sized so that exactly <c>columns</c> characters fill the printable width.
/// </summary>
internal sealed class ReceiptPrintDocument : PrintDocument
{
    private const string FontFamilyName = "Consolas";

    /// <summary>Advance width of one Consolas glyph as a fraction of the em size.</summary>
    private const float GlyphWidthEm = 0.5498f;

    private const int TopMargin = 8;     // 1/100 inch
    private const int BottomMargin = 40; // room for the auto-cutter

    private readonly IReadOnlyList<ReceiptLine> _lines;
    private readonly int _columns;
    private readonly float _printableWidth;
    private Font? _regular;
    private Font? _bold;
    private int _nextLine;

    public ReceiptPrintDocument(IReadOnlyList<ReceiptLine> lines, int paperWidthMm, string documentName)
    {
        _lines = lines;
        _columns = lines.Count == 0 ? ReceiptFormatter.ColumnsFor(paperWidthMm) : lines.Max(l => l.Text.Length);
        DocumentName = documentName;

        // 80 mm rolls print 72 mm wide, 58 mm rolls 48 mm.
        var paperWidth = MmToHundredths(paperWidthMm);
        _printableWidth = MmToHundredths(paperWidthMm <= 58 ? 48 : 72);
        var side = (int)Math.Round((paperWidth - _printableWidth) / 2f);

        FontSize = _printableWidth / 100f * 72f / (_columns * GlyphWidthEm);
        using var probe = CreateFont(FontStyle.Regular);
        LineHeight = probe.GetHeight(100);

        var height = (int)Math.Ceiling(TopMargin + BottomMargin + (_lines.Count * LineHeight));
        DefaultPageSettings.PaperSize = new PaperSize($"Hóa đơn {paperWidthMm}mm", (int)Math.Round(paperWidth), height);
        DefaultPageSettings.Margins = new Margins(side, side, TopMargin, BottomMargin);
        DefaultPageSettings.Landscape = false;
        OriginAtMargins = false;
    }

    /// <summary>Font size in points that fits the receipt width.</summary>
    public float FontSize { get; }

    /// <summary>Line height in 1/100 inch.</summary>
    public float LineHeight { get; }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _nextLine = 0;
        _regular ??= CreateFont(FontStyle.Regular);
        _bold ??= CreateFont(FontStyle.Bold);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        var graphics = e.Graphics!;
        graphics.PageUnit = GraphicsUnit.Display;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var bounds = e.MarginBounds;
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        // Sheet printers ignore the custom width: scale the text down if the page is narrower than the receipt.
        var regular = _regular!;
        var bold = _bold!;
        var measured = graphics.MeasureString(new string('W', _columns), regular, int.MaxValue, format).Width;
        var scale = measured > bounds.Width ? bounds.Width / measured : 1f;
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.Left, bounds.Top);
        graphics.ScaleTransform(scale, scale);

        var y = 0f;
        var pageHeight = bounds.Height / scale;
        while (_nextLine < _lines.Count && y + LineHeight <= pageHeight + 0.5f)
        {
            var line = _lines[_nextLine++];
            graphics.DrawString(line.Text, line.IsBold ? bold : regular, Brushes.Black, 0, y, format);
            y += LineHeight;
        }

        graphics.Restore(state);
        e.HasMorePages = _nextLine < _lines.Count;
    }

    protected override void OnEndPrint(PrintEventArgs e)
    {
        base.OnEndPrint(e);
        _nextLine = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _regular?.Dispose();
            _bold?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Font CreateFont(FontStyle style) => new(FontFamilyName, FontSize, style, GraphicsUnit.Point);

    private static float MmToHundredths(int mm) => mm / 25.4f * 100f;
}
