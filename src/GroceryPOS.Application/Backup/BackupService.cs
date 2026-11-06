using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Backup;

public interface IBackupService
{
    /// <summary>Backup folder, provider, schedule and the backups found, newest first (<c>system.backup</c>).</summary>
    Task<Result<BackupOverview>> GetOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>"Sao lưu ngay": a manual backup, audited as <c>backup.created</c>; old automatic copies are pruned.</summary>
    Task<Result<BackupFile>> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Start-up backup when the newest one is older than <see cref="BackupOptions.AutomaticIntervalHours"/>; runs before
    /// anyone signs in, so it needs no permission. Returns null when nothing was due.
    /// </summary>
    Task<Result<BackupFile?>> RunAutomaticIfDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks a backup file (in the backup folder or anywhere else, e.g. a USB stick) (<c>system.backup</c>).</summary>
    Task<Result<BackupInspection>> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the live data with the backup at <paramref name="path"/>: the file is checked first, the current data is
    /// saved as a "Trước khi phục hồi" copy, and the restore is audited (in the restored database) as <c>backup.restored</c>.
    /// The app must restart afterwards (<c>system.backup</c>).
    /// </summary>
    Task<Result<RestoreOutcome>> RestoreAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Deletes a backup from the backup folder, audited as <c>backup.deleted</c> (<c>system.backup</c>).</summary>
    Task<Result> DeleteAsync(string fileName, CancellationToken cancellationToken = default);
}

public static class BackupErrors
{
    public static readonly Error Denied = Error.Forbidden("Bạn không có quyền sao lưu hoặc phục hồi dữ liệu.");

    public static readonly Error NotFound = Error.NotFound("Không tìm thấy file sao lưu.");

    public static readonly Error RestoreNotSupported = Error.Validation(
        "Cơ sở dữ liệu SQL Server cần được phục hồi bằng SQL Server Management Studio (RESTORE DATABASE).");

    public static Error Failed(string action, Exception exception) =>
        new("backup.failed", $"Không {action} được: {exception.Message}");
}

/// <summary>Backups of the whole database into the backup folder, restore with a safety copy, and retention.</summary>
public sealed class BackupService : IBackupService
{
    private readonly IDatabaseBackupEngine _engine;
    private readonly BackupOptions _options;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public BackupService(
        IDatabaseBackupEngine engine,
        BackupOptions options,
        ICurrentUser currentUser,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        TimeProvider? clock = null)
    {
        _engine = engine;
        _options = options;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock ?? TimeProvider.System;
    }

    private string Folder => _options.ResolveFolder();

    private DateTime Now => _clock.GetLocalNow().DateTime;

    public Task<Result<BackupOverview>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return Task.FromResult<Result<BackupOverview>>(error);
        }

        Result<BackupOverview> overview = new BackupOverview(
            Folder, _engine.ProviderName, _engine.SupportsRestore, _options.AutomaticIntervalHours, ListFiles());
        return Task.FromResult(overview);
    }

    public async Task<Result<BackupFile>> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        var created = await TryBackupAsync(BackupKind.Manual, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        _audit.Record(AuditActions.BackupCreated, "Backup", created.Value.FileName, $"Sao lưu thủ công ({created.Value.SizeText}) vào {Folder}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        Prune();
        return created.Value;
    }

    public async Task<Result<BackupFile?>> RunAutomaticIfDueAsync(CancellationToken cancellationToken = default)
    {
        var latest = ListFiles().FirstOrDefault();
        if (!BackupPolicy.IsAutomaticBackupDue(latest?.CreatedAt, Now, _options.AutomaticIntervalHours))
        {
            return (BackupFile?)null;
        }

        var created = await TryBackupAsync(BackupKind.Automatic, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        Prune();
        return created.Value;
    }

    public async Task<Result<BackupInspection>> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return BackupErrors.NotFound;
        }

        try
        {
            return await _engine.InspectAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BackupInspection.Invalid(ex.Message);
        }
    }

    public async Task<Result<RestoreOutcome>> RestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        if (!_engine.SupportsRestore)
        {
            return BackupErrors.RestoreNotSupported;
        }

        var inspected = await InspectAsync(path, cancellationToken);
        if (inspected.IsFailure)
        {
            return inspected.Error;
        }

        var inspection = inspected.Value;
        if (!inspection.CanRestore)
        {
            return Error.Validation(inspection.IsValid
                ? "Bản sao lưu được tạo bởi phiên bản GroceryPOS mới hơn. Hãy cập nhật ứng dụng trước khi phục hồi."
                : $"File sao lưu không dùng được: {inspection.Problem}");
        }

        var source = Describe(path);
        var username = _currentUser.Session!.Username;

        // Keep what is in the database now, so a wrong restore can itself be undone.
        var safety = await TryBackupAsync(BackupKind.BeforeRestore, cancellationToken);
        if (safety.IsFailure)
        {
            return safety.Error;
        }

        try
        {
            await _engine.RestoreAsync(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BackupErrors.Failed("phục hồi dữ liệu", ex);
        }

        // Written into the restored database, so the trail shows who replaced the data and with what.
        _audit.RecordFor(
            null,
            username,
            AuditActions.BackupRestored,
            $"Phục hồi từ {source.FileName} (lúc {source.CreatedAt:HH:mm dd/MM/yyyy}); dữ liệu trước đó lưu ở {safety.Value.FileName}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        Prune();
        return new RestoreOutcome(source, safety.Value, inspection);
    }

    public async Task<Result> DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (Authorize() is { } error)
        {
            return error;
        }

        if (ResolveInFolder(fileName) is not { } path)
        {
            return BackupErrors.NotFound;
        }

        var file = Describe(path);
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupErrors.Failed("xóa bản sao lưu", ex);
        }

        _audit.Record(AuditActions.BackupDeleted, "Backup", file.FileName, $"Xóa bản sao lưu {file.KindText.ToLowerInvariant()} lúc {file.CreatedAt:HH:mm dd/MM/yyyy}");
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Backups in the folder, newest first (files that do not follow the naming scheme are ignored).</summary>
    private List<BackupFile> ListFiles()
    {
        var folder = Folder;
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, BackupFileName.Prefix + "*" + _engine.FileExtension)
            .Where(p => BackupFileName.TryParse(Path.GetFileName(p), out _, out _))
            .Select(Describe)
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BackupFile Describe(string path) => BackupFile.FromPath(path);

    private string? ResolveInFolder(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return null; // no paths: only files of the backup folder can be deleted
        }

        var path = Path.Combine(Folder, fileName);
        return File.Exists(path) && BackupFileName.TryParse(fileName, out _, out _) ? path : null;
    }

    private async Task<Result<BackupFile>> TryBackupAsync(BackupKind kind, CancellationToken cancellationToken)
    {
        var folder = Folder;
        string? path = null;
        try
        {
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, BackupFileName.Unique(folder, kind, Now, _engine.FileExtension));
            await _engine.CreateAsync(path, cancellationToken);
            return Describe(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(path);
            return BackupErrors.Failed("sao lưu dữ liệu", ex);
        }
    }

    private void Prune()
    {
        foreach (var file in BackupPolicy.SelectForDeletion(ListFiles(), _options))
        {
            TryDelete(file.FullPath);
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a locked old copy is retried after the next backup.
        }
    }

    private Error? Authorize()
    {
        if (_currentUser.Session is null)
        {
            return AuthErrors.NotAuthenticated;
        }

        return _currentUser.HasPermission(Permissions.SystemBackup) ? null : BackupErrors.Denied;
    }
}
