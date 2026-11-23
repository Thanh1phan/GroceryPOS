using GroceryPOS.Application.Backup;
using GroceryPOS.Application.Common;

namespace GroceryPOS.Presentation.Tests;

internal sealed class FakeBackupService : IBackupService
{
    public string Folder { get; set; } = Path.Combine(Path.GetTempPath(), "grocerypos-backup-tests");
    public bool SupportsRestore { get; set; } = true;
    public List<BackupFile> Files { get; } = new();
    public List<string> Calls { get; } = new();
    public Error? OverviewError { get; set; }
    public Error? CreateError { get; set; }
    public Error? RestoreError { get; set; }
    public Error? DeleteError { get; set; }
    public Error? InspectError { get; set; }

    /// <summary>Inspection returned per path; a valid current-schema backup otherwise.</summary>
    public Dictionary<string, BackupInspection> Inspections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, InspectAsync waits for it (to test "latest wins").</summary>
    public Func<string, Task>? InspectGate { get; set; }

    public static BackupInspection Good { get; } = new(true, null, BackupSchemaCompatibility.Current, "m1", 120, 30, 900, null);

    public BackupFile Add(BackupKind kind, DateTime createdAt, long size = 2048)
    {
        var name = BackupFileName.Build(kind, createdAt, ".db");
        var file = new BackupFile(name, Path.Combine(Folder, name), kind, createdAt, size);
        Files.Add(file);
        Files.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return file;
    }

    public Task<Result<BackupOverview>> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("overview");
        return Task.FromResult(OverviewError is { } e
            ? Result.Failure<BackupOverview>(e)
            : Result.Success(new BackupOverview(Folder, "SQLite", SupportsRestore, 24, Files.ToList())));
    }

    public Task<Result<BackupFile>> CreateAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("create");
        if (CreateError is { } e)
        {
            return Task.FromResult(Result.Failure<BackupFile>(e));
        }

        var latest = Files.Count == 0 ? new DateTime(2026, 11, 20, 9, 0, 0) : Files[0].CreatedAt;
        return Task.FromResult(Result.Success(Add(BackupKind.Manual, latest.AddMinutes(30))));
    }

    public Task<Result<BackupFile?>> RunAutomaticIfDueAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<BackupFile?>(null));

    public async Task<Result<BackupInspection>> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        Calls.Add("inspect:" + Path.GetFileName(path));
        if (InspectGate is { } gate)
        {
            await gate(path);
        }

        return InspectError is { } e
            ? Result.Failure<BackupInspection>(e)
            : Result.Success(Inspections.GetValueOrDefault(path) ?? Good);
    }

    public Task<Result<RestoreOutcome>> RestoreAsync(string path, CancellationToken cancellationToken = default)
    {
        Calls.Add("restore:" + Path.GetFileName(path));
        if (RestoreError is { } e)
        {
            return Task.FromResult(Result.Failure<RestoreOutcome>(e));
        }

        var safety = new BackupFile("grocerypos-20261120-100000-prerestore.db", Path.Combine(Folder, "grocerypos-20261120-100000-prerestore.db"), BackupKind.BeforeRestore, new DateTime(2026, 11, 20, 10, 0, 0), 4096);
        return Task.FromResult(Result.Success(new RestoreOutcome(BackupFile.FromPath(path), safety, Good)));
    }

    public Task<Result> DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        Calls.Add("delete:" + fileName);
        if (DeleteError is { } e)
        {
            return Task.FromResult(Result.Failure(e));
        }

        Files.RemoveAll(f => f.FileName == fileName);
        return Task.FromResult(Result.Success());
    }
}
