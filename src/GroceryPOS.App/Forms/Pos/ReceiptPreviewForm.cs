using System.Drawing.Printing;
using GroceryPOS.App.Printing;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Serilog;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Print preview of a receipt (Xem trước hóa đơn) or another roll printout such as a shift report. Enter / Ctrl+P prints to the default printer,
/// "Chọn máy in" opens the printer dialog, the paper switch re-lays the receipt for 80 or 58 mm rolls, Esc closes.
/// </summary>
internal sealed class ReceiptPreviewForm : Form
{
    private readonly Func<int, IReadOnlyList<ReceiptLine>> _layout;
    private readonly string _documentName;
    private readonly PrintPreviewControl _preview;
    private readonly ComboBox _paper;
    private readonly Label _status;
    private ReceiptPrintDocument? _document;

    public ReceiptPreviewForm(ReceiptData receipt, int paperWidthMm)
        : this(
            columns => ReceiptFormatter.Format(receipt, columns),
            receipt.IsReprint ? $"In lại hóa đơn {receipt.Code}" : $"Hóa đơn {receipt.Code}",
            $"Hóa đơn {receipt.Code}",
            paperWidthMm)
    {
    }

    /// <summary>Preview of any fixed-width printout (e.g. a shift report); <paramref name="layout"/> lays it out for a column count.</summary>
    public ReceiptPreviewForm(Func<int, IReadOnlyList<ReceiptLine>> layout, string title, string documentName, int paperWidthMm)
    {
        _layout = layout;
        _documentName = documentName;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(520, 760);
        MinimumSize = new Size(420, 480);

        var print = new ModernButton
        {
            Text = "In (Enter)",
            Variant = ButtonVariant.Primary,
            IconGlyph = Glyphs.Print,
            Size = new Size(130, 38),
            Margin = new Padding(0, 0, 8, 0),
        };
        print.Click += (_, _) => Print(choosePrinter: false);

        var choose = new ModernButton
        {
            Text = "Chọn máy in…",
            Variant = ButtonVariant.Secondary,
            Size = new Size(130, 38),
            Margin = new Padding(0, 0, 8, 0),
        };
        choose.Click += (_, _) => Print(choosePrinter: true);

        _paper = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 8, 8, 0) };
        _paper.Items.AddRange(["Khổ 80mm", "Khổ 58mm"]);
        _paper.SelectedIndex = paperWidthMm <= 58 ? 1 : 0;
        _paper.SelectedIndexChanged += (_, _) => Rebuild();

        var close = new ModernButton
        {
            Text = "Đóng (Esc)",
            Variant = ButtonVariant.Ghost,
            Size = new Size(110, 38),
            DialogResult = DialogResult.Cancel,
        };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(12, 8, 12, 8),
            WrapContents = false,
        };
        toolbar.Controls.AddRange([print, choose, _paper, close]);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
        };

        _preview = new PrintPreviewControl
        {
            Dock = DockStyle.Fill,
            AutoZoom = false,
            Zoom = 1.0,
            UseAntiAlias = true,
            BackColor = Color.FromArgb(0xE5, 0xE7, 0xEB),
        };

        Controls.Add(_preview);
        Controls.Add(_status);
        Controls.Add(toolbar);

        CancelButton = close;
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            Rebuild();
            print.Focus();
        };
        FormClosed += (_, _) => _document?.Dispose();
    }

    /// <summary>True once the receipt has been sent to a printer.</summary>
    public bool Printed { get; private set; }

    private int PaperWidthMm => _paper.SelectedIndex == 1 ? 58 : 80;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Enter or (Keys.Control | Keys.P))
        {
            Print(choosePrinter: false);
            return true;
        }

        if (keyData is (Keys.Control | Keys.Add) or (Keys.Control | Keys.Oemplus))
        {
            _preview.Zoom = Math.Min(3, _preview.Zoom + 0.25);
            return true;
        }

        if (keyData is (Keys.Control | Keys.Subtract) or (Keys.Control | Keys.OemMinus))
        {
            _preview.Zoom = Math.Max(0.5, _preview.Zoom - 0.25);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Rebuild()
    {
        var lines = _layout(ReceiptFormatter.ColumnsFor(PaperWidthMm));
        var old = _document;
        _document = new ReceiptPrintDocument(lines, PaperWidthMm, _documentName);
        _preview.Document = _document;
        _preview.InvalidatePreview();
        old?.Dispose();
        _status.Text = $"{lines.Count} dòng · khổ {PaperWidthMm}mm · Ctrl +/− phóng to/thu nhỏ";
    }

    private void Print(bool choosePrinter)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (choosePrinter)
            {
                using var dialog = new PrintDialog { Document = _document, UseEXDialog = true, AllowSomePages = false };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            // No "Đang in trang 1…" popup at the counter.
            _document.PrintController = new StandardPrintController();
            _document.Print();
            Printed = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception)
        {
            Log.Warning(ex, "Không in được {Document}", _documentName);
            _status.Text = "Không in được: " + ex.Message;
            MessageBox.Show(this, $"Không in được.\n{ex.Message}\n\nKiểm tra máy in hoặc chọn máy in khác.", _documentName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
