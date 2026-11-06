using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Backup;

/// <summary>Freshness of the newest backup, for the status badge.</summary>
public enum BackupHealth
{
    Ok = 0,
    Warning = 1,
    Danger = 2,
}

/// <summary>Vietnamese texts of the Sao lưu dữ liệu screen (UI-free so they can be tested).</summary>
public static class BackupText
{
    /// <summary>Newest backup older than this shows a warning; older than <see cref="DangerAfter"/> shows red.</summary>
    public static readonly TimeSpan WarningAfter = TimeSpan.FromDays(2);

    public static readonly TimeSpan DangerAfter = TimeSpan.FromDays(7);

    public static string KindName(BackupKind kind) => kind switch
    {
        BackupKind.Automatic => "Tự động",
        BackupKind.BeforeRestore => "Trước khi phục hồi",
        _ => "Thủ công",
    };

    /// <summary>"512 B", "48 KB", "1,2 MB", "2,35 GB" (vi-VN decimal comma).</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{Math.Max(bytes, 0)} B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, Vnd.Culture) + " " + units[unit];
    }

    /// <summary>"vừa xong", "15 phút trước", "3 giờ trước", "hôm qua", "5 ngày trước".</summary>
    public static string Age(DateTime at, DateTime now)
    {
        var age = now - at;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "vừa xong";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} phút trước";
        }

        if (age < TimeSpan.FromHours(24))
        {
            return $"{(int)age.TotalHours} giờ trước";
        }

        var days = (now.Date - at.Date).Days;
        return days <= 1 ? "hôm qua" : $"{days} ngày trước";
    }

    public static BackupHealth Health(DateTime? latest, DateTime now) =>
        latest is null ? BackupHealth.Danger
        : now - latest.Value >= DangerAfter ? BackupHealth.Danger
        : now - latest.Value >= WarningAfter ? BackupHealth.Warning
        : BackupHealth.Ok;

    /// <summary>Badge text: "Sao lưu 3 giờ trước", "Chưa có bản sao lưu".</summary>
    public static string Status(DateTime? latest, DateTime now) =>
        latest is null ? "Chưa có bản sao lưu" : $"Sao lưu {Age(latest.Value, now)}";

    /// <summary>Advice under the badge, or null when backups are fresh.</summary>
    public static string? Advice(DateTime? latest, DateTime now) => Health(latest, now) switch
    {
        BackupHealth.Danger when latest is null => "Dữ liệu bán hàng chưa từng được sao lưu. Hãy bấm \"Sao lưu ngay\" và chép bản sao lưu ra USB hoặc ổ đĩa khác.",
        BackupHealth.Danger => $"Lần sao lưu gần nhất đã {(now.Date - latest!.Value.Date).Days} ngày. Nếu máy hỏng ổ cứng, dữ liệu từ đó đến nay sẽ mất.",
        BackupHealth.Warning => "Đã hơn 2 ngày chưa sao lưu. Nên sao lưu sau mỗi ngày bán hàng.",
        _ => null,
    };

    public static string Schedule(int intervalHours) => intervalHours switch
    {
        <= 0 => "Tắt sao lưu tự động (đổi trong appsettings.json, mục Backup)",
        24 => "Tự động sao lưu mỗi ngày khi mở ứng dụng",
        _ when intervalHours % 24 == 0 => $"Tự động sao lưu mỗi {intervalHours / 24} ngày khi mở ứng dụng",
        _ => $"Tự động sao lưu khi mở ứng dụng nếu đã quá {intervalHours} giờ",
    };

    public static string SchemaName(BackupSchemaCompatibility schema) => schema switch
    {
        BackupSchemaCompatibility.Current => "Cùng phiên bản",
        BackupSchemaCompatibility.Older => "Phiên bản cũ hơn (sẽ được nâng cấp khi phục hồi)",
        BackupSchemaCompatibility.Newer => "Phiên bản mới hơn – không phục hồi được bằng ứng dụng này",
        _ => "Không đọc được",
    };

    /// <summary>Lines of the detail card for a checked backup.</summary>
    public static IReadOnlyList<string> Describe(BackupInspection inspection)
    {
        if (!inspection.IsValid)
        {
            return [$"✗ File không dùng được: {inspection.Problem}"];
        }

        var lines = new List<string> { "✓ File nguyên vẹn", $"Phiên bản dữ liệu: {SchemaName(inspection.Schema)}" };
        if (inspection.Products is { } products)
        {
            lines.Add($"Sản phẩm: {products.ToString("#,0", Vnd.Culture)}");
        }

        if (inspection.Customers is { } customers)
        {
            lines.Add($"Khách hàng: {customers.ToString("#,0", Vnd.Culture)}");
        }

        if (inspection.Sales is { } sales)
        {
            lines.Add($"Hóa đơn: {sales.ToString("#,0", Vnd.Culture)}");
        }

        if (inspection.LastSaleAt is { } last)
        {
            lines.Add($"Hóa đơn cuối: {last.ToLocalTime():dd/MM/yyyy HH:mm}");
        }

        if (inspection.Problem is { Length: > 0 } note)
        {
            lines.Add(note);
        }

        return lines;
    }

    /// <summary>Text of the red confirmation before a restore.</summary>
    public static string RestoreWarning(BackupFile file) =>
        $"Toàn bộ dữ liệu hiện tại (hóa đơn, tồn kho, khách hàng, người dùng…) sẽ được thay bằng bản sao lưu lúc {file.CreatedAt:HH:mm dd/MM/yyyy}.\n\n" +
        "Mọi thay đổi sau thời điểm đó sẽ không còn. Ứng dụng sẽ tự sao lưu dữ liệu hiện tại thành bản \"Trước khi phục hồi\" rồi khởi động lại sau khi phục hồi.";
}
