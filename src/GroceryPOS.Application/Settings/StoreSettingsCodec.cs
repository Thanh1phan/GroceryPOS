using System.Globalization;
using GroceryPOS.Application.Formatting;

namespace GroceryPOS.Application.Settings;

/// <summary>
/// Maps <see cref="StoreSettings"/> to the key/value rows stored in the <c>AppSettings</c> table and back.
/// Values are invariant-culture text ("10000", "true"); a missing or unreadable row falls back to the default
/// (appsettings.json), so a damaged value never stops the counter from selling.
/// </summary>
public static class StoreSettingsCodec
{
    public const string StoreSection = "Cửa hàng";
    public const string ReceiptSection = "Hóa đơn";
    public const string LoyaltySection = "Tích điểm";
    public const string SalesSection = "Bán hàng";

    public static class Keys
    {
        public const string StoreName = "store.name";
        public const string Address = "store.address";
        public const string Phone = "store.phone";
        public const string TaxCode = "store.tax_code";
        public const string ReceiptFooter = "receipt.footer";
        public const string ReceiptPaperWidth = "receipt.paper_width_mm";
        public const string ReceiptPreview = "receipt.preview_after_checkout";
        public const string VndPerPoint = "loyalty.vnd_per_point";
        public const string PointValue = "loyalty.point_value_vnd";
        public const string ReturnWindowDays = "sales.return_window_days";
        public const string AllowNegativeStock = "sales.allow_negative_stock";
        public const string RequireOpenShift = "sales.require_open_shift";
        public const string ShiftVarianceThreshold = "shift.variance_note_threshold";
    }

    private sealed record Field(
        string Key,
        string Section,
        string Label,
        Func<StoreSettings, string> Write,
        Func<StoreSettings, string, StoreSettings?> Read,
        Func<StoreSettings, string> Display);

    private static readonly Field[] Fields =
    [
        Text(Keys.StoreName, StoreSection, "Tên cửa hàng", s => s.StoreName, (s, v) => string.IsNullOrWhiteSpace(v) ? null : s with { StoreName = v }),
        Text(Keys.Address, StoreSection, "Địa chỉ", s => s.Address, (s, v) => s with { Address = Optional(v) }),
        Text(Keys.Phone, StoreSection, "Điện thoại", s => s.Phone, (s, v) => s with { Phone = Optional(v) }),
        Text(Keys.TaxCode, StoreSection, "Mã số thuế", s => s.TaxCode, (s, v) => s with { TaxCode = Optional(v) }),
        new(Keys.ReceiptFooter, ReceiptSection, "Lời cảm ơn cuối hóa đơn",
            s => s.ReceiptFooter,
            (s, v) => s with { ReceiptFooter = v },
            s => s.ReceiptFooter.Length == 0 ? "(trống)" : $"\"{s.ReceiptFooter.Replace("\n", " / ")}\""),
        Number(Keys.ReceiptPaperWidth, ReceiptSection, "Khổ giấy", s => s.ReceiptPaperWidthMm,
            (s, v) => v is 58 or 80 ? s with { ReceiptPaperWidthMm = (int)v } : null, v => $"{v:0}mm"),
        Flag(Keys.ReceiptPreview, ReceiptSection, "Xem trước hóa đơn sau thanh toán", s => s.PreviewReceiptAfterCheckout,
            (s, v) => s with { PreviewReceiptAfterCheckout = v }),
        Number(Keys.VndPerPoint, LoyaltySection, "Số tiền cho 1 điểm", s => s.VndPerPoint,
            (s, v) => v >= 0 ? s with { VndPerPoint = v } : null, v => v == 0 ? "tắt tích điểm" : Vnd.Format(v)),
        Number(Keys.PointValue, LoyaltySection, "Giá trị 1 điểm khi đổi", s => s.PointValueVnd,
            (s, v) => v >= 0 ? s with { PointValueVnd = v } : null, v => v == 0 ? "không cho đổi điểm" : Vnd.Format(v)),
        Number(Keys.ReturnWindowDays, SalesSection, "Hạn đổi trả", s => s.ReturnWindowDays,
            (s, v) => v >= 0 && v <= 365 ? s with { ReturnWindowDays = (int)v } : null, v => v == 0 ? "chỉ trong ngày" : $"{v:0} ngày"),
        Flag(Keys.AllowNegativeStock, SalesSection, "Cho bán khi hết tồn kho", s => s.AllowNegativeStock,
            (s, v) => s with { AllowNegativeStock = v }),
        Flag(Keys.RequireOpenShift, SalesSection, "Bắt buộc mở ca khi bán", s => s.RequireOpenShift,
            (s, v) => s with { RequireOpenShift = v }),
        Number(Keys.ShiftVarianceThreshold, SalesSection, "Lệch két cần ghi chú từ", s => s.ShiftVarianceNoteThreshold,
            (s, v) => v >= 0 ? s with { ShiftVarianceNoteThreshold = v } : null, Vnd.Format),
    ];

    /// <summary>Every stored key, in screen order.</summary>
    public static IReadOnlyList<string> AllKeys { get; } = Fields.Select(f => f.Key).ToList();

    /// <summary>Settings as storable text values, one per key.</summary>
    public static IReadOnlyDictionary<string, string> ToValues(StoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Fields.ToDictionary(f => f.Key, f => f.Write(settings), StringComparer.Ordinal);
    }

    /// <summary>
    /// Applies the stored <paramref name="values"/> on top of <paramref name="defaults"/>. Unknown keys are ignored,
    /// unreadable values keep the default and are reported in <paramref name="rejected"/>.
    /// </summary>
    public static StoreSettings Read(IReadOnlyDictionary<string, string> values, StoreSettings defaults, ICollection<string>? rejected = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(defaults);
        var settings = defaults;
        foreach (var field in Fields)
        {
            if (!values.TryGetValue(field.Key, out var raw))
            {
                continue;
            }

            if (field.Read(settings, raw ?? string.Empty) is { } next)
            {
                settings = next;
            }
            else
            {
                rejected?.Add(field.Key);
            }
        }

        return settings;
    }

    /// <summary>What differs between two settings snapshots, with Vietnamese labels and display values.</summary>
    public static IReadOnlyList<SettingChange> Diff(StoreSettings before, StoreSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return Fields
            .Where(f => !string.Equals(f.Write(before), f.Write(after), StringComparison.Ordinal))
            .Select(f => new SettingChange(f.Key, f.Section, f.Label, f.Display(before), f.Display(after)))
            .ToList();
    }

    /// <summary>Audit text: "Cửa hàng – Tên cửa hàng: "A" → "B"; Tích điểm – …", capped to fit the audit column.</summary>
    public static string DescribeForAudit(IReadOnlyList<SettingChange> changes, int maxLength = 1000)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var text = string.Join("; ", changes.Select(c => $"{c.Section} – {c}"));
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Field Text(string key, string section, string label, Func<StoreSettings, string?> get, Func<StoreSettings, string, StoreSettings?> set) =>
        new(key, section, label, s => get(s) ?? string.Empty, set, s => get(s) is { Length: > 0 } v ? $"\"{v}\"" : "(trống)");

    private static Field Flag(string key, string section, string label, Func<StoreSettings, bool> get, Func<StoreSettings, bool, StoreSettings> set) =>
        new(key, section, label,
            s => get(s) ? "true" : "false",
            (s, v) => bool.TryParse(v.Trim(), out var b) ? set(s, b) : null,
            s => get(s) ? "bật" : "tắt");

    private static Field Number(string key, string section, string label, Func<StoreSettings, decimal> get,
        Func<StoreSettings, decimal, StoreSettings?> set, Func<decimal, string> display) =>
        new(key, section, label,
            s => get(s).ToString("0.###", CultureInfo.InvariantCulture),
            (s, v) => decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? set(s, d) : null,
            s => display(get(s)));
}
