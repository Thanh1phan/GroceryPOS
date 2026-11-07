using System.ComponentModel;
using System.Drawing.Printing;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.App.Printing;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Serilog;

namespace GroceryPOS.App.Forms.Reports;

/// <summary>
/// Xem trước bản in báo cáo: A4 pages from <see cref="ReportPrintDocument"/> with page navigation (PgUp/PgDn,
/// Home/End), zoom (Ctrl +/−, Ctrl+0 vừa trang), portrait/landscape switch, In (Ctrl+P) to the default printer,
/// Chọn máy in… and Xuất PDF (Ctrl+S) through the Windows "Microsoft Print to PDF" printer. Every successful print
/// or PDF is reported through <c>onOutput</c> so the screen can audit it.
/// </summary>
internal sealed class ReportPrintPreviewForm : Form
{
    private const string Caption = "In báo cáo";
    private const string PdfPrinterName = "Microsoft Print to PDF";

    private readonly ReportDocument _report;
    private readonly Func<ReportOutput, string?, Task>? _onOutput;
    private readonly PrintPreviewControl _preview;
    private readonly ComboBox _orientation;
    private readonly Label _pageLabel;
    private readonly Label _status;
    private ReportPrintDocument? _document;

    public ReportPrintPreviewForm(ReportDocument report, Func<ReportOutput, string?, Task>? onOutput = null)
    {
        _report = report;
        _onOutput = onOutput;

        Text = $"Xem trước bản in – {report.Names}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(1100, 800);
        MinimumSize = new Size(760, 520);

        var print = Button("In (Ctrl+P)", Glyphs.Print, ButtonVariant.Primary, 130);
        print.Click += (_, _) => _ = PrintAsync(choosePrinter: false);
        var choose = Button("Chọn máy in…", null, ButtonVariant.Secondary, 130);
        choose.Click += (_, _) => _ = PrintAsync(choosePrinter: true);
        var pdf = Button("Xuất PDF (Ctrl+S)", Glyphs.Document, ButtonVariant.Secondary, 160);
        pdf.Click += (_, _) => _ = SavePdfAsync();

        _orientation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(8, 8, 8, 0) };
        _orientation.Items.AddRange(["A4 ngang", "A4 dọc"]);
        _orientation.SelectedIndex = 0;
        _orientation.SelectedIndexChanged += (_, _) => Rebuild();

        var previous = Button(string.Empty, Glyphs.ChevronLeft, ButtonVariant.Ghost, 40);
        previous.Click += (_, _) => GoTo(_preview!.StartPage - 1);
        _pageLabel = new Label { AutoSize = false, Width = 96, Height = 38, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0) };
        var next = Button(string.Empty, Glyphs.ChevronRight, ButtonVariant.Ghost, 40);
        next.Click += (_, _) => GoTo(_preview!.StartPage + 1);

        var fit = Button("Vừa trang", null, ButtonVariant.Ghost, 100);
        fit.Click += (_, _) => _preview!.AutoZoom = true;

        var close = Button("Đóng (Esc)", null, ButtonVariant.Ghost, 110);
        close.DialogResult = DialogResult.Cancel;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(12, 8, 12, 8),
            WrapContents = false,
        };
        toolbar.Controls.AddRange([print, choose, pdf, _orientation, previous, _pageLabel, next, fit, close]);

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
            AutoZoom = true,
            UseAntiAlias = true,
            BackColor = Color.FromArgb(0xE5, 0xE7, 0xEB),
        };
        _preview.StartPageChanged += (_, _) => UpdatePageLabel();

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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.P:
                _ = PrintAsync(choosePrinter: false);
                return true;
            case Keys.Control | Keys.S:
                _ = SavePdfAsync();
                return true;
            case Keys.PageDown or Keys.Right when ActiveControl is not ComboBox:
                GoTo(_preview.StartPage + 1);
                return true;
            case Keys.PageUp or Keys.Left when ActiveControl is not ComboBox:
                GoTo(_preview.StartPage - 1);
                return true;
            case Keys.Home:
                GoTo(0);
                return true;
            case Keys.End:
                GoTo(int.MaxValue);
                return true;
            case (Keys.Control | Keys.Add) or (Keys.Control | Keys.Oemplus):
                _preview.Zoom = Math.Min(3, _preview.Zoom + 0.15);
                return true;
            case (Keys.Control | Keys.Subtract) or (Keys.Control | Keys.OemMinus):
                _preview.Zoom = Math.Max(0.3, _preview.Zoom - 0.15);
                return true;
            case Keys.Control | Keys.D0:
                _preview.AutoZoom = true;
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static ModernButton Button(string text, string? glyph, ButtonVariant variant, int width) => new()
    {
        Text = text,
        IconGlyph = glyph,
        Variant = variant,
        Size = new Size(width, 38),
        Margin = new Padding(0, 0, 8, 0),
    };

    private bool Landscape => _orientation.SelectedIndex == 0;

    private void Rebuild()
    {
        var old = _document;
        try
        {
            _document = new ReportPrintDocument(_report, Landscape);
        }
        catch (InvalidPrinterException ex)
        {
            Log.Warning(ex, "Không tạo được bản in báo cáo");
            _status.Text = "Máy tính chưa cài máy in nào – hãy cài máy in hoặc \"Microsoft Print to PDF\".";
            return;
        }

        _preview.Document = _document;
        _preview.StartPage = 0;
        _preview.InvalidatePreview();
        old?.Dispose();

        var parts = new List<string>
        {
            $"{_document.PageCount} trang",
            Landscape ? "A4 ngang" : "A4 dọc",
            $"{_report.Sheets.Count} báo cáo",
        };
        if (_document.MinScale < 0.999)
        {
            parts.Add($"thu nhỏ còn {_document.MinScale:P0}");
        }

        if (_document.HasBands)
        {
            parts.Add("bảng rộng được chia thành nhiều phần cột" + (Landscape ? string.Empty : " – thử A4 ngang"));
        }

        parts.Add("PgUp/PgDn chuyển trang · Ctrl +/− phóng to");
        _status.Text = string.Join(" · ", parts);
        UpdatePageLabel();
    }

    private void GoTo(int page)
    {
        if (_document is null || _document.PageCount == 0)
        {
            return;
        }

        _preview.StartPage = Math.Clamp(page, 0, _document.PageCount - 1);
        UpdatePageLabel();
    }

    private void UpdatePageLabel()
    {
        var count = _document?.PageCount ?? 0;
        _pageLabel.Text = count == 0 ? "–" : ReportPrintPlan.PageLabel(Math.Min(_preview.StartPage + 1, count), count);
    }

    private async Task PrintAsync(bool choosePrinter)
    {
        if (_document is null)
        {
            return;
        }

        using var copy = _document.Clone();
        try
        {
            if (choosePrinter)
            {
                using var dialog = new PrintDialog { Document = copy, UseEXDialog = true, AllowSomePages = true };
                copy.PrinterSettings.MinimumPage = 1;
                copy.PrinterSettings.MaximumPage = _document.PageCount;
                copy.PrinterSettings.FromPage = 1;
                copy.PrinterSettings.ToPage = _document.PageCount;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            copy.Print();
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            Log.Warning(ex, "Không in được báo cáo {Report}", _report.FileStem);
            ModernDialog.Warning(this, "Không in được báo cáo. Kiểm tra máy in hoặc chọn máy in khác.", Caption, ex.Message);
            return;
        }

        var printer = copy.PrinterSettings.PrinterName;
        Toaster.Success(this, $"Đã gửi {_document.PageCount} trang tới {printer}.", Caption);
        if (_onOutput is not null)
        {
            await _onOutput(ReportOutput.Printer, printer);
        }
    }

    private async Task SavePdfAsync()
    {
        if (_document is null)
        {
            return;
        }

        var pdfPrinter = FindPdfPrinter();
        if (pdfPrinter is null)
        {
            ModernDialog.Warning(
                this,
                "Máy tính chưa có máy in \"Microsoft Print to PDF\". Bật nó trong Windows (Cài đặt → Máy in & máy quét, hoặc Turn Windows features on or off), hoặc dùng \"Chọn máy in…\" với một máy in PDF khác.",
                Caption);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu báo cáo PDF",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = _report.FileStem + ".pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var path = dialog.FileName;
        if (!CanWrite(path))
        {
            ModernDialog.Warning(this, "Không ghi được file PDF. Có thể file đang mở trong trình đọc PDF hoặc thư mục không cho phép ghi – hãy đóng file hoặc chọn nơi lưu khác.", Caption);
            return;
        }

        using var copy = _document.Clone();
        try
        {
            copy.PrinterSettings.PrinterName = pdfPrinter;
            copy.PrinterSettings.PrintToFile = true;
            copy.PrinterSettings.PrintFileName = path;
            copy.PrintController = new StandardPrintController();
            await LoadingOverlay.RunAsync(this, () => Task.Run(copy.Print), "Đang tạo file PDF…");
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception or IOException)
        {
            Log.Warning(ex, "Không xuất được PDF {Path}", path);
            ModernDialog.Warning(this, "Không tạo được file PDF.", Caption, ex.Message);
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        Toaster.Show(
            this,
            new ToastRequest(FeedbackKind.Success, $"Đã lưu {Path.GetFileName(path)}", Caption, ActionText: "Mở file"),
            () => ExcelFiles.Open(this, path, Caption));
        if (_onOutput is not null)
        {
            await _onOutput(ReportOutput.Pdf, Path.GetFileName(path));
        }
    }

    private static string? FindPdfPrinter()
    {
        string? fallback = null;
        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(printer, PdfPrinterName, StringComparison.OrdinalIgnoreCase))
            {
                return printer;
            }

            if (fallback is null && printer.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
            {
                fallback = printer;
            }
        }

        return fallback;
    }

    private static bool CanWrite(string path)
    {
        var existed = File.Exists(path);
        try
        {
            using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
            }

            if (!existed)
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
