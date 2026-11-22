using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Settings;

/// <summary>The Cấu hình screen as seen by its presenter.</summary>
public interface ISettingsView : IView
{
    /// <summary>Fills every field with <paramref name="settings"/> (without reporting the changes back as edits).</summary>
    void ShowSettings(StoreSettings settings);

    /// <summary>The values currently in the fields.</summary>
    StoreSettings ReadSettings();

    /// <summary>Validation marks, footer counter, loyalty summary, warnings, receipt preview, status badge and buttons.</summary>
    void ShowState(SettingsFormState state);

    /// <summary>Enables or disables the fields (off while nothing is loaded).</summary>
    void SetEditable(bool editable);

    /// <summary>Puts the cursor in the field of <paramref name="property"/> (a <see cref="StoreSettings"/> property name), switching tabs.</summary>
    void FocusField(string property);

    void ToastInfo(string message, string? title = null);

    /// <summary>Opens the print preview of the sample receipt.</summary>
    void ShowReceiptPrintPreview(ReceiptData receipt, int paperWidthMm);
}

/// <summary>
/// Cấu hình logic (<c>settings.manage</c>): load, live state while editing, undo, restore the appsettings.json defaults,
/// and save after listing what changes. Saving re-reads the stored settings first: changes saved meanwhile on another
/// counter to fields this user did not touch are kept (instead of being silently reverted by this screen's stale
/// copy), and a field both changed asks before overwriting. Leaving the screen with unsaved changes asks first.
/// </summary>
public sealed class SettingsPresenter : Presenter<ISettingsView>
{
    public const string Title = "Cấu hình";
    public const string NoPermission = "Bạn không có quyền thay đổi cấu hình hệ thống.";
    public const string NothingToSave = "Không có thay đổi nào để lưu.";
    public const string DefaultsQuestion =
        "Đưa mọi thiết lập về giá trị trong file appsettings.json?\nThay đổi chỉ được ghi khi bạn bấm \"Lưu thay đổi\".";

    private const int MaxListedChanges = 10;

    /// <summary>Fields in screen order (tab by tab), so a failed save focuses the first one the user would see.</summary>
    private static readonly string[] FieldOrder =
    [
        nameof(StoreSettings.StoreName), nameof(StoreSettings.Address), nameof(StoreSettings.Phone), nameof(StoreSettings.TaxCode),
        nameof(StoreSettings.ReceiptFooter), nameof(StoreSettings.ReceiptPaperWidthMm),
        nameof(StoreSettings.VndPerPoint), nameof(StoreSettings.PointValueVnd),
        nameof(StoreSettings.ReturnWindowDays), nameof(StoreSettings.ShiftVarianceNoteThreshold),
    ];

    private readonly TimeProvider _clock;
    private StoreProfile _baseStore = StoreProfile.Default;
    private bool _saving;
    private bool _loadFailed;

    public SettingsPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, TimeProvider clock)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        CanManage = currentUser.HasPermission(Permissions.SettingsManage);
    }

    public bool CanManage { get; }

    /// <summary>The settings as stored when last loaded or saved; null until loaded.</summary>
    public StoreSettings? Saved { get; private set; }

    /// <summary>"• Label: old → new" lines, at most ten plus a "… và N thay đổi khác" line.</summary>
    public static string ChangeList(IReadOnlyList<SettingChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var list = string.Join("\n", changes.Take(MaxListedChanges).Select(c => $"• {c}"));
        return changes.Count > MaxListedChanges ? $"{list}\n• … và {changes.Count - MaxListedChanges} thay đổi khác" : list;
    }

    public static string SaveQuestion(IReadOnlyList<SettingChange> changes) =>
        $"Lưu {changes.Count} thay đổi cấu hình?\n\n{ChangeList(changes)}";

    public static string LeaveQuestion(int changes) => $"Có {changes} thay đổi cấu hình chưa lưu. Bỏ các thay đổi này?";

    public static string ConflictQuestion(IReadOnlyList<SettingChange> overridden) =>
        "Trong lúc bạn sửa, một máy khác đã lưu giá trị khác cho cùng mục:\n\n" +
        ChangeList(overridden) +
        "\n\n(giá trị máy khác → giá trị của bạn). Ghi đè bằng giá trị của bạn?";

    public const string ConflictDeclined =
        "Đã nạp các giá trị mới từ máy khác. Những thay đổi khác của bạn vẫn còn trên màn hình, chưa được lưu.";

    public static string SavedMessage(int changes, IReadOnlyList<SettingChange> kept)
    {
        var text = $"Đã lưu {changes} thay đổi. Các màn hình mở sau sẽ dùng cấu hình mới.";
        return kept.Count == 0
            ? text
            : $"{text} Đã giữ {kept.Count} thay đổi do máy khác lưu trước đó ({string.Join(", ", kept.Select(k => k.Label))}).";
    }

    protected override void OnAttached()
    {
        View.SetEditable(false);
        View.ShowState(State(new StoreSettings()));
    }

    /// <summary>Loads the stored settings into the fields. False after showing the error.</summary>
    public async Task<bool> LoadAsync()
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return false;
        }

        var result = await UseScopeAsync(sp =>
        {
            _baseStore = sp.GetService<StoreProfile>() ?? StoreProfile.Default;
            return sp.GetRequiredService<ISettingsService>().GetAsync();
        });
        if (!ViewAlive)
        {
            return false;
        }

        if (result.IsFailure)
        {
            if (Saved is null)
            {
                _loadFailed = true;
                View.ShowState(State(View.ReadSettings()));
                View.ShowWarning(result.Error.Message, Title);
            }
            else
            {
                View.ToastError(result.Error.Message, "Không tải lại được cấu hình");
            }

            return false;
        }

        _loadFailed = false;
        Saved = result.Value.Normalize();
        View.ShowSettings(Saved);
        View.SetEditable(true);
        Refresh();
        View.FocusField(nameof(StoreSettings.StoreName));
        return true;
    }

    /// <summary>F5: asks first when there are unsaved changes, then reloads.</summary>
    public async Task ReloadAsync()
    {
        if (ConfirmLeave())
        {
            await LoadAsync();
        }
    }

    /// <summary>A field changed: recompute the state.</summary>
    public void Edited()
    {
        if (Saved is not null)
        {
            Refresh();
        }
    }

    /// <summary>Puts the saved values back.</summary>
    public void Undo()
    {
        if (Saved is null || _saving)
        {
            return;
        }

        View.ShowSettings(Saved);
        Refresh();
    }

    /// <summary>Fills the appsettings.json defaults after confirming; nothing is written until saved.</summary>
    public async Task RestoreDefaultsAsync()
    {
        if (Saved is null || _saving)
        {
            return;
        }

        var defaults = await UseAsync<ISettingsService, Result<StoreSettings>>(s => Task.FromResult(s.GetDefaults()));
        if (!ViewAlive)
        {
            return;
        }

        if (defaults.IsFailure)
        {
            View.ShowWarning(defaults.Error.Message, Title);
            return;
        }

        if (View.Confirm(DefaultsQuestion, "Khôi phục mặc định", "Khôi phục"))
        {
            View.ShowSettings(defaults.Value);
            Refresh();
        }
    }

    /// <summary>True when the screen may be left: nothing unsaved, or the user agreed to drop the changes.</summary>
    public bool ConfirmLeave()
    {
        if (Saved is null)
        {
            return true;
        }

        var changes = StoreSettingsCodec.Diff(Saved, View.ReadSettings().Normalize()).Count;
        return changes == 0 || View.Confirm(LeaveQuestion(changes), Title, "Bỏ thay đổi", danger: true);
    }

    /// <summary>Validates, lists the changes, merges with what was saved meanwhile elsewhere, and saves.</summary>
    public async Task<bool> SaveAsync()
    {
        if (Saved is not { } original || _saving)
        {
            return false;
        }

        var edited = View.ReadSettings().Normalize();
        var state = State(edited);
        if (!state.IsValid)
        {
            var first = FieldOrder.FirstOrDefault(state.FieldErrors.ContainsKey) ?? state.FieldErrors.Keys.First();
            View.ShowWarning(state.FieldErrors[first], "Không lưu được cấu hình");
            View.FocusField(first);
            return false;
        }

        var changes = StoreSettingsCodec.Diff(original, edited);
        if (changes.Count == 0)
        {
            View.ToastInfo(NothingToSave);
            return false;
        }

        if (!View.Confirm(SaveQuestion(changes), "Lưu cấu hình", "Lưu"))
        {
            return false;
        }

        _saving = true;
        View.ShowState(State(edited));
        try
        {
            var stored = await UseAsync<ISettingsService, Result<StoreSettings>>(s => s.GetAsync());
            if (!ViewAlive)
            {
                return false;
            }

            if (stored.IsFailure)
            {
                View.ShowWarning(stored.Error.Message, "Không lưu được cấu hình");
                return false;
            }

            var current = stored.Value.Normalize();
            var merge = StoreSettingsCodec.Merge(original, edited, current);
            if (merge.Overridden.Count > 0 && !View.Confirm(ConflictQuestion(merge.Overridden), "Cấu hình vừa được thay đổi", "Ghi đè", danger: true))
            {
                // Take the other counter's values (theirs win) and keep this user's other edits on screen, unsaved.
                Saved = current;
                View.ShowSettings(StoreSettingsCodec.Merge(original, current, edited).Settings);
                View.ShowInfo(ConflictDeclined, Title);
                return false;
            }

            var result = await UseAsync<ISettingsService, Result<SettingsSaveResult>>(s => s.SaveAsync(merge.Settings));
            if (!ViewAlive)
            {
                return false;
            }

            if (result.IsFailure)
            {
                View.ShowWarning(result.Error.Message, "Không lưu được cấu hình");
                return false;
            }

            Saved = result.Value.Settings.Normalize();
            View.ShowSettings(Saved);
            View.ToastSuccess(SavedMessage(result.Value.Changes.Count, merge.Kept), "Đã lưu cấu hình");
            return true;
        }
        finally
        {
            _saving = false;
            if (ViewAlive)
            {
                Refresh();
            }
        }
    }

    /// <summary>Opens the print preview of the sample receipt with the edited values.</summary>
    public void PrintPreview()
    {
        var settings = View.ReadSettings().Normalize();
        View.ShowReceiptPrintPreview(SettingsFormState.SampleReceipt(settings, _baseStore, _clock.GetLocalNow()), settings.ReceiptPaperWidthMm);
    }

    private void Refresh() => View.ShowState(State(View.ReadSettings()));

    private SettingsFormState State(StoreSettings edited) =>
        SettingsFormState.Compute(Saved, edited, _baseStore, _clock.GetLocalNow(), _saving, _loadFailed);
}
