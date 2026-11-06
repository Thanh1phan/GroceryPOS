using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Backup;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public class BackupFileNameTests
{
    [Fact]
    public void Names_round_trip_kind_and_local_time()
    {
        var at = new DateTime(2026, 11, 6, 21, 30, 5);

        var name = BackupFileName.Build(BackupKind.Automatic, at, ".db");

        Assert.Equal("grocerypos-20261106-213005-auto.db", name);
        Assert.True(BackupFileName.TryParse(name, out var kind, out var parsed));
        Assert.Equal(BackupKind.Automatic, kind);
        Assert.Equal(at, parsed);
        Assert.Equal("grocerypos-20261106-213005-prerestore.bak", BackupFileName.Build(BackupKind.BeforeRestore, at, "bak"));
    }

    [Theory]
    [InlineData("grocerypos-20261106-213005-manual-2.db", true)]
    [InlineData("GROCERYPOS-20261106-213005-MANUAL.db", true)]
    [InlineData("grocerypos-20261106-213005-weekly.db", false)]
    [InlineData("grocerypos-20261306-213005-manual.db", false)]
    [InlineData("grocerypos-20261106-213005-manual-x.db", false)]
    [InlineData("grocerypos.db", false)]
    [InlineData("backup-20261106-213005-manual.db", false)]
    public void Only_names_of_the_scheme_parse(string name, bool expected) =>
        Assert.Equal(expected, BackupFileName.TryParse(name, out _, out _));

    [Fact]
    public void Unique_adds_a_counter_when_two_backups_share_a_second()
    {
        var folder = Directory.CreateTempSubdirectory("grocerypos-backup-").FullName;
        try
        {
            var at = new DateTime(2026, 11, 6, 8, 0, 0);
            File.WriteAllText(Path.Combine(folder, BackupFileName.Build(BackupKind.Manual, at, ".db")), "x");

            var name = BackupFileName.Unique(folder, BackupKind.Manual, at, ".db");

            Assert.Equal("grocerypos-20261106-080000-manual-2.db", name);
            Assert.True(BackupFileName.TryParse(name, out var kind, out _));
            Assert.Equal(BackupKind.Manual, kind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class BackupPolicyTests
{
    private static BackupFile File(BackupKind kind, int day) =>
        new($"f-{kind}-{day}", $"/b/f-{kind}-{day}", kind, new DateTime(2026, 11, day, 8, 0, 0), 100);

    [Fact]
    public void Retention_keeps_every_manual_backup_and_the_newest_automatic_and_safety_copies()
    {
        var files = Enumerable.Range(1, 6).Select(d => File(BackupKind.Automatic, d))
            .Concat(Enumerable.Range(1, 4).Select(d => File(BackupKind.BeforeRestore, d)))
            .Concat(Enumerable.Range(1, 9).Select(d => File(BackupKind.Manual, d)))
            .ToList();

        var deleted = BackupPolicy.SelectForDeletion(files, new BackupOptions { KeepAutomatic = 3, KeepBeforeRestore = 2 });

        Assert.Equal(
            ["f-Automatic-3", "f-Automatic-2", "f-Automatic-1", "f-BeforeRestore-2", "f-BeforeRestore-1"],
            deleted.Select(f => f.FileName).ToArray());
    }

    [Fact]
    public void A_zero_keep_count_still_keeps_the_newest_copy()
    {
        var files = new[] { File(BackupKind.Automatic, 1), File(BackupKind.Automatic, 2) };

        var deleted = BackupPolicy.SelectForDeletion(files, new BackupOptions { KeepAutomatic = 0 });

        Assert.Equal("f-Automatic-1", Assert.Single(deleted).FileName);
    }

    [Theory]
    [InlineData(null, 24, true)]
    [InlineData(23.5, 24, false)]
    [InlineData(24.0, 24, true)]
    [InlineData(100.0, 0, false)]
    [InlineData(null, 0, false)]
    public void Automatic_backup_is_due_when_the_newest_backup_is_older_than_the_interval(double? hoursAgo, int interval, bool due)
    {
        var now = new DateTime(2026, 11, 6, 9, 0, 0);
        DateTime? latest = hoursAgo is { } h ? now.AddHours(-h) : null;

        Assert.Equal(due, BackupPolicy.IsAutomaticBackupDue(latest, now, interval));
    }
}

public class BackupTextTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 9, 0, 0);

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1,5 KB")]
    [InlineData(1_258_291L, "1,2 MB")]
    [InlineData(157_286_400L, "150 MB")]
    [InlineData(2_523_490_000L, "2,35 GB")]
    public void Sizes_use_binary_units_and_a_decimal_comma(long bytes, string expected) =>
        Assert.Equal(expected, BackupText.FormatSize(bytes));

    [Fact]
    public void Ages_read_naturally()
    {
        Assert.Equal("vừa xong", BackupText.Age(Now.AddSeconds(-20), Now));
        Assert.Equal("15 phút trước", BackupText.Age(Now.AddMinutes(-15), Now));
        Assert.Equal("3 giờ trước", BackupText.Age(Now.AddHours(-3), Now));
        Assert.Equal("hôm qua", BackupText.Age(Now.AddHours(-30), Now));
        Assert.Equal("5 ngày trước", BackupText.Age(Now.AddDays(-5), Now));
    }

    [Fact]
    public void Health_and_advice_follow_the_age_of_the_newest_backup()
    {
        Assert.Equal(BackupHealth.Danger, BackupText.Health(null, Now));
        Assert.Equal("Chưa có bản sao lưu", BackupText.Status(null, Now));
        Assert.Contains("chưa từng được sao lưu", BackupText.Advice(null, Now));

        Assert.Equal(BackupHealth.Ok, BackupText.Health(Now.AddHours(-20), Now));
        Assert.Null(BackupText.Advice(Now.AddHours(-20), Now));
        Assert.Equal("Sao lưu 20 giờ trước", BackupText.Status(Now.AddHours(-20), Now));

        Assert.Equal(BackupHealth.Warning, BackupText.Health(Now.AddDays(-3), Now));
        Assert.Equal(BackupHealth.Danger, BackupText.Health(Now.AddDays(-8), Now));
        Assert.Contains("8 ngày", BackupText.Advice(Now.AddDays(-8), Now));
    }

    [Fact]
    public void Schedule_and_inspection_are_described_in_vietnamese()
    {
        Assert.Equal("Tự động sao lưu mỗi ngày khi mở ứng dụng", BackupText.Schedule(24));
        Assert.Equal("Tự động sao lưu mỗi 2 ngày khi mở ứng dụng", BackupText.Schedule(48));
        Assert.StartsWith("Tắt sao lưu tự động", BackupText.Schedule(0));

        var lines = BackupText.Describe(new BackupInspection(true, null, BackupSchemaCompatibility.Older, "m2", 1250, 80, 15_300, null));
        Assert.Contains("✓ File nguyên vẹn", lines);
        Assert.Contains("Sản phẩm: 1.250", lines);
        Assert.Contains("Hóa đơn: 15.300", lines);
        Assert.Contains(lines, l => l.Contains("sẽ được nâng cấp"));

        Assert.StartsWith("✗", Assert.Single(BackupText.Describe(BackupInspection.Invalid("hỏng"))));
    }
}

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("grocerypos-backup-").FullName;
    private readonly FakeClock _clock = new() { Now = new DateTimeOffset(2026, 11, 6, 2, 0, 0, TimeSpan.Zero) };
    private readonly FakeBackupEngine _engine = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly BackupOptions _options;

    public BackupServiceTests() => _options = new BackupOptions { Folder = _folder, KeepAutomatic = 2, KeepBeforeRestore = 1 };

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DateTime LocalNow => _clock.GetLocalNow().DateTime;

    private BackupService CreateService(params string[] permissions)
    {
        if (permissions.Length > 0)
        {
            _session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), _clock.Now));
        }

        return new BackupService(_engine, _options, _session, _audit, _uow, _clock);
    }

    private string Existing(BackupKind kind, DateTime at)
    {
        var path = Path.Combine(_folder, BackupFileName.Build(kind, at, ".db"));
        File.WriteAllText(path, "old");
        return path;
    }

    [Fact]
    public async Task Every_operation_except_the_start_up_backup_needs_the_backup_permission()
    {
        var anonymous = CreateService();
        Assert.Equal("auth.required", (await anonymous.GetOverviewAsync()).Error.Code);

        var cashier = CreateService(Permissions.SalesCreate, Permissions.SettingsManage);
        Assert.Equal(BackupErrors.Denied, (await cashier.GetOverviewAsync()).Error);
        Assert.Equal(BackupErrors.Denied, (await cashier.CreateAsync()).Error);
        Assert.Equal(BackupErrors.Denied, (await cashier.InspectAsync("x.db")).Error);
        Assert.Equal(BackupErrors.Denied, (await cashier.RestoreAsync("x.db")).Error);
        Assert.Equal(BackupErrors.Denied, (await cashier.DeleteAsync("x.db")).Error);
        Assert.Empty(_engine.Created);
    }

    [Fact]
    public async Task Manual_backup_is_written_to_the_folder_audited_and_listed_first()
    {
        Existing(BackupKind.Automatic, LocalNow.AddDays(-1));
        var sut = CreateService(Permissions.SystemBackup);

        var result = await sut.CreateAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupKind.Manual, result.Value.Kind);
        Assert.Equal(LocalNow, result.Value.CreatedAt);
        Assert.True(File.Exists(result.Value.FullPath));
        Assert.Equal(_folder, Path.GetDirectoryName(result.Value.FullPath));
        Assert.Equal(AuditActions.BackupCreated, Assert.Single(_audit.Entries).Action);
        Assert.Equal(1, _uow.SaveCount);

        var overview = (await sut.GetOverviewAsync()).Value;
        Assert.Equal(2, overview.Files.Count);
        Assert.Equal(result.Value.FileName, overview.Latest!.FileName);
        Assert.Equal("Fake", overview.ProviderName);
    }

    [Fact]
    public async Task Overview_ignores_files_that_are_not_backups_and_creates_nothing()
    {
        File.WriteAllText(Path.Combine(_folder, "ghi-chu.db"), "x");
        File.WriteAllText(Path.Combine(_folder, "grocerypos-20261101-080000-manual.txt"), "x");
        var sut = CreateService(Permissions.SystemBackup);

        Assert.Empty((await sut.GetOverviewAsync()).Value.Files);

        Directory.Delete(_folder, recursive: true);
        Assert.Empty((await sut.GetOverviewAsync()).Value.Files);
        Assert.False(Directory.Exists(_folder));
        Directory.CreateDirectory(_folder); // for Dispose
    }

    [Fact]
    public async Task Start_up_backup_runs_without_a_session_only_when_due_and_prunes_old_automatic_copies()
    {
        var oldest = Existing(BackupKind.Automatic, LocalNow.AddDays(-3));
        Existing(BackupKind.Automatic, LocalNow.AddDays(-2));
        var manual = Existing(BackupKind.Manual, LocalNow.AddDays(-10));
        var sut = CreateService();

        var first = await sut.RunAutomaticIfDueAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal(BackupKind.Automatic, first.Value!.Kind);
        Assert.False(File.Exists(oldest)); // keep 2 automatic copies
        Assert.True(File.Exists(manual));
        Assert.Empty(_audit.Entries);

        _clock.Advance(TimeSpan.FromHours(5));
        var second = await sut.RunAutomaticIfDueAsync();
        Assert.True(second.IsSuccess);
        Assert.Null(second.Value);
        Assert.Single(_engine.Created);
    }

    [Fact]
    public async Task Start_up_backup_is_off_when_the_interval_is_zero()
    {
        _options.AutomaticIntervalHours = 0;

        var result = await CreateService().RunAutomaticIfDueAsync();

        Assert.Null(result.Value);
        Assert.Empty(_engine.Created);
    }

    [Fact]
    public async Task A_failing_engine_returns_an_error_and_leaves_no_partial_file()
    {
        _engine.FailCreate = true;
        var sut = CreateService(Permissions.SystemBackup);

        var result = await sut.CreateAsync();

        Assert.Equal("backup.failed", result.Error.Code);
        Assert.Contains("đĩa đầy", result.Error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Restore_keeps_a_safety_copy_restores_and_audits_in_the_restored_database()
    {
        var source = Existing(BackupKind.Manual, new DateTime(2026, 11, 1, 20, 15, 0));
        var sut = CreateService(Permissions.SystemBackup);

        var result = await sut.RestoreAsync(source);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupKind.BeforeRestore, result.Value.SafetyCopy.Kind);
        Assert.True(File.Exists(result.Value.SafetyCopy.FullPath));
        Assert.Equal(source, Assert.Single(_engine.Restored));
        Assert.True(_engine.Created.Count == 1 && _engine.Created[0] == result.Value.SafetyCopy.FullPath);

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(AuditActions.BackupRestored, audit.Action);
        Assert.Equal("admin", audit.Username);
        Assert.Contains("20:15 01/11/2026", audit.Details);
        Assert.Contains(result.Value.SafetyCopy.FileName, audit.Details);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Theory]
    [InlineData(false, BackupSchemaCompatibility.Current, "không dùng được")]
    [InlineData(true, BackupSchemaCompatibility.Newer, "mới hơn")]
    public async Task Restore_refuses_unreadable_or_newer_backups_before_touching_anything(bool valid, BackupSchemaCompatibility schema, string message)
    {
        var source = Existing(BackupKind.Manual, LocalNow.AddDays(-1));
        _engine.Inspection = new BackupInspection(valid, valid ? null : "hỏng", schema, null, null, null, null, null);
        var sut = CreateService(Permissions.SystemBackup);

        var result = await sut.RestoreAsync(source);

        Assert.Contains(message, result.Error.Message);
        Assert.Empty(_engine.Created);
        Assert.Empty(_engine.Restored);
    }

    [Fact]
    public async Task Restore_reports_a_missing_file_and_an_engine_without_restore()
    {
        var sut = CreateService(Permissions.SystemBackup);
        Assert.Equal(BackupErrors.NotFound, (await sut.RestoreAsync(Path.Combine(_folder, "khong-co.db"))).Error);

        _engine.CanRestore = false;
        Assert.Equal(BackupErrors.RestoreNotSupported, (await sut.RestoreAsync(Existing(BackupKind.Manual, LocalNow))).Error);
    }

    [Fact]
    public async Task Delete_accepts_only_backup_names_of_the_folder()
    {
        var path = Existing(BackupKind.Manual, LocalNow.AddDays(-1));
        var outside = Path.Combine(Path.GetDirectoryName(_folder)!, "grocerypos-20261101-080000-manual.db");
        var sut = CreateService(Permissions.SystemBackup);

        Assert.Equal(BackupErrors.NotFound, (await sut.DeleteAsync("../" + Path.GetFileName(outside))).Error);
        Assert.Equal(BackupErrors.NotFound, (await sut.DeleteAsync(path)).Error); // full paths are rejected too
        File.WriteAllText(Path.Combine(_folder, "notes.db"), "x");
        Assert.Equal(BackupErrors.NotFound, (await sut.DeleteAsync("notes.db")).Error);

        var deleted = await sut.DeleteAsync(Path.GetFileName(path));

        Assert.True(deleted.IsSuccess);
        Assert.False(File.Exists(path));
        Assert.Equal(AuditActions.BackupDeleted, Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    public async Task Inspect_turns_engine_exceptions_into_an_invalid_result()
    {
        var path = Existing(BackupKind.Manual, LocalNow);
        _engine.FailInspect = true;

        var result = await CreateService(Permissions.SystemBackup).InspectAsync(path);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanRestore);
        Assert.Equal("không đọc được", result.Value.Problem);
    }

    private sealed class FakeBackupEngine : IDatabaseBackupEngine
    {
        public List<string> Created { get; } = [];

        public List<string> Restored { get; } = [];

        public bool FailCreate { get; set; }

        public bool FailInspect { get; set; }

        public bool CanRestore { get; set; } = true;

        public BackupInspection Inspection { get; set; } =
            new(true, null, BackupSchemaCompatibility.Current, "20261105_AddAppSettings", 10, 2, 30, null);

        public string ProviderName => "Fake";

        public string FileExtension => ".db";

        public bool SupportsRestore => CanRestore;

        public async Task CreateAsync(string destinationPath, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(destinationPath, "partial", cancellationToken);
            if (FailCreate)
            {
                throw new IOException("đĩa đầy");
            }

            Created.Add(destinationPath);
        }

        public Task<BackupInspection> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            FailInspect ? throw new InvalidDataException("không đọc được") : Task.FromResult(Inspection);

        public Task RestoreAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            Restored.Add(sourcePath);
            return Task.CompletedTask;
        }
    }
}
