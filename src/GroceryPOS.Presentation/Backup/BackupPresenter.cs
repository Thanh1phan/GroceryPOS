using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Backup;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Backup;

/// <summary>The Sao lưu dữ liệu screen as seen by its presenter.</summary>
public interface IBackupView : IView
{
    void ShowHeader(BackupHeader header);

    /// <summary>
    /// Shows the backups (newest first) and selects <paramref name="select"/> when given; returns the row actually
    /// selected afterwards (the grid falls back to the first row).
    /// </summary>
    BackupFile? ShowFiles(IReadOnlyList<BackupFile> files, BackupFile? select);

    void ShowDetail(BackupDetail detail);

    void ShowActions(BackupActions actions);

    /// <summary>Runs <paramref name="work"/> behind the loading overlay.</summary>
    Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message);

    void ShowError(string message, string title);

    /// <summary>A warning box with expandable <paramref name="details"/>.</summary>
    void ShowWarning(string message, string title, string details);

    void ShowSuccess(string message, string title);

    void ToastWarning(string message, string? title = null);

    /// <summary>Success toast with a "Sao chép ra…" button that runs <paramref name="copy"/>.</summary>
    void ToastBackupCreated(string message, string title, Action copy);

    /// <summary>Asks for a backup file to restore (e.g. from a USB stick); null when cancelled.</summary>
    string? PickRestoreFile(string initialFolder);

    /// <summary>Asks where to copy <paramref name="file"/>; null when cancelled.</summary>
    string? PickCopyTarget(BackupFile file);

    /// <summary>Opens <paramref name="folder"/> in Explorer; the error text when that failed, otherwise null.</summary>
    string? OpenFolder(string folder);

    /// <summary>Starts a fresh instance and ends this one (after a restore).</summary>
    void RestartApplication();
}

/// <summary>
/// Sao lưu dữ liệu logic (<c>system.backup</c>): overview with "latest wins" loading, the selected file checked in the
/// background (results cached per file), Sao lưu ngay, Phục hồi from the list or any file (re-checked, red confirmation,
/// restart), Sao chép ra…, Xóa (keeps the selection on the neighbouring row) and Mở thư mục. Only one long action runs at a time.
/// </summary>
public sealed class BackupPresenter : Presenter<IBackupView>
{
    public const string Title = "Sao lưu dữ liệu";
    public const string NoPermission = "Bạn không có quyền sao lưu hoặc phục hồi dữ liệu.";

    private readonly TimeProvider _clock;
    private readonly Dictionary<string, (BackupFile File, BackupInspection Inspection)> _inspections = new(StringComparer.OrdinalIgnoreCase);
    private BackupOverview? _overview;
    private BackupFile? _selected;
    private long _loadVersion;
    private long _inspectVersion;
    private bool _busy;

    public BackupPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, TimeProvider clock)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        CanManage = currentUser.HasPermission(Permissions.SystemBackup);
    }

    public bool CanManage { get; }

    public BackupOverview? Overview => _overview;

    public BackupFile? Selected => _selected;

    /// <summary>True while a backup, restore, copy or delete runs.</summary>
    public bool IsBusy => _busy;

    private DateTime Now => _clock.GetLocalNow().DateTime;

    private bool SupportsRestore => _overview is { SupportsRestore: true };

    protected override void OnAttached()
    {
        View.ShowHeader(CanManage ? BackupHeader.Loading : BackupHeader.Failed(NoPermission));
        View.ShowDetail(BackupDetail.None);
        View.ShowActions(BackupActions.None);
    }

    /// <summary>
    /// Reloads the overview; selects the file named <paramref name="select"/>, else keeps the current selection when it
    /// still exists. A failed load keeps the rows already shown and reports the error.
    /// </summary>
    public async Task LoadAsync(string? select = null)
    {
        if (!CanManage)
        {
            return;
        }

        var version = ++_loadVersion;
        var result = await UseAsync<IBackupService, Result<BackupOverview>>(s => s.GetOverviewAsync());
        if (!ViewAlive || version != _loadVersion)
        {
            return;
        }

        if (result.IsFailure)
        {
            if (_overview is null)
            {
                View.ShowHeader(BackupHeader.Failed(result.Error.Message));
            }
            else
            {
                View.ToastError(result.Error.Message, "Không tải được danh sách bản sao lưu");
            }

            return;
        }

        ShowOverview(result.Value, select ?? _selected?.FileName);
    }

    /// <summary>The grid selection changed: shows the file and checks it in the background (cached per file).</summary>
    public Task SelectAsync(BackupFile? file)
    {
        _selected = file;
        View.ShowActions(ActionsFor(file));
        if (file is null)
        {
            ++_inspectVersion;
            View.ShowDetail(BackupDetail.None);
            return Task.CompletedTask;
        }

        if (Cached(file) is { } cached)
        {
            ++_inspectVersion;
            View.ShowDetail(BackupDetail.Inspected(file, cached, Now));
            return Task.CompletedTask;
        }

        View.ShowDetail(BackupDetail.Pending(file, Now));
        return InspectAsync(file, overlay: false);
    }

    /// <summary>Kiểm tra / Enter: checks the selected file again behind the overlay.</summary>
    public async Task InspectSelectedAsync()
    {
        if (_selected is { } file && !_busy)
        {
            View.ShowDetail(BackupDetail.Pending(file, Now));
            await InspectAsync(file, overlay: true);
        }
    }

    /// <summary>Sao lưu ngay (Ctrl+S): a manual backup, then a toast offering to copy it and the list with it selected.</summary>
    public async Task BackupNowAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (_busy)
        {
            return;
        }

        Result<BackupFile> result;
        _busy = true;
        try
        {
            result = await View.RunBusyAsync(
                () => UseAsync<IBackupService, Result<BackupFile>>(s => s.CreateAsync()),
                "Đang sao lưu dữ liệu…");
        }
        finally
        {
            _busy = false;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message, Title);
            return;
        }

        var file = result.Value;
        View.ToastBackupCreated($"Đã sao lưu {file.SizeText} – {file.FileName}", Title, () => _ = CopyAsync(file));
        await LoadAsync(select: file.FileName);
    }

    public Task RestoreSelectedAsync() => _selected is { } file ? RestoreAsync(file) : Task.CompletedTask;

    /// <summary>Phục hồi từ file…: any file, e.g. a copy on a USB stick.</summary>
    public async Task RestoreFromFileAsync()
    {
        if (!SupportsRestore || _busy)
        {
            return;
        }

        if (View.PickRestoreFile(_overview!.Folder) is { Length: > 0 } path)
        {
            await RestoreAsync(BackupFile.FromPath(path));
        }
    }

    /// <summary>
    /// Re-checks <paramref name="file"/> (never from the cache), refuses one that cannot be restored, asks in red with the
    /// backup's content, restores, then tells where the previous data was kept and restarts the app.
    /// </summary>
    public async Task RestoreAsync(BackupFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!SupportsRestore || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var inspection = await InspectAsync(file, overlay: true, useCache: false);
            if (!ViewAlive || inspection is null)
            {
                return;
            }

            if (!inspection.CanRestore)
            {
                View.ShowWarning("Không thể phục hồi từ file này.", Title, string.Join(Environment.NewLine, BackupText.Describe(inspection)));
                return;
            }

            var summary = string.Join("\n", BackupText.Describe(inspection).Skip(1));
            if (!View.Confirm($"{BackupText.RestoreWarning(file)}\n\nNội dung bản sao lưu:\n{summary}", "Phục hồi dữ liệu", "Phục hồi và khởi động lại", danger: true))
            {
                return;
            }

            var result = await View.RunBusyAsync(
                () => UseAsync<IBackupService, Result<RestoreOutcome>>(s => s.RestoreAsync(file.FullPath)),
                "Đang phục hồi dữ liệu… Không tắt máy.");
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowError(result.Error.Message, Title);
                _busy = false;
                await LoadAsync();
                return;
            }

            View.ShowSuccess(
                $"Đã phục hồi dữ liệu từ bản sao lưu lúc {file.CreatedAt:HH:mm dd/MM/yyyy}.\n\n" +
                $"Dữ liệu trước khi phục hồi được giữ ở bản \"{result.Value.SafetyCopy.FileName}\".\n\nỨng dụng sẽ khởi động lại để tải dữ liệu mới; hãy đăng nhập lại.",
                Title);
            View.RestartApplication();
        }
        finally
        {
            _busy = false;
        }
    }

    public Task CopySelectedAsync() => _selected is { } file ? CopyAsync(file) : Task.CompletedTask;

    /// <summary>Sao chép ra…: copies a backup to a USB stick or another drive.</summary>
    public async Task CopyAsync(BackupFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!ViewAlive || _busy)
        {
            return;
        }

        if (View.PickCopyTarget(file) is not { Length: > 0 } target
            || string.Equals(Path.GetFullPath(target), Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? error;
        _busy = true;
        try
        {
            error = await View.RunBusyAsync(() => CopyFileAsync(file.FullPath, target), "Đang sao chép…");
        }
        finally
        {
            _busy = false;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (error is not null)
        {
            View.ShowWarning("Không sao chép được. Kiểm tra USB/ổ đĩa còn chỗ trống và cho phép ghi.", Title, error);
            return;
        }

        View.ToastSuccess($"Đã sao chép {file.FileName} ra {Path.GetDirectoryName(target)}.", Title);
    }

    /// <summary>Xóa (Delete): confirms in red (stronger wording for a manual backup), deletes, keeps the neighbouring row selected.</summary>
    public async Task DeleteSelectedAsync()
    {
        if (_selected is not { } file || _busy)
        {
            return;
        }

        var warning = file.Kind == BackupKind.Manual
            ? "Bản sao lưu thủ công này sẽ không thể khôi phục lại."
            : "Thao tác này không hoàn tác được.";
        if (!View.Confirm($"Xóa bản sao lưu lúc {file.CreatedAt:HH:mm dd/MM/yyyy} ({file.SizeText})?\n{warning}", Title, "Xóa", danger: true))
        {
            return;
        }

        var files = _overview?.Files ?? [];
        var index = IndexOf(files, file);
        var neighbour = index < 0 ? null
            : index + 1 < files.Count ? files[index + 1]
            : index > 0 ? files[index - 1]
            : null;

        Result result;
        _busy = true;
        try
        {
            result = await UseAsync<IBackupService, Result>(s => s.DeleteAsync(file.FileName));
        }
        finally
        {
            _busy = false;
        }

        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            await LoadAsync();
            return;
        }

        _inspections.Remove(file.FullPath);
        View.ToastSuccess($"Đã xóa {file.FileName}.", Title);
        _selected = null;
        await LoadAsync(select: neighbour?.FileName);
    }

    /// <summary>Mở thư mục: opens the backup folder in Explorer.</summary>
    public void OpenFolder()
    {
        if (_overview is { } overview && View.OpenFolder(overview.Folder) is { } error)
        {
            View.ToastWarning($"Không mở được thư mục: {error}", Title);
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    private void ShowOverview(BackupOverview overview, string? select)
    {
        _overview = overview;
        View.ShowHeader(BackupHeader.From(overview, Now));
        var selected = select is null ? null : overview.Files.FirstOrDefault(f => string.Equals(f.FileName, select, StringComparison.OrdinalIgnoreCase));
        _ = SelectAsync(View.ShowFiles(overview.Files, selected));
    }

    private BackupActions ActionsFor(BackupFile? file) =>
        file is null || !CanManage
            ? BackupActions.None
            : new BackupActions(CanInspect: true, CanRestore: SupportsRestore, CanCopy: true, CanDelete: true);

    private BackupInspection? Cached(BackupFile file) =>
        _inspections.TryGetValue(file.FullPath, out var entry) && entry.File == file ? entry.Inspection : null;

    private async Task<BackupInspection?> InspectAsync(BackupFile file, bool overlay, bool useCache = true)
    {
        if (useCache && Cached(file) is { } cached)
        {
            return cached;
        }

        var version = ++_inspectVersion;
        Func<Task<Result<BackupInspection>>> work = () => UseAsync<IBackupService, Result<BackupInspection>>(s => s.InspectAsync(file.FullPath));
        var result = overlay ? await View.RunBusyAsync(work, "Đang kiểm tra bản sao lưu…") : await work();
        if (!ViewAlive)
        {
            return null;
        }

        var inspection = result.IsSuccess ? result.Value : BackupInspection.Invalid(result.Error.Message);
        if (result.IsSuccess)
        {
            _inspections[file.FullPath] = (file, inspection);
        }

        // A newer selection's check owns the detail card; a restore still gets its answer.
        if (version == _inspectVersion && _selected is { } selected && SameFile(selected, file))
        {
            View.ShowDetail(BackupDetail.Inspected(file, inspection, Now));
        }

        return overlay || version == _inspectVersion ? inspection : null;
    }

    private static bool SameFile(BackupFile a, BackupFile b) => string.Equals(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<BackupFile> files, BackupFile file)
    {
        for (var i = 0; i < files.Count; i++)
        {
            if (SameFile(files[i], file))
            {
                return i;
            }
        }

        return -1;
    }

    private static Task<string?> CopyFileAsync(string source, string target) => Task.Run<string?>(() =>
    {
        try
        {
            File.Copy(source, target, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    });
}
