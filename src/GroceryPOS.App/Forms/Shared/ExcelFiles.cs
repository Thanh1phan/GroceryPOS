using System.ComponentModel;
using System.Diagnostics;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Feedback.Core;

namespace GroceryPOS.App.Forms.Shared;

/// <summary>
/// Saving generated Excel files: asks where to save, writes the bytes, reports a locked file or a
/// read-only folder in a dialog, and confirms with a toast whose "Mở file" link opens the workbook.
/// </summary>
internal static class ExcelFiles
{
    public const string Filter = "Excel (*.xlsx)|*.xlsx";

    /// <returns>The saved path, or null when the user cancelled or the file could not be written.</returns>
    public static async Task<string?> SaveAsync(Control owner, string caption, string dialogTitle, string suggestedName, byte[] content)
    {
        using var dialog = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = Filter,
            FileName = suggestedName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        var path = dialog.FileName;
        try
        {
            await File.WriteAllBytesAsync(path, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!owner.IsDisposed)
            {
                ModernDialog.Warning(owner, "Không lưu được file. Có thể file đang mở trong Excel hoặc thư mục không cho phép ghi – hãy đóng file hoặc chọn nơi lưu khác.", caption, ex.Message);
            }

            return null;
        }

        if (!owner.IsDisposed)
        {
            Toaster.Show(
                owner,
                new ToastRequest(FeedbackKind.Success, $"Đã lưu {Path.GetFileName(path)}", caption, ActionText: "Mở file"),
                () => Open(owner, path, caption));
        }

        return path;
    }

    /// <summary>Opens a file with its associated app; a missing association is reported as a toast.</summary>
    public static void Open(Control owner, string path, string caption)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            if (!owner.IsDisposed)
            {
                Toaster.Warning(owner, $"Không mở được {Path.GetFileName(path)}: máy chưa có ứng dụng đọc file Excel, hoặc file đã bị di chuyển.", caption);
            }
        }
    }
}
