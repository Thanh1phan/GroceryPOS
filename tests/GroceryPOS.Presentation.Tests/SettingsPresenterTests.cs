using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Settings;

namespace GroceryPOS.Presentation.Tests;

public sealed class SettingsPresenterTests
{
    private sealed class FakeSettingsView : RecordingView, ISettingsView
    {
        /// <summary>What the fields hold; edits in tests change this, then call <see cref="SettingsPresenter.Edited"/>.</summary>
        public StoreSettings Fields { get; set; } = new() { StoreName = string.Empty };
        public List<StoreSettings> Shown { get; } = new();
        public List<SettingsFormState> States { get; } = new();
        public List<bool> Editable { get; } = new();
        public List<string> Focused { get; } = new();
        public List<string> InfoToasts { get; } = new();
        public List<(ReceiptData Receipt, int Paper)> Previews { get; } = new();

        /// <summary>Answers for successive Confirm calls; falls back to <see cref="RecordingView.ConfirmAnswer"/>.</summary>
        public Queue<bool> Answers { get; } = new();

        public SettingsFormState State => States[^1];

        public void ShowSettings(StoreSettings settings)
        {
            Shown.Add(settings);
            Fields = settings;
        }

        public StoreSettings ReadSettings() => Fields;

        public void ShowState(SettingsFormState state) => States.Add(state);

        public void SetEditable(bool editable) => Editable.Add(editable);

        public void FocusField(string property) => Focused.Add(property);

        public void ToastInfo(string message, string? title = null) => InfoToasts.Add(message);

        public void ShowReceiptPrintPreview(ReceiptData receipt, int paperWidthMm) => Previews.Add((receipt, paperWidthMm));

        public new bool Confirm(string message, string title, string confirmText, bool danger = false)
        {
            var answer = base.Confirm(message, title, confirmText, danger);
            return Answers.Count > 0 ? Answers.Dequeue() : answer;
        }

        bool IView.Confirm(string message, string title, string confirmText, bool danger) => Confirm(message, title, confirmText, danger);
    }

    private static (TestHost Host, SettingsPresenter Presenter, FakeSettingsView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.SettingsManage) : new TestHost();
        var presenter = host.Get<SettingsPresenter>();
        var view = new FakeSettingsView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    private static async Task<(TestHost Host, SettingsPresenter Presenter, FakeSettingsView View)> Loaded()
    {
        var created = Create();
        Assert.True(await created.Presenter.LoadAsync());
        return created;
    }

    [Fact]
    public void Attaching_disables_the_fields_and_shows_loading()
    {
        var (host, _, view) = Create();
        using var _h = host;

        Assert.Equal(new[] { false }, view.Editable);
        Assert.Equal(SettingsFormState.Loading, view.State.StatusText);
        Assert.False(view.State.CanSave);
        Assert.False(view.State.CanUndo);
    }

    [Fact]
    public async Task Load_fills_the_fields_enables_them_and_focuses_the_store_name()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        Assert.Equal(host.Settings.Stored, presenter.Saved);
        Assert.Equal(host.Settings.Stored, view.Shown.Single());
        Assert.Equal(new[] { false, true }, view.Editable);
        Assert.Equal(SettingsFormState.Saved, view.State.StatusText);
        Assert.Equal(Tone.Success, view.State.StatusTone);
        Assert.Contains("TẠP HÓA CÔ BA", view.State.ReceiptPreview);
        Assert.Equal(nameof(StoreSettings.StoreName), view.Focused.Single());
    }

    [Fact]
    public async Task Without_permission_nothing_is_loaded()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _h = host;

        Assert.False(presenter.CanManage);
        Assert.False(await presenter.LoadAsync());
        Assert.Equal(SettingsPresenter.NoPermission, view.Warnings.Single());
        Assert.Equal(0, host.Settings.Gets);
    }

    [Fact]
    public async Task A_failed_first_load_marks_the_badge_and_a_failed_reload_keeps_the_data()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Settings.FailGetWith = Error.Validation("Mất kết nối CSDL");

        Assert.False(await presenter.LoadAsync());
        Assert.Equal("Mất kết nối CSDL", view.Warnings.Single());
        Assert.Equal(SettingsFormState.LoadFailed, view.State.StatusText);
        Assert.Equal(Tone.Danger, view.State.StatusTone);

        host.Settings.FailGetWith = null;
        Assert.True(await presenter.LoadAsync());
        host.Settings.FailGetWith = Error.Validation("Mất kết nối CSDL");
        Assert.False(await presenter.LoadAsync());
        Assert.Equal("Mất kết nối CSDL", view.Errors.Single());
        Assert.Single(view.Warnings);
        Assert.NotNull(presenter.Saved);
    }

    [Fact]
    public async Task Editing_shows_unsaved_errors_warnings_and_the_live_receipt()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        view.Fields = view.Fields with { StoreName = "Tạp hóa Út Hai", ReceiptPaperWidthMm = 58, RequireOpenShift = false };
        presenter.Edited();
        Assert.Equal(SettingsFormState.Unsaved, view.State.StatusText);
        Assert.Equal(3, view.State.ChangeCount);
        Assert.True(view.State.CanSave);
        Assert.True(view.State.CanUndo);
        Assert.Contains("TẠP HÓA ÚT HAI", view.State.ReceiptPreview);
        Assert.True(view.State.ReceiptPreview.Split('\n').Max(l => l.Length) <= ReceiptFormatter.ColumnsFor(58));
        Assert.Contains(view.State.Warnings, w => w.StartsWith("Không bắt buộc mở ca", StringComparison.Ordinal));

        view.Fields = view.Fields with { StoreName = "  " };
        presenter.Edited();
        Assert.Equal(SettingsFormState.HasErrors, view.State.StatusText);
        Assert.True(view.State.FieldErrors.ContainsKey(nameof(StoreSettings.StoreName)));
        Assert.False(view.State.CanSave);
        Assert.True(view.State.CanUndo);
        Assert.Contains("(TÊN CỬA HÀNG)", view.State.ReceiptPreview);
    }

    [Fact]
    public async Task Undo_puts_the_saved_values_back()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { Phone = "0911222333" };
        presenter.Edited();

        presenter.Undo();

        Assert.Equal(host.Settings.Stored, view.Fields);
        Assert.Equal(SettingsFormState.Saved, view.State.StatusText);
    }

    [Fact]
    public async Task Restore_defaults_asks_then_fills_without_saving()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        host.Settings.Defaults = new StoreSettings { StoreName = "Mặc định" };

        view.ConfirmAnswer = false;
        await presenter.RestoreDefaultsAsync();
        Assert.Equal("Tạp hóa Cô Ba", view.Fields.StoreName);

        view.ConfirmAnswer = true;
        await presenter.RestoreDefaultsAsync();
        Assert.Equal("Mặc định", view.Fields.StoreName);
        Assert.Equal(SettingsFormState.Unsaved, view.State.StatusText);
        Assert.Empty(host.Settings.SavedRequests);
        Assert.Equal(SettingsPresenter.DefaultsQuestion, view.Questions[^1]);
    }

    [Fact]
    public async Task Save_lists_the_changes_saves_and_reports()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { VndPerPoint = 20_000, ReturnWindowDays = 3 };

        Assert.True(await presenter.SaveAsync());

        var question = view.Questions.Single();
        Assert.StartsWith("Lưu 2 thay đổi cấu hình?", question);
        Assert.Contains("• Số tiền cho 1 điểm: 10.000 ₫ → 20.000 ₫", question);
        Assert.Contains("• Hạn đổi trả: 7 ngày → 3 ngày", question);
        Assert.Equal(20_000, host.Settings.Stored.VndPerPoint);
        Assert.Equal(host.Settings.Stored, presenter.Saved);
        Assert.Equal(SettingsPresenter.SavedMessage(2, []), view.Successes.Single());
        Assert.Equal(SettingsFormState.Saved, view.State.StatusText);
        Assert.False(view.State.CanSave);
    }

    [Fact]
    public async Task Save_without_changes_or_when_declined_writes_nothing()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;

        Assert.False(await presenter.SaveAsync());
        Assert.Equal(SettingsPresenter.NothingToSave, view.InfoToasts.Single());

        view.Fields = view.Fields with { Phone = "0911222333" };
        presenter.Edited();
        view.ConfirmAnswer = false;
        Assert.False(await presenter.SaveAsync());
        Assert.Empty(host.Settings.SavedRequests);
        Assert.Equal(SettingsFormState.Unsaved, view.State.StatusText);
    }

    [Fact]
    public async Task Save_with_an_invalid_field_focuses_it_instead()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Focused.Clear();
        view.Fields = view.Fields with { Phone = "abc", StoreName = "" };

        Assert.False(await presenter.SaveAsync());

        Assert.Equal(nameof(StoreSettings.StoreName), view.Focused.Single());
        Assert.Single(view.Warnings);
        Assert.Empty(view.Questions);
        Assert.Empty(host.Settings.SavedRequests);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_edits()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { Phone = "0911222333" };
        host.Settings.FailSaveWith = Error.Forbidden("Bạn không có quyền thay đổi cấu hình hệ thống.");

        Assert.False(await presenter.SaveAsync());

        Assert.Equal("Bạn không có quyền thay đổi cấu hình hệ thống.", view.Warnings.Single());
        Assert.Equal("0911222333", view.Fields.Phone);
        Assert.Equal(SettingsFormState.Unsaved, view.State.StatusText);
        Assert.True(view.State.CanSave);
    }

    [Fact]
    public async Task Save_keeps_changes_another_counter_saved_meanwhile()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { Phone = "0911222333" };
        host.Settings.Stored = host.Settings.Stored with { PointValueVnd = 200 }; // saved on another counter

        Assert.True(await presenter.SaveAsync());

        Assert.Equal("0911222333", host.Settings.Stored.Phone);
        Assert.Equal(200, host.Settings.Stored.PointValueVnd); // not reverted to the stale 100
        Assert.Contains("Đã giữ 1 thay đổi do máy khác lưu trước đó (Giá trị 1 điểm khi đổi)", view.Successes.Single());
        Assert.Equal(200, view.Fields.PointValueVnd);
        Assert.Single(view.Questions);
    }

    [Fact]
    public async Task A_conflicting_field_asks_before_overwriting()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { Phone = "0911222333", ReturnWindowDays = 3 };
        host.Settings.Stored = host.Settings.Stored with { Phone = "0988777666" };

        view.Answers.Enqueue(true);  // save 2 changes
        view.Answers.Enqueue(true);  // overwrite
        Assert.True(await presenter.SaveAsync());

        Assert.Equal(2, view.Questions.Count);
        Assert.Contains("• Điện thoại: \"0988777666\" → \"0911222333\"", view.DangerQuestions.Single());
        Assert.Equal("0911222333", host.Settings.Stored.Phone);
        Assert.Equal(3, host.Settings.Stored.ReturnWindowDays);
    }

    [Fact]
    public async Task Declining_the_overwrite_takes_their_value_and_keeps_my_other_edits_unsaved()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { Phone = "0911222333", ReturnWindowDays = 3 };
        host.Settings.Stored = host.Settings.Stored with { Phone = "0988777666" };
        var theirs = host.Settings.Stored;

        view.Answers.Enqueue(true);
        view.Answers.Enqueue(false);
        Assert.False(await presenter.SaveAsync());

        Assert.Empty(host.Settings.SavedRequests);
        Assert.Equal(theirs, presenter.Saved);
        Assert.Equal("0988777666", view.Fields.Phone);
        Assert.Equal(3, view.Fields.ReturnWindowDays);
        Assert.Equal(SettingsPresenter.ConflictDeclined, view.Infos.Single());
        Assert.Equal(1, view.State.ChangeCount);
        Assert.Equal(SettingsFormState.Unsaved, view.State.StatusText);
    }

    [Fact]
    public async Task Leaving_with_unsaved_changes_asks_and_F5_reloads_only_when_agreed()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        Assert.True(presenter.ConfirmLeave());
        Assert.Empty(view.Questions);

        view.Fields = view.Fields with { Phone = "0911222333", TaxCode = "0312345678" };
        view.ConfirmAnswer = false;
        Assert.False(presenter.ConfirmLeave());
        Assert.Equal(SettingsPresenter.LeaveQuestion(2), view.DangerQuestions.Single());

        var gets = host.Settings.Gets;
        await presenter.ReloadAsync();
        Assert.Equal(gets, host.Settings.Gets);

        view.ConfirmAnswer = true;
        await presenter.ReloadAsync();
        Assert.Equal(gets + 1, host.Settings.Gets);
        Assert.Equal(host.Settings.Stored, view.Fields);
    }

    [Fact]
    public async Task Print_preview_uses_the_edited_values_and_paper()
    {
        var (host, presenter, view) = await Loaded();
        using var _h = host;
        view.Fields = view.Fields with { StoreName = "Tạp hóa Út Hai", ReceiptPaperWidthMm = 58 };

        presenter.PrintPreview();

        var (receipt, paper) = view.Previews.Single();
        Assert.Equal(58, paper);
        Assert.Equal("Tạp hóa Út Hai", receipt.Store.Name);
        Assert.Equal(host.Clock.Now, receipt.IssuedAt);
    }

    [Fact]
    public async Task A_closed_view_is_not_updated_after_loading()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.IsAlive = false;

        Assert.False(await presenter.LoadAsync());

        Assert.Empty(view.Shown);
        Assert.Null(presenter.Saved);
    }

    [Fact]
    public void Change_list_is_capped_at_ten_lines()
    {
        var changes = Enumerable.Range(1, 12).Select(i => new SettingChange($"k{i}", "S", $"Mục {i}", "a", "b")).ToList();

        var text = SettingsPresenter.ChangeList(changes);

        Assert.Equal(11, text.Split('\n').Length);
        Assert.EndsWith("• … và 2 thay đổi khác", text);
    }
}
