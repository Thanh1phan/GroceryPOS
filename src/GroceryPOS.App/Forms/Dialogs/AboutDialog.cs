using System.Diagnostics;
using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Infrastructure.Hosting;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Thông tin phần mềm (user menu): version, runtime and where program, data, database, logs and backups live, with
/// shortcuts to open the data / log folders and "Sao chép thông tin" for support. Esc closes.
/// </summary>
internal sealed class AboutDialog : Form
{
    private const int LabelWidth = 170;
    private const int ValueWidth = 470;

    private readonly SupportInfo _info;

    public AboutDialog(SupportInfo info)
    {
        _info = info;
        Text = "Thông tin phần mềm";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var title = new Label { Text = "GroceryPOS", AutoSize = true, Location = new Point(24, 20), Font = ThemeFonts.Get(FontRole.Display, FontStyle.Bold) };
        var version = new ModernBadge { Location = new Point(220, 32) };
        version.Set($"Phiên bản {info.Version}", BadgeKind.Primary);
        var subtitle = new ToneLabel
        {
            Text = "Phần mềm quản lý bán hàng cho cửa hàng tạp hóa",
            AutoSize = true,
            Location = new Point(24, 68),
            Tone = LabelTone.Muted,
        };
        Controls.AddRange([title, version, subtitle]);

        var top = 108;
        foreach (var line in info.Lines.Skip(1)) // version is in the badge
        {
            var label = new ToneLabel { Text = line.Label, Location = new Point(24, top), Size = new Size(LabelWidth, 22), Tone = LabelTone.Muted };
            var value = new TextBox
            {
                Text = line.Value,
                Location = new Point(24 + LabelWidth, top),
                Width = ValueWidth,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                TabStop = false,
            };
            Controls.AddRange([label, value]);
            top += 30;
        }

        var hint = new ToneLabel
        {
            Text = "Dữ liệu nằm ngoài thư mục chương trình nên cài đặt bản mới không làm mất dữ liệu. " +
                   "Thay đổi cấu hình riêng của cửa hàng (ví dụ chuỗi kết nối SQL Server) trong " + AppDataLocation.LocalSettingsFileName + ".",
            Location = new Point(24, top + 6),
            Size = new Size(LabelWidth + ValueWidth, 44),
            Tone = LabelTone.Muted,
            Font = ThemeFonts.Get(FontRole.Caption),
        };
        top += 60;

        var openData = Button("Mở thư mục dữ liệu", Glyphs.Document, ButtonVariant.Secondary, 24, top, 190);
        openData.Click += (_, _) => OpenFolder(info.Location.DataDirectory);
        var openLogs = Button("Mở thư mục log", Glyphs.List, ButtonVariant.Secondary, 222, top, 160);
        openLogs.Click += (_, _) => OpenFolder(info.LogsDirectory);
        var copy = Button("Sao chép thông tin", Glyphs.Copy, ButtonVariant.Ghost, 390, top, 170);
        copy.Click += (_, _) => CopyInfo();
        var close = Button("Đóng", null, ButtonVariant.Primary, 568, top, 96);
        close.DialogResult = DialogResult.Cancel;

        Controls.AddRange([hint, openData, openLogs, copy, close]);
        CancelButton = close;
        AcceptButton = close;
        ClientSize = new Size(24 + LabelWidth + ValueWidth + 24, top + 40 + 24);
        Load += (_, _) =>
        {
            ThemeManager.Track(this);
            close.Focus();
        };
    }

    private static ModernButton Button(string text, string? glyph, ButtonVariant variant, int x, int y, int width) =>
        new() { Text = text, IconGlyph = glyph, Variant = variant, Location = new Point(x, y), Size = new Size(width, 40) };

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Toaster.Warning(this, $"Không mở được thư mục: {ex.Message}", Text);
        }
    }

    private void CopyInfo()
    {
        try
        {
            Clipboard.SetText(_info.ToClipboardText());
            Toaster.Success(this, "Đã sao chép thông tin phần mềm – dán vào tin nhắn gửi bộ phận hỗ trợ.", Text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            Toaster.Warning(this, $"Không sao chép được: {ex.Message}", Text);
        }
    }
}
