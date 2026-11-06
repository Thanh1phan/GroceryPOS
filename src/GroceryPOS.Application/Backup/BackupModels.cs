using System.Globalization;

namespace GroceryPOS.Application.Backup;

/// <summary>Why a backup file was made; encoded in its file name.</summary>
public enum BackupKind
{
    /// <summary>"Sao lưu ngay" on the Sao lưu dữ liệu screen.</summary>
    Manual = 0,

    /// <summary>Made at start-up when the last backup is older than the configured interval.</summary>
    Automatic = 1,

    /// <summary>Safety copy of the live database taken just before a restore overwrites it.</summary>
    BeforeRestore = 2,
}

/// <summary>Bound from the <c>Backup</c> section of appsettings.json.</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Backup folder; empty = <c>%LocalAppData%\GroceryPOS\Backups</c>.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>Automatic backup at start-up when the newest backup is at least this old; 0 turns it off.</summary>
    public int AutomaticIntervalHours { get; set; } = 24;

    /// <summary>Automatic backups kept (older ones are deleted after each backup); manual backups are never pruned.</summary>
    public int KeepAutomatic { get; set; } = 14;

    /// <summary>"Trước khi phục hồi" safety copies kept.</summary>
    public int KeepBeforeRestore { get; set; } = 5;

    public string ResolveFolder() =>
        string.IsNullOrWhiteSpace(Folder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GroceryPOS", "Backups")
            : Environment.ExpandEnvironmentVariables(Folder.Trim());
}

/// <summary>A backup file found in the backup folder.</summary>
public sealed record BackupFile(string FileName, string FullPath, BackupKind Kind, DateTime CreatedAt, long SizeBytes)
{
    public string KindText => BackupText.KindName(Kind);

    public string SizeText => BackupText.FormatSize(SizeBytes);

    /// <summary>Describes a file on disk: kind and time from its name, or Manual + last write time for other names.</summary>
    public static BackupFile FromPath(string path)
    {
        var info = new FileInfo(path);
        return BackupFileName.TryParse(info.Name, out var kind, out var createdAt)
            ? new BackupFile(info.Name, info.FullName, kind, createdAt, info.Exists ? info.Length : 0)
            : new BackupFile(info.Name, info.FullName, BackupKind.Manual, info.Exists ? info.LastWriteTime : default, info.Exists ? info.Length : 0);
    }
}

/// <summary>How the schema of a backup compares with the migrations this version of the app knows.</summary>
public enum BackupSchemaCompatibility
{
    /// <summary>Same migrations as the app.</summary>
    Current = 0,

    /// <summary>Made by an older version; restoring upgrades it (pending migrations are applied).</summary>
    Older = 1,

    /// <summary>Made by a newer version; cannot be restored by this app.</summary>
    Newer = 2,

    /// <summary>The provider cannot read the backup's content (e.g. SQL Server .bak files).</summary>
    Unknown = 3,
}

/// <summary>What a backup file contains, read without touching the live database.</summary>
public sealed record BackupInspection(
    bool IsValid,
    string? Problem,
    BackupSchemaCompatibility Schema,
    string? LatestMigration,
    int? Products,
    int? Customers,
    int? Sales,
    DateTimeOffset? LastSaleAt)
{
    public static BackupInspection Invalid(string problem) =>
        new(false, problem, BackupSchemaCompatibility.Unknown, null, null, null, null, null);

    /// <summary>Restorable by this app: readable and not made by a newer version.</summary>
    public bool CanRestore => IsValid && Schema is BackupSchemaCompatibility.Current or BackupSchemaCompatibility.Older;
}

/// <summary>Everything the Sao lưu dữ liệu screen shows.</summary>
public sealed record BackupOverview(
    string Folder,
    string ProviderName,
    bool SupportsRestore,
    int AutomaticIntervalHours,
    IReadOnlyList<BackupFile> Files)
{
    public BackupFile? Latest => Files.Count == 0 ? null : Files[0];

    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}

/// <summary>Result of a restore: the safety copy taken before it and the file restored.</summary>
public sealed record RestoreOutcome(BackupFile Restored, BackupFile SafetyCopy, BackupInspection Inspection);

/// <summary>
/// File naming of backups: <c>grocerypos-20261106-213005-manual.db</c>. The timestamp is local time so the names sort
/// and read naturally in Explorer; the kind lets retention prune automatic copies without touching manual ones.
/// </summary>
public static class BackupFileName
{
    public const string Prefix = "grocerypos-";
    private const string StampFormat = "yyyyMMdd-HHmmss";

    public static string Build(BackupKind kind, DateTime localTime, string extension) =>
        $"{Prefix}{localTime.ToString(StampFormat, CultureInfo.InvariantCulture)}-{KindToken(kind)}{NormalizeExtension(extension)}";

    public static bool TryParse(string fileName, out BackupKind kind, out DateTime createdAt)
    {
        kind = BackupKind.Manual;
        createdAt = default;
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = name[Prefix.Length..];
        if (rest.Length < StampFormat.Length + 2 || rest[StampFormat.Length] != '-')
        {
            return false;
        }

        if (!DateTime.TryParseExact(rest[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out createdAt))
        {
            return false;
        }

        var token = rest[(StampFormat.Length + 1)..];
        var dash = token.IndexOf('-');
        if (dash >= 0)
        {
            // "-2", "-3"… added by Unique when two backups share a second.
            if (!int.TryParse(token[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            token = token[..dash];
        }

        BackupKind? parsed = token.ToLowerInvariant() switch
        {
            "manual" => BackupKind.Manual,
            "auto" => BackupKind.Automatic,
            "prerestore" => BackupKind.BeforeRestore,
            _ => null,
        };
        if (parsed is null)
        {
            return false;
        }

        kind = parsed.Value;
        return true;
    }

    /// <summary>A name not yet used in <paramref name="folder"/> (adds "-2", "-3"… when two backups share a second).</summary>
    public static string Unique(string folder, BackupKind kind, DateTime localTime, string extension)
    {
        var name = Build(kind, localTime, extension);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 2; File.Exists(Path.Combine(folder, name)); n++)
        {
            name = $"{stem}-{n}{ext}";
        }

        return name;
    }

    private static string KindToken(BackupKind kind) => kind switch
    {
        BackupKind.Automatic => "auto",
        BackupKind.BeforeRestore => "prerestore",
        _ => "manual",
    };

    private static string NormalizeExtension(string extension) =>
        string.IsNullOrEmpty(extension) ? ".db" : extension.StartsWith('.') ? extension : "." + extension;
}

/// <summary>Which backups to delete and when the next automatic backup is due.</summary>
public static class BackupPolicy
{
    /// <summary>
    /// Files beyond the newest <see cref="BackupOptions.KeepAutomatic"/> automatic and newest
    /// <see cref="BackupOptions.KeepBeforeRestore"/> safety copies. Manual backups are never selected.
    /// </summary>
    public static IReadOnlyList<BackupFile> SelectForDeletion(IEnumerable<BackupFile> files, BackupOptions options)
    {
        var all = files.ToList();
        return Excess(all, BackupKind.Automatic, options.KeepAutomatic)
            .Concat(Excess(all, BackupKind.BeforeRestore, options.KeepBeforeRestore))
            .ToList();
    }

    /// <summary>True when automatic backups are on and no backup of any kind is younger than the interval.</summary>
    public static bool IsAutomaticBackupDue(DateTime? latestBackup, DateTime now, int intervalHours) =>
        intervalHours > 0 && (latestBackup is null || now - latestBackup.Value >= TimeSpan.FromHours(intervalHours));

    private static IEnumerable<BackupFile> Excess(List<BackupFile> files, BackupKind kind, int keep) =>
        files.Where(f => f.Kind == kind)
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(keep, 1));
}
