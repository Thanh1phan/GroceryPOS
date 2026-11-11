using System.Diagnostics;
using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Backup;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GroceryPOS.App.Forms.Settings;

/// <summary>
/// Sao lưu dữ liệu (<c>system.backup</c>): freshness badge and advice, KPI cards, the backups in the backup folder (newest
/// first) with a detail card that checks the selected file, and actions – Sao lưu ngay (Ctrl+S), Phục hồi (from the list
/// or any file, e.g. a USB stick), Sao chép ra… (copy to USB / another drive), Xóa, Mở thư mục. A restore is confirmed in a
/// red dialog, keeps the current data as a "Trước khi phục hồi" copy, then restarts the app. F5 reloads.
/// </summary>
internal sealed class BackupScreen : UserControl
{
    private const string Caption = "Sao lưu dữ liệu";

    private readonly IServiceScopeFactory _scopeFactory;
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

    private BackupOverview? _overview;
    private CancellationTokenSource? _inspectCts;

    public BackupScreen(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        Dock = DockStyle.Fill;

        // ---- Header ---------------------------------------------------------------------------------------
        var title = new Label { Text = "Sao lưu & phục hồi dữ liệu", AutoSize = true, Location = new Point(0, 2), Font = new Font(Font.FontFamily, 14, FontStyle.Bold) };
        _status = new ModernBadge { Location = new Point(300, 8) };
        _status.Set("Đang tải…", BadgeKind.Neutral);
        _subtitle = new ToneLabel { AutoSize = true, Location = new Point(0, 34), Tone = LabelTone.Muted };

        var openFolder = Action("Mở thư mục", Glyphs.Document, ButtonVariant.Ghost, OpenFolder);
        _restoreFile = Action("Phục hồi từ file…", Glyphs.Return, ButtonVariant.Secondary, RestoreFromFileAsync);
        _backupNow = Action("Sao lưu ngay (Ctrl+S)", Glyphs.Save, ButtonVariant.Primary, BackupNowAsync);
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
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 136, WrapContents = false, Padding = new Padding(24, 4, 24, 0) };
        stats.Controls.AddRange([_latest, _count, _database]);

        // ---- Backup list ----------------------------------------------------------------------------------
        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có bản sao lưu nào. Bấm \"Sao lưu ngay\" để tạo bản đầu tiên.", SearchPlaceholder = "Tìm theo ngày, loại…" };
        _grid.AddDateColumn(nameof(BackupFile.CreatedAt), "Thời gian", width: 150, withTime: true);
        _grid.AddTextColumn(nameof(BackupFile.KindText), "Loại", width: 150);
        _grid.AddTextColumn(nameof(BackupFile.SizeText), "Kích thước", width: 100, searchable: false);
        _grid.AddTextColumn(nameof(BackupFile.FileName), "Tên file");
        _grid.SelectionChanged += (_, _) => OnSelectionChanged();
        _grid.RowActivated += async (_, _) => await InspectSelectedAsync();

        _check = Action("Kiểm tra", Glyphs.Accept, ButtonVariant.Secondary, InspectSelectedAsync);
        _restore = Action("Phục hồi", Glyphs.Return, ButtonVariant.Danger, RestoreSelectedAsync);
        _copy = Action("Sao chép ra…", Glyphs.Copy, ButtonVariant.Secondary, CopySelectedAsync);
        _delete = Action("Xóa", Glyphs.Delete, ButtonVariant.Ghost, DeleteSelectedAsync);
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

        ShowDetail(null, null);
        Load += async (_, _) => await ReloadAsync();
        Disposed += (_, _) => _inspectCts?.Cancel();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                _ = BackupNowAsync();
                return true;
            case Keys.F5:
                _ = ReloadAsync();
                return true;
            case Keys.Delete when _grid.ContainsFocus:
                _ = DeleteSelectedAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Loading ------------------------------------------------------------------------------------------

    private async Task ReloadAsync(string? select = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBackupService>().GetOverviewAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            _status.Set("Không tải được", BadgeKind.Danger);
            _subtitle.Text = result.Error.Message;
            return;
        }

        var overview = _overview = result.Value;
        var now = DateTime.Now;
        var latest = overview.Latest?.CreatedAt;
        _status.Set(BackupText.Status(latest, now), BackupText.Health(latest, now) switch
        {
            BackupHealth.Ok => BadgeKind.Success,
            BackupHealth.Warning => BadgeKind.Warning,
            _ => BadgeKind.Danger,
        });
        _subtitle.Text = $"{BackupText.Schedule(overview.AutomaticIntervalHours)} · Thư mục: {overview.Folder}";

        var advice = BackupText.Advice(latest, now);
        _advice.Text = advice ?? string.Empty;
        _advice.Tone = BackupText.Health(latest, now) == BackupHealth.Danger ? LabelTone.Danger : LabelTone.Warning;
        _advice.Visible = advice is not null;

        _latest.Value = latest is null ? "Chưa có" : BackupText.Age(latest.Value, now);
        _latest.Note = overview.Latest is { } last ? $"{last.CreatedAt:HH:mm dd/MM/yyyy} · {last.KindText}" : "Hãy sao lưu ngay";
        _count.Value = overview.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _count.Note = $"Tổng {BackupText.FormatSize(overview.TotalBytes)}";
        _database.Value = overview.ProviderName;
        _database.Note = overview.SupportsRestore ? "Sao lưu & phục hồi trong ứng dụng" : "Phục hồi bằng SQL Server Management Studio";
        _restoreFile.Enabled = overview.SupportsRestore;

        _grid.SetItems(overview.Files);
        if (select is not null && overview.Files.FirstOrDefault(f => f.FileName == select) is { } selected)
        {
            _grid.SelectItem(selected);
        }

        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        var selected = _grid.GetSelected<BackupFile>();
        _check.Enabled = selected is not null;
        _restore.Enabled = selected is not null && _overview is { SupportsRestore: true };
        _copy.Enabled = selected is not null;
        _delete.Enabled = selected is not null;
        ShowDetail(selected, null);
        if (selected is not null)
        {
            _ = InspectAsync(selected, showOverlay: false);
        }
    }

    private void ShowDetail(BackupFile? file, BackupInspection? inspection)
    {
        if (file is null)
        {
            _detailTitle.Text = "Chọn một bản sao lưu";
            _detailTitle.Tone = LabelTone.Muted;
            _detailText.Text = "Chọn một dòng để xem nội dung bản sao lưu (số sản phẩm, khách hàng, hóa đơn) trước khi phục hồi.";
            return;
        }

        var lines = new List<string>
        {
            $"Tạo lúc: {file.CreatedAt:HH:mm:ss dd/MM/yyyy}",
            $"Loại: {file.KindText}",
            $"Kích thước: {file.SizeText}",
            $"File: {file.FileName}",
            string.Empty,
        };
        if (inspection is null)
        {
            lines.Add("Đang kiểm tra…");
            _detailTitle.Tone = LabelTone.Normal;
        }
        else
        {
            lines.AddRange(BackupText.Describe(inspection));
            _detailTitle.Tone = inspection.CanRestore ? LabelTone.Success : inspection.IsValid ? LabelTone.Warning : LabelTone.Danger;
        }

        _detailTitle.Text = BackupText.Age(file.CreatedAt, DateTime.Now) + " · " + file.KindText;
        _detailText.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task<BackupInspection?> InspectAsync(BackupFile file, bool showOverlay)
    {
        _inspectCts?.Cancel();
        var cts = _inspectCts = new CancellationTokenSource();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var result = showOverlay
                ? await LoadingOverlay.RunAsync(this, () => service.InspectAsync(file.FullPath, cts.Token), "Đang kiểm tra bản sao lưu…")
                : await service.InspectAsync(file.FullPath, cts.Token);
            if (IsDisposed || cts.IsCancellationRequested)
            {
                return null;
            }

            if (result.IsFailure)
            {
                ShowDetail(file, BackupInspection.Invalid(result.Error.Message));
                return null;
            }

            if (_grid.GetSelected<BackupFile>()?.FullPath == file.FullPath)
            {
                ShowDetail(file, result.Value);
            }

            return result.Value;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // ---- Actions ------------------------------------------------------------------------------------------

    private async Task BackupNowAsync()
    {
        if (!_backupNow.Enabled || LoadingOverlay.IsBusy(this))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var result = await LoadingOverlay.RunAsync(this, () => service.CreateAsync(), "Đang sao lưu dữ liệu…");
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            ModernDialog.Error(FindForm(), result.Error.Message, Caption);
            return;
        }

        var file = result.Value;
        Toaster.Show(
            this,
            new ToastRequest(FeedbackKind.Success, $"Đã sao lưu {file.SizeText} – {file.FileName}", Caption, ActionText: "Sao chép ra…"),
            () => _ = CopyAsync(file));
        await ReloadAsync(select: file.FileName);
    }

    private async Task InspectSelectedAsync()
    {
        if (_grid.GetSelected<BackupFile>() is { } file)
        {
            ShowDetail(file, null);
            await InspectAsync(file, showOverlay: true);
        }
    }

    private async Task RestoreSelectedAsync()
    {
        if (_grid.GetSelected<BackupFile>() is { } file)
        {
            await RestoreAsync(file);
        }
    }

    private async Task RestoreFromFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn file sao lưu để phục hồi",
            Filter = "Bản sao lưu GroceryPOS (*.db)|*.db|Tất cả file (*.*)|*.*",
            InitialDirectory = _overview?.Folder ?? string.Empty,
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            await RestoreAsync(BackupFile.FromPath(dialog.FileName));
        }
    }

    private async Task RestoreAsync(BackupFile file)
    {
        if (_overview is not { SupportsRestore: true } || LoadingOverlay.IsBusy(this))
        {
            return;
        }

        var inspection = await InspectAsync(file, showOverlay: true);
        if (IsDisposed || inspection is null)
        {
            return;
        }

        if (!inspection.CanRestore)
        {
            ModernDialog.Warning(FindForm(), "Không thể phục hồi từ file này.", Caption, string.Join(Environment.NewLine, BackupText.Describe(inspection)));
            return;
        }

        var summary = string.Join(Environment.NewLine, BackupText.Describe(inspection).Skip(1));
        if (!ModernDialog.Confirm(
                FindForm(),
                $"{BackupText.RestoreWarning(file)}\n\nNội dung bản sao lưu:\n{summary}",
                "Phục hồi dữ liệu",
                "Phục hồi và khởi động lại",
                "Không phục hồi",
                danger: true))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
        var result = await LoadingOverlay.RunAsync(this, () => service.RestoreAsync(file.FullPath), "Đang phục hồi dữ liệu… Không tắt máy.");
        if (result.IsFailure)
        {
            ModernDialog.Error(FindForm(), result.Error.Message, Caption);
            await ReloadAsync();
            return;
        }

        ModernDialog.Success(
            FindForm(),
            $"Đã phục hồi dữ liệu từ bản sao lưu lúc {file.CreatedAt:HH:mm dd/MM/yyyy}.\n\n" +
            $"Dữ liệu trước khi phục hồi được giữ ở bản \"{result.Value.SafetyCopy.FileName}\".\n\nỨng dụng sẽ khởi động lại để tải dữ liệu mới; hãy đăng nhập lại.",
            Caption);
        RestartApplication();
    }

    private async Task CopySelectedAsync()
    {
        if (_grid.GetSelected<BackupFile>() is { } file)
        {
            await CopyAsync(file);
        }
    }

    private async Task CopyAsync(BackupFile file)
    {
        if (IsDisposed)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Sao chép bản sao lưu ra (USB, ổ đĩa khác…)",
            FileName = file.FileName,
            Filter = $"Bản sao lưu (*{Path.GetExtension(file.FileName)})|*{Path.GetExtension(file.FileName)}",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        var target = dialog.FileName;
        if (string.Equals(Path.GetFullPath(target), file.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await LoadingOverlay.RunAsync(this, () => Task.Run(() => File.Copy(file.FullPath, target, overwrite: true)), "Đang sao chép…");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModernDialog.Warning(FindForm(), "Không sao chép được. Kiểm tra USB/ổ đĩa còn chỗ trống và cho phép ghi.", Caption, ex.Message);
            return;
        }

        Toaster.Success(this, $"Đã sao chép {file.FileName} ra {Path.GetDirectoryName(target)}.", Caption);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_grid.GetSelected<BackupFile>() is not { } file)
        {
            return;
        }

        var warning = file.Kind == BackupKind.Manual
            ? "Bản sao lưu thủ công này sẽ không thể khôi phục lại."
            : "Thao tác này không hoàn tác được.";
        if (!ModernDialog.Confirm(FindForm(), $"Xóa bản sao lưu lúc {file.CreatedAt:HH:mm dd/MM/yyyy} ({file.SizeText})?\n{warning}", Caption, "Xóa", "Giữ lại", danger: true))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBackupService>().DeleteAsync(file.FileName);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            ModernDialog.Warning(FindForm(), result.Error.Message, Caption);
        }
        else
        {
            Toaster.Success(this, $"Đã xóa {file.FileName}.", Caption);
        }

        await ReloadAsync();
    }

    private Task OpenFolder()
    {
        if (_overview is { } overview)
        {
            try
            {
                Directory.CreateDirectory(overview.Folder);
                Process.Start(new ProcessStartInfo(overview.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Toaster.Warning(this, $"Không mở được thư mục: {ex.Message}", Caption);
            }
        }

        return Task.CompletedTask;
    }

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
