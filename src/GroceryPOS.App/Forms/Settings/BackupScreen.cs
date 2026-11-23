using System.Diagnostics;
using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Backup;
using GroceryPOS.Presentation.Backup;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GroceryPOS.App.Forms.Settings;

/// <summary>
/// Sao lưu dữ liệu (<c>system.backup</c>): freshness badge and advice, KPI cards, the backups in the backup folder (newest
/// first) with a detail card that checks the selected file, and actions – Sao lưu ngay (Ctrl+S), Phục hồi (from the list
/// or any file, e.g. a USB stick), Sao chép ra… (copy to USB / another drive), Xóa, Mở thư mục. F5 reloads.
/// Passive MVP view – the logic lives in <see cref="BackupPresenter"/>.
/// </summary>
internal sealed class BackupScreen : BaseUserControl, IBackupView
{
    private readonly BackupPresenter _presenter;
    private readonly ModernBadge _status;
    private readonly ToneLabel _subtitle;
    private readonly ToneLabel _advice;
    private readonly StatCard _latest;
    private readonly StatCard _count;
    private readonly StatCard _database;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _backupNow;
    private readonly ModernButton _restoreFile;
    private readonly ModernButton _restore;
    private readonly ModernButton _check;
    private readonly ModernButton _copy;
    private readonly ModernButton _delete;
    private readonly ToneLabel _detailTitle;
    private readonly TextBox _detailText;

    private bool _showingFiles;

    public BackupScreen(IServiceProvider services)
    {
        _presenter = services.GetRequiredService<BackupPresenter>();

        // ---- Header ---------------------------------------------------------------------------------------
        var title = new Label { Text = "Sao lưu & phục hồi dữ liệu", AutoSize = true, Location = new Point(0, 2), Font = new Font(Font.FontFamily, 14, FontStyle.Bold) };
        _status = new ModernBadge { Location = new Point(300, 8) };
        _subtitle = new ToneLabel { AutoSize = true, Location = new Point(0, 34), Tone = LabelTone.Muted };

        var openFolder = Action("Mở thư mục", Glyphs.Document, ButtonVariant.Ghost, () => { _presenter.OpenFolder(); return Task.CompletedTask; });
        _restoreFile = Action("Phục hồi từ file…", Glyphs.Return, ButtonVariant.Secondary, () => _presenter.RestoreFromFileAsync());
        _backupNow = Action("Sao lưu ngay (Ctrl+S)", Glyphs.Save, ButtonVariant.Primary, () => _presenter.BackupNowAsync());
        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        headerActions.Controls.AddRange([openFolder, _restoreFile, _backupNow]);

        var headerText = new Panel { Dock = DockStyle.Fill };
        headerText.Controls.AddRange([title, _status, _subtitle]);
        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(24, 20, 24, 0) };
        header.Controls.Add(headerText);
        header.Controls.Add(headerActions);

        _advice = new ToneLabel { Dock = DockStyle.Top, Height = 28, Tone = LabelTone.Warning, Padding = new Padding(24, 4, 24, 0), Visible = false };

        // ---- KPI row --------------------------------------------------------------------------------------
        _latest = Stat("Lần sao lưu gần nhất", Glyphs.History, BadgeKind.Primary);
        _count = Stat("Bản sao lưu đang giữ", Glyphs.Save, BadgeKind.Info);
        _database = Stat("Cơ sở dữ liệu", Glyphs.Settings, BadgeKind.Neutral);
        var stats = new StatCardRow { CardHeight = 120, MinCardWidth = 240, Padding = new Padding(24, 4, 24, 12) };
        stats.AddCards(_latest, _count, _database);

        // ---- Backup list ----------------------------------------------------------------------------------
        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có bản sao lưu nào. Bấm \"Sao lưu ngay\" để tạo bản đầu tiên.", SearchPlaceholder = "Tìm theo ngày, loại…" };
        _grid.AddDateColumn(nameof(BackupFile.CreatedAt), "Thời gian", width: 150, withTime: true);
        _grid.AddTextColumn(nameof(BackupFile.KindText), "Loại", width: 150);
        _grid.AddTextColumn(nameof(BackupFile.SizeText), "Kích thước", width: 100, searchable: false);
        _grid.AddTextColumn(nameof(BackupFile.FileName), "Tên file");
        _grid.SelectionChanged += (_, _) => OnSelectionChanged();
        _grid.RowActivated += async (_, _) => await _presenter.InspectSelectedAsync();

        _check = Action("Kiểm tra", Glyphs.Accept, ButtonVariant.Secondary, () => _presenter.InspectSelectedAsync());
        _restore = Action("Phục hồi", Glyphs.Return, ButtonVariant.Danger, () => _presenter.RestoreSelectedAsync());
        _copy = Action("Sao chép ra…", Glyphs.Copy, ButtonVariant.Secondary, () => _presenter.CopySelectedAsync());
        _delete = Action("Xóa", Glyphs.Delete, ButtonVariant.Ghost, () => _presenter.DeleteSelectedAsync());
        _grid.ToolbarItems.Controls.AddRange([_check, _restore, _copy, _delete]);

        var gridCard = new ModernCard { Title = "Các bản sao lưu", HeaderGlyph = Glyphs.List, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
        gridCard.Controls.Add(_grid);

        // ---- Detail card ----------------------------------------------------------------------------------
        _detailTitle = new ToneLabel { Dock = DockStyle.Top, Height = 32, Font = ThemeFonts.Get(FontRole.Subtitle), AutoEllipsis = true };
        _detailText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = ThemeFonts.Get(FontRole.Body),
        };
        var tips = new ToneLabel
        {
            Dock = DockStyle.Bottom,
            Height = 112,
            Tone = LabelTone.Muted,
            Font = ThemeFonts.Get(FontRole.Caption),
            Text = "Mẹo: sau mỗi ngày bán hàng, chép bản sao lưu mới nhất ra USB hoặc Google Drive (nút \"Sao chép ra…\"). " +
                   "Bản sao lưu nằm cùng máy sẽ mất theo nếu ổ cứng hỏng.\n\nBản thủ công được giữ đến khi bạn xóa; bản tự động và bản trước khi phục hồi chỉ giữ các bản mới nhất.",
        };
        var detailCard = new ModernCard { Title = "Chi tiết bản sao lưu", HeaderGlyph = Glyphs.Info, Dock = DockStyle.Fill };
        detailCard.Controls.Add(_detailText);
        detailCard.Controls.Add(_detailTitle);
        detailCard.Controls.Add(tips);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(24, 0, 24, 24) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(gridCard, 0, 0);
        body.Controls.Add(detailCard, 1, 0);

        Controls.Add(body);
        Controls.Add(stats);
        Controls.Add(_advice);
        Controls.Add(header);

        _presenter.Attach(this);
    }

    public override Task RefreshDataAsync() => _presenter.LoadAsync();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S when !IsBusy:
                _ = _presenter.BackupNowAsync();
                return true;
            case Keys.Delete when _grid.ContainsFocus && !IsBusy:
                _ = _presenter.DeleteSelectedAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnSelectionChanged()
    {
        if (!_showingFiles)
        {
            _ = _presenter.SelectAsync(_grid.GetSelected<BackupFile>());
        }
    }

    // ---- IBackupView ------------------------------------------------------------------------------------

    bool IView.IsAlive => IsAlive;

    void IBackupView.ShowHeader(BackupHeader header)
    {
        _status.Set(header.StatusText, header.StatusTone.ToBadgeKind());
        _subtitle.Text = header.Subtitle;
        _advice.Text = header.Advice ?? string.Empty;
        _advice.Tone = header.AdviceTone.ToLabelTone();
        _advice.Visible = header.Advice is not null;
        _latest.Value = header.LatestValue;
        _latest.Note = header.LatestNote;
        _count.Value = header.CountValue;
        _count.Note = header.CountNote;
        _database.Value = header.DatabaseValue;
        _database.Note = header.DatabaseNote;
        _restoreFile.Enabled = header.CanRestoreFromFile;
    }

    BackupFile? IBackupView.ShowFiles(IReadOnlyList<BackupFile> files, BackupFile? select)
    {
        // The presenter picks the selection itself; grid events raised while refilling are not user choices.
        _showingFiles = true;
        try
        {
            _grid.SetItems(files);
            if (select is not null)
            {
                _grid.SelectItem(select);
            }

            return _grid.GetSelected<BackupFile>();
        }
        finally
        {
            _showingFiles = false;
        }
    }

    void IBackupView.ShowDetail(BackupDetail detail)
    {
        _detailTitle.Text = detail.Title;
        _detailTitle.Tone = detail.TitleTone.ToLabelTone();
        _detailText.Text = string.Join(Environment.NewLine, detail.Lines);
    }

    void IBackupView.ShowActions(BackupActions actions)
    {
        _check.Enabled = actions.CanInspect;
        _restore.Enabled = actions.CanRestore;
        _copy.Enabled = actions.CanCopy;
        _delete.Enabled = actions.CanDelete;
    }

    Task<T> IBackupView.RunBusyAsync<T>(Func<Task<T>> work, string message) => RunBusyAsync(work, message);

    void IBackupView.ShowError(string message, string title) => ShowError(message, title);

    void IBackupView.ShowWarning(string message, string title, string details) => ModernDialog.Warning(FindForm(), message, title, details);

    void IBackupView.ShowSuccess(string message, string title) => ModernDialog.Success(FindForm(), message, title);

    void IBackupView.ToastWarning(string message, string? title) => ToastWarning(message, title);

    void IBackupView.ToastBackupCreated(string message, string title, Action copy) =>
        Toaster.Show(this, new ToastRequest(FeedbackKind.Success, message, title, ActionText: "Sao chép ra…"), copy);

    string? IBackupView.PickRestoreFile(string initialFolder)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn file sao lưu để phục hồi",
            Filter = "Bản sao lưu GroceryPOS (*.db)|*.db|Tất cả file (*.*)|*.*",
            InitialDirectory = initialFolder,
        };
        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.FileName : null;
    }

    string? IBackupView.PickCopyTarget(BackupFile file)
    {
        if (IsDisposed)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        using var dialog = new SaveFileDialog
        {
            Title = "Sao chép bản sao lưu ra (USB, ổ đĩa khác…)",
            FileName = file.FileName,
            Filter = $"Bản sao lưu (*{extension})|*{extension}",
        };
        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.FileName : null;
    }

    string? IBackupView.OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }

    void IBackupView.RestartApplication() => RestartApplication();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, confirmText.StartsWith("Phục hồi", StringComparison.Ordinal) ? "Không phục hồi" : "Giữ lại", danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    /// <summary>
    /// Starts a fresh instance and ends this one: every singleton (session, settings, cached screens) belonged to the data
    /// that was just replaced. <see cref="System.Windows.Forms.Application.Restart"/> is not used because the host's login
    /// loop would show the login form again in this process.
    /// </summary>
    private static void RestartApplication()
    {
        Log.Information("Restarting after a database restore");
        Log.CloseAndFlush();
        if (Environment.ProcessPath is { } exe)
        {
            // The new copy waits for this one to release the single-instance lock (see SingleInstance).
            Process.Start(new ProcessStartInfo(exe, Hosting.SingleInstance.RestartArgument) { UseShellExecute = true });
        }

        Environment.Exit(0);
    }

    // ---- Building blocks ----------------------------------------------------------------------------------

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent) => new()
    {
        Caption = caption,
        Glyph = glyph,
        Accent = accent,
        Value = "–",
        Size = new Size(280, 120),
        Margin = new Padding(0, 0, 16, 0),
    };
}
