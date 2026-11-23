using GroceryPOS.Application.Backup;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Backup;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Tests;

public sealed class BackupPresenterTests
{
    private sealed class FakeBackupView : RecordingView, IBackupView
    {
        public List<BackupHeader> Headers { get; } = new();
        public List<(IReadOnlyList<BackupFile> Files, BackupFile? Select)> Lists { get; } = new();
        public List<BackupDetail> Details { get; } = new();
        public List<BackupActions> ActionStates { get; } = new();
        public List<string> BusyMessages { get; } = new();
        public List<string> ErrorBoxes { get; } = new();
        public List<(string Message, string Details)> DetailedWarnings { get; } = new();
        public List<string> SuccessBoxes { get; } = new();
        public List<string> WarningToasts { get; } = new();
        public List<(string Message, Action Copy)> BackupToasts { get; } = new();
        public List<string> OpenedFolders { get; } = new();
        public string? RestorePick { get; set; }
        public string? CopyPick { get; set; }
        public string? OpenFolderError { get; set; }
        public int Restarts { get; private set; }

        /// <summary>When set, RunBusyAsync waits for it before running the work.</summary>
        public Task? BusyGate { get; set; }

        public BackupHeader Header => Headers[^1];
        public BackupDetail Detail => Details[^1];
        public BackupActions Actions => ActionStates[^1];

        public void ShowHeader(BackupHeader header) => Headers.Add(header);

        /// <summary>Like the real grid: the requested row, else the first one.</summary>
        public BackupFile? ShowFiles(IReadOnlyList<BackupFile> files, BackupFile? select)
        {
            Lists.Add((files, select));
            return select ?? (files.Count > 0 ? files[0] : null);
        }

        public void ShowDetail(BackupDetail detail) => Details.Add(detail);

        public void ShowActions(BackupActions actions) => ActionStates.Add(actions);

        public async Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message)
        {
            BusyMessages.Add(message);
            if (BusyGate is { } gate)
            {
                await gate;
            }

            return await work();
        }

        public void ShowError(string message, string title) => ErrorBoxes.Add(message);

        public void ShowWarning(string message, string title, string details) => DetailedWarnings.Add((message, details));

        public void ShowSuccess(string message, string title) => SuccessBoxes.Add(message);

        public void ToastWarning(string message, string? title = null) => WarningToasts.Add(message);

        public void ToastBackupCreated(string message, string title, Action copy) => BackupToasts.Add((message, copy));

        public string? PickRestoreFile(string initialFolder) => RestorePick;

        public string? PickCopyTarget(BackupFile file) => CopyPick;

        public string? OpenFolder(string folder)
        {
            OpenedFolders.Add(folder);
            return OpenFolderError;
        }

        public void RestartApplication() => Restarts++;
    }

    private static (TestHost Host, BackupPresenter Presenter, FakeBackupView View) Create(bool canBackup = true)
    {
        var host = canBackup ? new TestHost(Permissions.SystemBackup) : new TestHost();
        var presenter = host.Get<BackupPresenter>();
        var view = new FakeBackupView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static DateTime Now(TestHost host) => host.Clock.GetLocalNow().DateTime;

    [Fact]
    public void Attaching_shows_loading_and_no_selection()
    {
        var (host, _, view) = Create();
        using var _h = host;

        Assert.Equal(BackupHeader.Loading, view.Header);
        Assert.Equal(BackupDetail.None, view.Detail);
        Assert.Equal(BackupActions.None, view.Actions);
    }

    [Fact]
    public async Task Without_permission_nothing_is_loaded_and_backup_is_refused()
    {
        var (host, presenter, view) = Create(canBackup: false);
        using var _h = host;

        await presenter.LoadAsync();
        await presenter.BackupNowAsync();

        Assert.Equal(BackupPresenter.NoPermission, view.Header.Subtitle);
        Assert.Equal(Tone.Danger, view.Header.StatusTone);
        Assert.Empty(host.Backups.Calls);
        Assert.Equal(new[] { BackupPresenter.NoPermission }, view.Warnings);
    }

    [Fact]
    public async Task Load_shows_a_fresh_backup_in_green_with_the_kpis()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-3), 1024 * 1024);
        host.Backups.Add(BackupKind.Manual, Now(host).AddDays(-1), 1024 * 1024);

        await presenter.LoadAsync();

        var header = view.Header;
        Assert.Equal("Sao lưu 3 giờ trước", header.StatusText);
        Assert.Equal(Tone.Success, header.StatusTone);
        Assert.Null(header.Advice);
        Assert.Equal("3 giờ trước", header.LatestValue);
        Assert.EndsWith("Tự động", header.LatestNote);
        Assert.Equal("2", header.CountValue);
        Assert.Equal("Tổng 2 MB", header.CountNote);
        Assert.Equal("SQLite", header.DatabaseValue);
        Assert.True(header.CanRestoreFromFile);
        Assert.Contains(host.Backups.Folder, header.Subtitle);
        Assert.Equal(2, view.Lists[^1].Files.Count);
        Assert.Null(view.Lists[^1].Select);
        Assert.Equal(host.Backups.Files[0], presenter.Selected); // the grid's first row
        Assert.Equal(Tone.Success, view.Detail.TitleTone);
    }

    [Fact]
    public void Header_warns_about_stale_or_missing_backups_and_sql_server()
    {
        var now = new DateTime(2026, 11, 20, 10, 0, 0);
        BackupOverview Overview(bool restore, params BackupFile[] files) => new("C:\\Backups", restore ? "SQLite" : "SQL Server", restore, 24, files);
        BackupFile File(int daysAgo) => new("f.db", "C:\\Backups\\f.db", BackupKind.Manual, now.AddDays(-daysAgo), 10);

        var none = BackupHeader.From(Overview(true), now);
        Assert.Equal(Tone.Danger, none.StatusTone);
        Assert.Equal("Chưa có", none.LatestValue);
        Assert.Equal("Hãy sao lưu ngay", none.LatestNote);
        Assert.Equal(Tone.Danger, none.AdviceTone);
        Assert.NotNull(none.Advice);

        var stale = BackupHeader.From(Overview(true, File(3)), now);
        Assert.Equal(Tone.Warning, stale.StatusTone);
        Assert.Equal(Tone.Warning, stale.AdviceTone);

        var old = BackupHeader.From(Overview(true, File(9)), now);
        Assert.Equal(Tone.Danger, old.StatusTone);
        Assert.Contains("9 ngày", old.Advice);

        var sql = BackupHeader.From(Overview(false, File(0)), now);
        Assert.False(sql.CanRestoreFromFile);
        Assert.Equal("Phục hồi bằng SQL Server Management Studio", sql.DatabaseNote);
    }

    [Fact]
    public async Task Selecting_checks_the_file_once_and_enables_the_actions()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();

        await presenter.SelectAsync(file);

        Assert.Contains(view.Details, d => d.Lines.Contains(BackupDetail.Checking));
        Assert.Equal(Tone.Success, view.Detail.TitleTone);
        Assert.Contains("✓ File nguyên vẹn", view.Detail.Lines);
        Assert.Contains("Hóa đơn: 900", view.Detail.Lines);
        Assert.Equal(new BackupActions(true, true, true, true), view.Actions);

        await presenter.SelectAsync(null);
        Assert.Equal(BackupDetail.None, view.Detail);
        Assert.Equal(BackupActions.None, view.Actions);

        await presenter.SelectAsync(file);
        Assert.Equal(1, host.Backups.Calls.Count(c => c.StartsWith("inspect:", StringComparison.Ordinal)));
        Assert.Equal(Tone.Success, view.Detail.TitleTone);
    }

    [Fact]
    public async Task Detail_is_amber_for_a_newer_version_and_red_for_a_broken_file()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var newer = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        var broken = host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-2));
        host.Backups.Inspections[newer.FullPath] = Good with { Schema = BackupSchemaCompatibility.Newer };
        host.Backups.Inspections[broken.FullPath] = BackupInspection.Invalid("file is not a database");
        await presenter.LoadAsync();

        await presenter.SelectAsync(newer);
        Assert.Equal(Tone.Warning, view.Detail.TitleTone);

        await presenter.SelectAsync(broken);
        Assert.Equal(Tone.Danger, view.Detail.TitleTone);
        Assert.Contains("✗ File không dùng được: file is not a database", view.Detail.Lines);
    }

    [Fact]
    public async Task A_failed_check_is_shown_as_unusable_and_not_cached()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        host.Backups.InspectError = new Error("io", "đang bị khóa");
        await presenter.LoadAsync(); // the first row is selected and checked

        Assert.Equal(file, presenter.Selected);
        Assert.Equal(Tone.Danger, view.Detail.TitleTone);
        Assert.Contains("✗ File không dùng được: đang bị khóa", view.Detail.Lines);

        host.Backups.InspectError = null;
        await presenter.InspectSelectedAsync();
        Assert.Equal(Tone.Success, view.Detail.TitleTone);
        Assert.Equal(2, host.Backups.Calls.Count(c => c.StartsWith("inspect:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_slow_check_of_an_earlier_selection_does_not_overwrite_the_current_one()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.Add(BackupKind.Manual, Now(host).AddMinutes(-5)); // selected by the load
        var first = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        var second = host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-5));
        host.Backups.Inspections[first.FullPath] = BackupInspection.Invalid("hỏng");
        await presenter.LoadAsync();
        var release = new TaskCompletionSource();
        host.Backups.InspectGate = path => path == first.FullPath ? release.Task : Task.CompletedTask;

        var slow = presenter.SelectAsync(first);
        await presenter.SelectAsync(second);
        release.SetResult();
        await slow;

        Assert.Equal(Tone.Success, view.Detail.TitleTone);
        Assert.Contains($"File: {second.FileName}", view.Detail.Lines);
    }

    [Fact]
    public async Task A_failed_reload_keeps_the_list_and_reports_the_error()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        host.Backups.OverviewError = new Error("io", "Thư mục không truy cập được");

        await presenter.LoadAsync();

        Assert.Single(view.Lists);
        Assert.Equal(new[] { "Thư mục không truy cập được" }, view.Errors);
        Assert.Equal(Tone.Success, view.Header.StatusTone);
    }

    [Fact]
    public async Task A_failed_first_load_shows_the_error_in_the_header()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.OverviewError = new Error("io", "Thư mục không truy cập được");

        await presenter.LoadAsync();

        Assert.Equal("Không tải được", view.Header.StatusText);
        Assert.Equal("Thư mục không truy cập được", view.Header.Subtitle);
        Assert.Empty(view.Lists);
    }

    [Fact]
    public async Task Reload_keeps_the_selected_file()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);

        await presenter.LoadAsync();

        Assert.Equal(file, view.Lists[^1].Select);
        Assert.Equal(file, presenter.Selected);
    }

    [Fact]
    public async Task Backup_now_offers_a_copy_and_selects_the_new_file()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-1));
        await presenter.LoadAsync();

        await presenter.BackupNowAsync();

        Assert.Contains("create", host.Backups.Calls);
        Assert.Equal(new[] { "Đang sao lưu dữ liệu…" }, view.BusyMessages);
        var toast = Assert.Single(view.BackupToasts);
        Assert.StartsWith("Đã sao lưu 2 KB – grocerypos-", toast.Message);
        Assert.Equal(host.Backups.Files[0], view.Lists[^1].Select);
        Assert.Equal(host.Backups.Files[0], presenter.Selected);
    }

    [Fact]
    public async Task A_failed_backup_shows_an_error_box()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync();
        host.Backups.CreateError = new Error("backup.failed", "Không sao lưu được: ổ đĩa đầy");

        await presenter.BackupNowAsync();

        Assert.Equal(new[] { "Không sao lưu được: ổ đĩa đầy" }, view.ErrorBoxes);
        Assert.Empty(view.BackupToasts);
        Assert.Single(view.Lists);
    }

    [Fact]
    public async Task A_second_backup_is_ignored_while_one_runs()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync();
        var gate = new TaskCompletionSource();
        view.BusyGate = gate.Task;

        var first = presenter.BackupNowAsync();
        Assert.True(presenter.IsBusy);
        await presenter.BackupNowAsync();
        gate.SetResult();
        await first;

        Assert.Single(host.Backups.Calls, "create");
        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task Restore_rechecks_confirms_in_red_and_restarts()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);

        await presenter.RestoreSelectedAsync();

        Assert.Equal(2, host.Backups.Calls.Count(c => c.StartsWith("inspect:", StringComparison.Ordinal)));
        var question = Assert.Single(view.DangerQuestions);
        Assert.Contains("Nội dung bản sao lưu:", question);
        Assert.Contains("Sản phẩm: 120", question);
        Assert.Contains("restore:" + file.FileName, host.Backups.Calls);
        Assert.Contains("grocerypos-20261120-100000-prerestore.db", Assert.Single(view.SuccessBoxes));
        Assert.Equal(1, view.Restarts);
    }

    [Fact]
    public async Task Restore_is_refused_for_a_backup_of_a_newer_version()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        host.Backups.Inspections[file.FullPath] = Good with { Schema = BackupSchemaCompatibility.Newer };
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);

        await presenter.RestoreSelectedAsync();

        var warning = Assert.Single(view.DetailedWarnings);
        Assert.Equal("Không thể phục hồi từ file này.", warning.Message);
        Assert.Contains("Phiên bản mới hơn", warning.Details);
        Assert.Empty(view.Questions);
        Assert.DoesNotContain(host.Backups.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Declining_the_restore_changes_nothing()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);
        view.ConfirmAnswer = false;

        await presenter.RestoreSelectedAsync();

        Assert.DoesNotContain(host.Backups.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
        Assert.Equal(0, view.Restarts);
        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task A_failed_restore_reports_and_reloads_without_restarting()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);
        host.Backups.RestoreError = new Error("backup.failed", "Không phục hồi được: file đang mở");

        await presenter.RestoreSelectedAsync();

        Assert.Equal(new[] { "Không phục hồi được: file đang mở" }, view.ErrorBoxes);
        Assert.Equal(0, view.Restarts);
        Assert.Equal(2, view.Lists.Count);
        Assert.False(presenter.IsBusy);
    }

    [Fact]
    public async Task Restore_is_not_offered_on_sql_server()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Backups.SupportsRestore = false;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);
        view.RestorePick = file.FullPath;

        await presenter.RestoreSelectedAsync();
        await presenter.RestoreFromFileAsync();

        Assert.False(view.Actions.CanRestore);
        Assert.True(view.Actions.CanCopy);
        Assert.DoesNotContain(host.Backups.Calls, c => c.StartsWith("restore:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_from_file_uses_the_picked_file()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync();

        await presenter.RestoreFromFileAsync();
        Assert.Empty(view.Questions);

        view.RestorePick = Path.Combine(Path.GetTempPath(), "usb", "grocerypos-20261101-200000-manual.db");
        await presenter.RestoreFromFileAsync();

        Assert.Contains("restore:grocerypos-20261101-200000-manual.db", host.Backups.Calls);
        Assert.Contains("20:00 01/11/2026", Assert.Single(view.DangerQuestions));
    }

    [Fact]
    public async Task Delete_confirms_and_selects_the_next_row()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var newest = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        var middle = host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-2));
        var oldest = host.Backups.Add(BackupKind.Automatic, Now(host).AddHours(-3));
        await presenter.LoadAsync();
        await presenter.SelectAsync(middle);

        await presenter.DeleteSelectedAsync();

        Assert.Contains("Thao tác này không hoàn tác được.", Assert.Single(view.DangerQuestions));
        Assert.Contains("delete:" + middle.FileName, host.Backups.Calls);
        Assert.Equal($"Đã xóa {middle.FileName}.", Assert.Single(view.Successes));
        Assert.Equal(oldest, view.Lists[^1].Select);

        await presenter.DeleteSelectedAsync();
        Assert.Contains("delete:" + oldest.FileName, host.Backups.Calls);
        Assert.Equal(newest, view.Lists[^1].Select);
    }

    [Fact]
    public async Task Deleting_a_manual_backup_warns_more_strongly_and_can_be_declined()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);
        view.ConfirmAnswer = false;

        await presenter.DeleteSelectedAsync();

        Assert.Contains("Bản sao lưu thủ công này sẽ không thể khôi phục lại.", Assert.Single(view.DangerQuestions));
        Assert.DoesNotContain(host.Backups.Calls, c => c.StartsWith("delete:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_delete_warns_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
        await presenter.LoadAsync();
        await presenter.SelectAsync(file);
        host.Backups.DeleteError = BackupErrors.NotFound;

        await presenter.DeleteSelectedAsync();

        Assert.Equal(new[] { BackupErrors.NotFound.Message }, view.Warnings);
        Assert.Equal(2, view.Lists.Count);
        Assert.Empty(view.Successes);
    }

    [Fact]
    public async Task Copy_writes_the_file_to_the_picked_place()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        var dir = Directory.CreateTempSubdirectory("grocerypos-copy-");
        try
        {
            host.Backups.Folder = dir.FullName;
            var file = host.Backups.Add(BackupKind.Manual, Now(host).AddHours(-1));
            await File.WriteAllTextAsync(file.FullPath, "backup");
            await presenter.LoadAsync();
            await presenter.SelectAsync(file);

            var usb = Directory.CreateDirectory(Path.Combine(dir.FullName, "usb"));
            view.CopyPick = Path.Combine(usb.FullName, file.FileName);
            await presenter.CopySelectedAsync();

            Assert.Equal("backup", await File.ReadAllTextAsync(view.CopyPick));
            Assert.Equal($"Đã sao chép {file.FileName} ra {usb.FullName}.", Assert.Single(view.Successes));

            view.CopyPick = file.FullPath; // onto itself: nothing to do
            await presenter.CopySelectedAsync();
            Assert.Single(view.Successes);

            view.CopyPick = Path.Combine(dir.FullName, "missing", "x.db");
            await presenter.CopySelectedAsync();
            Assert.Equal("Không sao chép được. Kiểm tra USB/ổ đĩa còn chỗ trống và cho phép ghi.", Assert.Single(view.DetailedWarnings).Message);
            Assert.False(presenter.IsBusy);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task The_backup_toast_copies_the_new_file()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        await presenter.LoadAsync();
        await presenter.BackupNowAsync();

        view.CopyPick = null; // the user cancels the save dialog
        view.BackupToasts[0].Copy();

        Assert.Empty(view.Successes);
        Assert.Empty(view.DetailedWarnings);
    }

    [Fact]
    public async Task Open_folder_reports_a_failure()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        presenter.OpenFolder(); // not loaded yet: nothing to open
        Assert.Empty(view.OpenedFolders);

        await presenter.LoadAsync();
        view.OpenFolderError = "Access denied";
        presenter.OpenFolder();

        Assert.Equal(new[] { host.Backups.Folder }, view.OpenedFolders);
        Assert.Equal(new[] { "Không mở được thư mục: Access denied" }, view.WarningToasts);
    }

    private static BackupInspection Good => FakeBackupService.Good;
}
