using System.Globalization;
using GroceryPOS.Application.Backup;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Backup;

/// <summary>Header of the Sao lưu dữ liệu screen: freshness badge, subtitle, advice line and the three KPI cards.</summary>
public sealed record BackupHeader(
    string StatusText,
    Tone StatusTone,
    string Subtitle,
    string? Advice,
    Tone AdviceTone,
    string LatestValue,
    string LatestNote,
    string CountValue,
    string CountNote,
    string DatabaseValue,
    string DatabaseNote,
    bool CanRestoreFromFile)
{
    public static readonly BackupHeader Loading = Placeholder("Đang tải…", Tone.Muted, string.Empty);

    public static BackupHeader Failed(string message) => Placeholder("Không tải được", Tone.Danger, message);

    public static BackupHeader From(BackupOverview overview, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(overview);
        var latest = overview.Latest;
        var health = BackupText.Health(latest?.CreatedAt, now);
        return new BackupHeader(
            BackupText.Status(latest?.CreatedAt, now),
            health switch
            {
                BackupHealth.Ok => Tone.Success,
                BackupHealth.Warning => Tone.Warning,
                _ => Tone.Danger,
            },
            $"{BackupText.Schedule(overview.AutomaticIntervalHours)} · Thư mục: {overview.Folder}",
            BackupText.Advice(latest?.CreatedAt, now),
            health == BackupHealth.Danger ? Tone.Danger : Tone.Warning,
            latest is null ? "Chưa có" : BackupText.Age(latest.CreatedAt, now),
            latest is null ? "Hãy sao lưu ngay" : $"{latest.CreatedAt:HH:mm dd/MM/yyyy} · {latest.KindText}",
            overview.Files.Count.ToString(CultureInfo.InvariantCulture),
            $"Tổng {BackupText.FormatSize(overview.TotalBytes)}",
            overview.ProviderName,
            overview.SupportsRestore ? "Sao lưu & phục hồi trong ứng dụng" : "Phục hồi bằng SQL Server Management Studio",
            overview.SupportsRestore);
    }

    private static BackupHeader Placeholder(string status, Tone tone, string subtitle) =>
        new(status, tone, subtitle, null, Tone.Warning, "–", string.Empty, "–", string.Empty, "–", string.Empty, false);
}

/// <summary>The detail card of the selected backup: a title with a verdict color and the lines describing it.</summary>
public sealed record BackupDetail(string Title, Tone TitleTone, IReadOnlyList<string> Lines)
{
    public const string Checking = "Đang kiểm tra…";

    public static readonly BackupDetail None = new(
        "Chọn một bản sao lưu",
        Tone.Muted,
        ["Chọn một dòng để xem nội dung bản sao lưu (số sản phẩm, khách hàng, hóa đơn) trước khi phục hồi."]);

    /// <summary>The file's facts and "Đang kiểm tra…" while it is being read.</summary>
    public static BackupDetail Pending(BackupFile file, DateTime now) => Build(file, null, now);

    /// <summary>The file's facts and what the check found; the title is green (restorable), amber (readable only) or red.</summary>
    public static BackupDetail Inspected(BackupFile file, BackupInspection inspection, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Build(file, inspection, now);
    }

    private static BackupDetail Build(BackupFile file, BackupInspection? inspection, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lines = new List<string>
        {
            $"Tạo lúc: {file.CreatedAt:HH:mm:ss dd/MM/yyyy}",
            $"Loại: {file.KindText}",
            $"Kích thước: {file.SizeText}",
            $"File: {file.FileName}",
            string.Empty,
        };
        lines.AddRange(inspection is null ? [Checking] : BackupText.Describe(inspection));
        var tone = inspection is null ? Tone.Normal
            : inspection.CanRestore ? Tone.Success
            : inspection.IsValid ? Tone.Warning
            : Tone.Danger;
        return new BackupDetail(BackupText.Age(file.CreatedAt, now) + " · " + file.KindText, tone, lines);
    }
}

/// <summary>Which list actions are available for the current selection.</summary>
public sealed record BackupActions(bool CanInspect, bool CanRestore, bool CanCopy, bool CanDelete)
{
    public static readonly BackupActions None = new(false, false, false, false);
}
