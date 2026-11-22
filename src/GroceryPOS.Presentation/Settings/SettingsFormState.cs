using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Settings;

/// <summary>
/// Everything the Cấu hình screen shows around the edited fields, computed from the saved and the edited settings:
/// per-field errors, footer counter, loyalty explanation, warnings, the live receipt preview and the status badge.
/// </summary>
public sealed record SettingsFormState(
    IReadOnlyDictionary<string, string> FieldErrors,
    string FooterInfo,
    Tone FooterTone,
    string LoyaltySummary,
    IReadOnlyList<string> Warnings,
    string ReceiptPreview,
    string StatusText,
    Tone StatusTone,
    int ChangeCount,
    bool CanSave,
    bool CanUndo)
{
    public const string Loading = "Đang tải…";
    public const string LoadFailed = "Không tải được";
    public const string HasErrors = "Có lỗi cần sửa";
    public const string Unsaved = "Chưa lưu";
    public const string Saved = "Đã lưu";
    public const string Saving = "Đang lưu…";

    private static readonly StoreSettingsValidator Validator = new();

    public bool IsValid => FieldErrors.Count == 0;

    public bool IsDirty => ChangeCount > 0;

    /// <summary>
    /// The state for <paramref name="edited"/>; <paramref name="saved"/> is null while nothing is loaded (Save and
    /// Undo stay off). <paramref name="baseStore"/> supplies what the screen does not edit (receipt header extras).
    /// </summary>
    public static SettingsFormState Compute(
        StoreSettings? saved,
        StoreSettings edited,
        StoreProfile baseStore,
        DateTimeOffset now,
        bool saving = false,
        bool loadFailed = false)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(baseStore);
        var settings = edited.Normalize();

        var errors = Validator.Validate(settings).ToFieldErrors()
            .ToDictionary(e => e.Key, e => e.Value[0], StringComparer.Ordinal);

        string footerInfo;
        Tone footerTone;
        if (errors.TryGetValue(nameof(StoreSettings.ReceiptFooter), out var footerError))
        {
            footerInfo = footerError;
            footerTone = Tone.Danger;
        }
        else
        {
            footerInfo = $"{StoreSettings.FooterLineCount(settings.ReceiptFooter)}/{StoreSettingsValidator.MaxFooterLines} dòng · " +
                         $"{settings.ReceiptFooter.Length}/{StoreSettingsValidator.MaxFooterLength} ký tự";
            footerTone = Tone.Muted;
        }

        var loyalty = string.Join("\n", SettingsText.LoyaltySummary(settings.VndPerPoint, settings.PointValueVnd));
        var receipt = ReceiptFormatter.ToText(
            ReceiptFormatter.Format(SampleReceipt(settings, baseStore, now), ReceiptFormatter.ColumnsFor(settings.ReceiptPaperWidthMm)));

        var changes = saved is null ? 0 : StoreSettingsCodec.Diff(saved, settings).Count;
        var valid = errors.Count == 0;
        var (status, tone) = (saved, loadFailed, saving, valid, changes) switch
        {
            (null, true, _, _, _) => (LoadFailed, Tone.Danger),
            (null, _, _, _, _) => (Loading, Tone.Muted),
            (_, _, true, _, _) => (Saving, Tone.Info),
            (_, _, _, false, _) => (HasErrors, Tone.Danger),
            (_, _, _, _, > 0) => (Unsaved, Tone.Warning),
            _ => (Saved, Tone.Success),
        };

        return new SettingsFormState(
            errors,
            footerInfo,
            footerTone,
            loyalty,
            SettingsText.Warnings(settings),
            receipt,
            status,
            tone,
            changes,
            CanSave: saved is not null && !saving && valid && changes > 0,
            CanUndo: saved is not null && !saving && changes > 0);
    }

    /// <summary>The sample receipt for the preview and its print preview; a blank store name shows a placeholder.</summary>
    public static ReceiptData SampleReceipt(StoreSettings settings, StoreProfile baseStore, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(baseStore);
        var named = settings with { StoreName = string.IsNullOrWhiteSpace(settings.StoreName) ? "(Tên cửa hàng)" : settings.StoreName };
        return SettingsText.SampleReceipt(named.ApplyTo(baseStore), new LoyaltyPolicy(settings.VndPerPoint, settings.PointValueVnd), now);
    }
}
