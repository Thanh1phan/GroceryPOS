using System.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Tests;

public class ToastQueueTests
{
    private static ToastRequest Req(string message, FeedbackKind kind = FeedbackKind.Info, string? title = null, double? seconds = null) =>
        new(kind, message, title, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value));

    [Fact]
    public void Shows_up_to_the_limit_and_queues_the_rest_in_order()
    {
        var queue = new ToastQueue(maxVisible: 2);
        queue.Enqueue(Req("a"));
        queue.Enqueue(Req("b"));
        queue.Enqueue(Req("c"));
        queue.Enqueue(Req("d"));

        Assert.Equal(new[] { "a", "b" }, queue.Visible.Select(t => t.Message));
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public void Expired_toasts_leave_and_waiting_ones_take_their_place_with_a_fresh_clock()
    {
        var queue = new ToastQueue(maxVisible: 1);
        queue.Enqueue(Req("a", seconds: 2));
        queue.Enqueue(Req("b", seconds: 3));

        Assert.Empty(queue.Advance(TimeSpan.FromSeconds(1)));
        var expired = queue.Advance(TimeSpan.FromSeconds(1.5));

        Assert.Equal("a", Assert.Single(expired).Message);
        var b = Assert.Single(queue.Visible);
        Assert.Equal("b", b.Message);
        Assert.Equal(TimeSpan.FromSeconds(3), b.Remaining);
        Assert.Equal(1d, b.Progress);
    }

    [Fact]
    public void Identical_toast_is_merged_restarted_and_counted()
    {
        var queue = new ToastQueue();
        var first = queue.Enqueue(Req("Không tìm thấy mã 893", FeedbackKind.Warning, seconds: 4));
        queue.Advance(TimeSpan.FromSeconds(3));
        var second = queue.Enqueue(Req("  Không tìm thấy mã 893 ", FeedbackKind.Warning, seconds: 4));

        Assert.Same(first, second);
        Assert.Single(queue.Visible);
        Assert.Equal(2, first.Repeat);
        Assert.Equal(TimeSpan.FromSeconds(4), first.Remaining);
    }

    [Fact]
    public void Same_text_with_another_kind_or_title_is_a_different_toast()
    {
        var queue = new ToastQueue();
        queue.Enqueue(Req("Đã lưu", FeedbackKind.Success));
        queue.Enqueue(Req("Đã lưu", FeedbackKind.Info));
        queue.Enqueue(Req("Đã lưu", FeedbackKind.Success, "Sản phẩm"));

        Assert.Equal(3, queue.Visible.Count);
    }

    [Fact]
    public void Paused_queue_does_not_count_down()
    {
        var queue = new ToastQueue();
        var toast = queue.Enqueue(Req("a", seconds: 2));
        queue.IsPaused = true;

        Assert.Empty(queue.Advance(TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(2), toast.Remaining);

        queue.IsPaused = false;
        queue.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(0.75, toast.Progress, 3);
    }

    [Fact]
    public void Sticky_toast_stays_until_dismissed()
    {
        var queue = new ToastQueue();
        var toast = queue.Enqueue(new ToastRequest(FeedbackKind.Error, "Mất kết nối máy in", Duration: TimeSpan.Zero));

        queue.Advance(TimeSpan.FromHours(1));

        Assert.True(toast.IsSticky);
        Assert.Single(queue.Visible);
        Assert.True(queue.Dismiss(toast.Id));
        Assert.Empty(queue.Visible);
        Assert.False(queue.Dismiss(toast.Id));
    }

    [Fact]
    public void Dismissing_a_visible_toast_promotes_the_next_one()
    {
        var queue = new ToastQueue(maxVisible: 1);
        var a = queue.Enqueue(Req("a"));
        queue.Enqueue(Req("b"));

        queue.Dismiss(a.Id);

        Assert.Equal("b", Assert.Single(queue.Visible).Message);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Overflowing_waiting_list_drops_the_oldest_non_error_first()
    {
        var queue = new ToastQueue(maxVisible: 1, maxPending: 2);
        queue.Enqueue(Req("visible"));
        var error = queue.Enqueue(Req("lỗi", FeedbackKind.Error));
        queue.Enqueue(Req("info 1"));
        queue.Enqueue(Req("info 2"));

        Assert.Equal(2, queue.PendingCount);
        queue.Dismiss(queue.Visible[0].Id);
        Assert.Same(error, queue.Visible[0]);
        queue.Dismiss(error.Id);
        Assert.Equal("info 2", queue.Visible[0].Message);
    }

    [Fact]
    public void Version_changes_on_every_structural_change()
    {
        var queue = new ToastQueue();
        var v0 = queue.Version;
        var toast = queue.Enqueue(Req("a", seconds: 1));
        var v1 = queue.Version;
        queue.Advance(TimeSpan.FromMilliseconds(100));
        var v2 = queue.Version;
        queue.Advance(TimeSpan.FromSeconds(1));
        var v3 = queue.Version;

        Assert.NotEqual(v0, v1);
        Assert.Equal(v1, v2); // counting down alone does not need a re-layout
        Assert.NotEqual(v2, v3);
        Assert.False(queue.Dismiss(toast.Id));
    }

    [Fact]
    public void Rejects_empty_message_and_bad_limits()
    {
        var queue = new ToastQueue();
        Assert.Throws<ArgumentException>(() => queue.Enqueue(Req("   ")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToastQueue(maxVisible: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToastQueue(maxPending: -1));
    }

    [Fact]
    public void Blank_title_and_action_are_treated_as_missing()
    {
        var toast = new ToastQueue().Enqueue(new ToastRequest(FeedbackKind.Info, " Xin chào ", "  ", ActionText: ""));

        Assert.Null(toast.Title);
        Assert.Null(toast.ActionText);
        Assert.Equal("Xin chào", toast.Message);
    }

    [Theory]
    [InlineData(FeedbackKind.Success, 3.5)]
    [InlineData(FeedbackKind.Info, 4.5)]
    [InlineData(FeedbackKind.Warning, 6)]
    [InlineData(FeedbackKind.Error, 8)]
    public void Default_duration_depends_on_kind(FeedbackKind kind, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ToastDurations.For(kind, "Ngắn"));
    }

    [Fact]
    public void Long_messages_get_more_time_but_never_beyond_the_cap()
    {
        var shortTime = ToastDurations.For(FeedbackKind.Info, new string('a', 60));
        var longer = ToastDurations.For(FeedbackKind.Info, new string('a', 90));
        var huge = ToastDurations.For(FeedbackKind.Error, new string('a', 5000));

        Assert.Equal(TimeSpan.FromSeconds(4.5), shortTime);
        Assert.Equal(TimeSpan.FromSeconds(6.5), longer);
        Assert.Equal(ToastDurations.Max, huge);
    }
}

public class ToastLayoutTests
{
    private static readonly Size Client = new(1000, 600);

    [Fact]
    public void Bottom_right_stacks_upwards_with_newest_at_the_bottom()
    {
        var sizes = new[] { new Size(300, 60), new Size(300, 80) };

        var bounds = ToastLayout.Arrange(Client, sizes, ToastPosition.BottomRight, margin: 16, gap: 8);

        Assert.Equal(new Rectangle(684, 504, 300, 80), bounds[1]); // newest
        Assert.Equal(new Rectangle(684, 436, 300, 60), bounds[0]);
    }

    [Fact]
    public void Top_center_stacks_downwards_with_newest_on_top()
    {
        var sizes = new[] { new Size(300, 60), new Size(300, 80) };

        var bounds = ToastLayout.Arrange(Client, sizes, ToastPosition.TopCenter, margin: 16, gap: 8);

        Assert.Equal(new Rectangle(350, 16, 300, 80), bounds[1]);
        Assert.Equal(new Rectangle(350, 104, 300, 60), bounds[0]);
    }

    [Fact]
    public void Toasts_that_do_not_fit_are_left_empty_oldest_first()
    {
        var sizes = new[] { new Size(300, 200), new Size(300, 200), new Size(300, 200) };

        var bounds = ToastLayout.Arrange(new Size(800, 450), sizes, ToastPosition.BottomRight, margin: 16, gap: 8);

        Assert.True(bounds[0].IsEmpty);
        Assert.False(bounds[1].IsEmpty);
        Assert.False(bounds[2].IsEmpty);
    }

    [Theory]
    [InlineData(1200, 360)]
    [InlineData(300, 268)]
    [InlineData(150, 200)]
    public void Width_narrows_for_small_windows_down_to_a_minimum(int clientWidth, int expected)
    {
        Assert.Equal(expected, ToastLayout.Width(clientWidth, preferred: 360, margin: 16));
    }
}

public class FeedbackPaletteTests
{
    public static TheoryData<ThemeMode, FeedbackKind> All()
    {
        var data = new TheoryData<ThemeMode, FeedbackKind>();
        foreach (var mode in Enum.GetValues<ThemeMode>())
        {
            foreach (var kind in Enum.GetValues<FeedbackKind>())
            {
                data.Add(mode, kind);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Texts_and_icons_are_readable_in_every_theme(ThemeMode mode, FeedbackKind kind)
    {
        var colors = FeedbackPalette.Resolve(Themes.For(mode), kind);

        Assert.True(ColorMath.ContrastRatio(colors.Title, colors.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.Text, colors.Background) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.IconForeground, colors.IconBackground) >= 4.5);
        Assert.True(ColorMath.ContrastRatio(colors.Accent, colors.Background) >= 3.0);
    }

    [Fact]
    public void Errors_and_danger_share_the_danger_color_and_questions_use_the_brand()
    {
        var theme = Themes.Light;

        Assert.Equal(theme.Colors.Danger, FeedbackPalette.AccentOf(theme, FeedbackKind.Error));
        Assert.Equal(theme.Colors.Danger, FeedbackPalette.AccentOf(theme, FeedbackKind.Danger));
        Assert.Equal(theme.Colors.Primary, FeedbackPalette.AccentOf(theme, FeedbackKind.Question));
        Assert.Equal(theme.Colors.Info, FeedbackPalette.AccentOf(theme, FeedbackKind.Info));
    }

    [Fact]
    public void Scrim_is_translucent()
    {
        foreach (var mode in Enum.GetValues<ThemeMode>())
        {
            var scrim = FeedbackPalette.Scrim(Themes.For(mode));
            Assert.InRange(scrim.A, 60, 200);
        }
    }
}

public class DialogSpecTests
{
    [Fact]
    public void Standard_sets_use_vietnamese_labels_in_windows_order()
    {
        var spec = DialogSpec.Standard(FeedbackKind.Question, "Phiếu nhập", "Lưu phiếu tạm?", DialogButtonSet.YesNoCancel);

        Assert.Equal(new[] { "Có", "Không", "Hủy" }, spec.Buttons.Select(b => b.Text));
        Assert.Equal(DialogButtonStyle.Primary, spec.Buttons[0].Style);
        Assert.All(spec.Buttons.Skip(1), b => Assert.Equal(DialogButtonStyle.Secondary, b.Style));
        Assert.Equal(DialogChoice.Yes, spec.DefaultChoice);
        Assert.Equal(DialogChoice.Cancel, spec.CancelChoice);
    }

    [Theory]
    [InlineData(DialogButtonSet.Ok, DialogChoice.Ok)]
    [InlineData(DialogButtonSet.OkCancel, DialogChoice.Cancel)]
    [InlineData(DialogButtonSet.YesNo, DialogChoice.No)]
    [InlineData(DialogButtonSet.YesNoCancel, DialogChoice.Cancel)]
    [InlineData(DialogButtonSet.RetryCancel, DialogChoice.Cancel)]
    [InlineData(DialogButtonSet.AbortRetryIgnore, DialogChoice.Abort)]
    public void Escape_gives_the_safe_answer_of_each_set(DialogButtonSet set, DialogChoice expected)
    {
        Assert.Equal(expected, DialogSpec.Standard(FeedbackKind.Info, "T", "M", set).CancelChoice);
    }

    [Fact]
    public void Default_index_is_clamped_to_the_buttons()
    {
        Assert.Equal(DialogChoice.No, DialogSpec.Standard(FeedbackKind.Question, "T", "M", DialogButtonSet.YesNo, defaultIndex: 1).DefaultChoice);
        Assert.Equal(DialogChoice.No, DialogSpec.Standard(FeedbackKind.Question, "T", "M", DialogButtonSet.YesNo, defaultIndex: 7).DefaultChoice);
        Assert.Equal(DialogChoice.Ok, DialogSpec.Standard(FeedbackKind.Info, "T", "M", DialogButtonSet.Ok, defaultIndex: -3).DefaultChoice);
    }

    [Fact]
    public void Danger_confirmation_has_a_red_button_and_defaults_to_the_safe_answer()
    {
        var spec = DialogSpec.Confirm("Phân quyền", "Xóa vai trò \"Kho 2\"?", "Xóa vai trò", "Giữ lại", danger: true);

        Assert.Equal(FeedbackKind.Danger, spec.Kind);
        Assert.Equal(new[] { "Xóa vai trò", "Giữ lại" }, spec.Buttons.Select(b => b.Text));
        Assert.Equal(DialogButtonStyle.Danger, spec.Buttons[0].Style);
        Assert.Equal(DialogChoice.No, spec.DefaultChoice);
        Assert.Equal(DialogChoice.No, spec.CancelChoice);
    }

    [Fact]
    public void Plain_confirmation_defaults_to_confirm_and_falls_back_to_standard_labels()
    {
        var spec = DialogSpec.Confirm("Kết ca", "Kết ca ngay?", " ", "");

        Assert.Equal(FeedbackKind.Question, spec.Kind);
        Assert.Equal(new[] { "Đồng ý", "Hủy" }, spec.Buttons.Select(b => b.Text));
        Assert.Equal(DialogChoice.Yes, spec.DefaultChoice);
    }

    [Fact]
    public void Danger_kind_in_a_standard_set_makes_the_first_button_red()
    {
        var spec = DialogSpec.Standard(FeedbackKind.Danger, "T", "M", DialogButtonSet.YesNo);

        Assert.Equal(DialogButtonStyle.Danger, spec.Buttons[0].Style);
    }

    [Fact]
    public void Access_keys_are_unaccented_first_letters_without_clashes()
    {
        var yesNoCancel = DialogSpec.Standard(FeedbackKind.Question, "T", "M", DialogButtonSet.YesNoCancel);
        Assert.Equal(new[] { 'C', 'K', 'H' }, yesNoCancel.Buttons.Select(b => b.AccessKey));

        // "Đóng" and "Đồng ý" both start with Đ → D; the second one takes its next free letter.
        var clash = DialogSpec.Confirm("T", "M", "Đóng phiếu", "Đồng ý");
        Assert.Equal(new[] { 'D', 'O' }, clash.Buttons.Select(b => b.AccessKey));
    }

    [Theory]
    [InlineData('c', DialogChoice.Yes)]
    [InlineData('K', DialogChoice.No)]
    [InlineData('y', DialogChoice.Yes)]
    [InlineData('n', DialogChoice.No)]
    [InlineData('h', DialogChoice.Cancel)]
    public void Letter_keys_press_buttons(char key, DialogChoice expected)
    {
        var spec = DialogSpec.Standard(FeedbackKind.Question, "T", "M", DialogButtonSet.YesNoCancel);

        Assert.Equal(expected, spec.ChoiceForKey(key));
    }

    [Fact]
    public void Unknown_keys_and_aliases_without_a_button_do_nothing()
    {
        var ok = DialogSpec.Alert(FeedbackKind.Info, "T", "M");

        Assert.Null(ok.ChoiceForKey('y'));
        Assert.Null(ok.ChoiceForKey('x'));
        Assert.Null(ok.ChoiceForKey(' '));
        Assert.Equal(DialogChoice.Ok, ok.ChoiceForKey('d'));
    }

    [Fact]
    public void Clipboard_text_matches_the_windows_message_box_layout()
    {
        var spec = DialogSpec.Alert(FeedbackKind.Error, "Xuất Excel", "Không lưu được file.", details: "IOException: file đang mở");

        var text = spec.ToClipboardText().ReplaceLineEndings("\n");

        Assert.Equal(
            "---------------------------\nXuất Excel\n---------------------------\nKhông lưu được file.\n\nIOException: file đang mở\n" +
            "---------------------------\nĐồng ý\n---------------------------",
            text);
    }

    [Fact]
    public void Blank_title_and_details_are_normalised()
    {
        var spec = DialogSpec.Alert(FeedbackKind.Info, "  ", " Xong ", details: "   ");

        Assert.Equal("GroceryPOS", spec.Title);
        Assert.Equal("Xong", spec.Message);
        Assert.Null(spec.Details);
    }

    [Theory]
    [InlineData(DialogChoice.Ok, "Đồng ý")]
    [InlineData(DialogChoice.Retry, "Thử lại")]
    [InlineData(DialogChoice.Ignore, "Bỏ qua")]
    [InlineData(DialogChoice.None, "")]
    public void Labels(DialogChoice choice, string label) => Assert.Equal(label, DialogSpec.LabelOf(choice));
}

public class BusyTrackerTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Quick_operations_never_show_the_overlay()
    {
        var tracker = new BusyTracker(Ms(250), Ms(400));
        var id = tracker.Begin(Ms(0));
        Assert.False(tracker.Evaluate(Ms(200)));
        tracker.End(id, Ms(240));

        Assert.False(tracker.IsBusy);
        Assert.False(tracker.Evaluate(Ms(1000)));
        Assert.Null(tracker.NextChangeIn(Ms(1000)));
    }

    [Fact]
    public void Slow_operation_shows_after_the_delay_and_stays_for_the_minimum_time()
    {
        var tracker = new BusyTracker(Ms(250), Ms(400));
        var id = tracker.Begin(Ms(0));
        Assert.Equal(Ms(150), tracker.NextChangeIn(Ms(100)));
        Assert.True(tracker.Evaluate(Ms(260)));

        tracker.End(id, Ms(300)); // finished 40 ms after appearing
        Assert.True(tracker.IsVisible);
        Assert.Equal(Ms(360), tracker.NextChangeIn(Ms(300)));
        Assert.True(tracker.Evaluate(Ms(600)));
        Assert.False(tracker.Evaluate(Ms(660)));
    }

    [Fact]
    public void Nested_operations_share_one_overlay_and_the_latest_message_wins()
    {
        var tracker = new BusyTracker(TimeSpan.Zero, TimeSpan.Zero);
        var outer = tracker.Begin(Ms(0), "Đang tải báo cáo…");
        var inner = tracker.Begin(Ms(10), "Đang xuất Excel…");
        var silent = tracker.Begin(Ms(20));

        Assert.True(tracker.IsVisible);
        Assert.Equal(3, tracker.Depth);
        Assert.Equal("Đang xuất Excel…", tracker.Message);

        tracker.End(inner, Ms(30));
        Assert.Equal("Đang tải báo cáo…", tracker.Message);
        tracker.End(outer, Ms(40));
        tracker.End(silent, Ms(50));
        tracker.End(silent, Ms(60)); // ending twice is harmless
        Assert.False(tracker.IsVisible);
        Assert.Null(tracker.Message);
    }

    [Fact]
    public void New_work_while_the_overlay_is_lingering_keeps_it_up()
    {
        var tracker = new BusyTracker(Ms(250), Ms(400));
        var first = tracker.Begin(Ms(0));
        tracker.Evaluate(Ms(300));
        tracker.End(first, Ms(350));
        tracker.Begin(Ms(500));

        Assert.True(tracker.Evaluate(Ms(1000)));
        Assert.Null(tracker.NextChangeIn(Ms(1000)));
    }

    [Fact]
    public void Default_rules_and_validation()
    {
        var tracker = BusyTracker.CreateDefault();
        Assert.Equal(Ms(250), tracker.ShowDelay);
        Assert.Equal(Ms(400), tracker.MinimumVisible);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BusyTracker(Ms(-1), Ms(0)));
    }
}

public class SpinnerGeometryTests
{
    [Fact]
    public void Arc_sweep_stays_within_bounds_and_start_within_a_turn()
    {
        for (var ms = 0; ms < 5000; ms += 37)
        {
            var (start, sweep) = SpinnerGeometry.ArcAt(TimeSpan.FromMilliseconds(ms));
            Assert.InRange(sweep, SpinnerGeometry.MinSweep, SpinnerGeometry.MaxSweep);
            Assert.InRange(start, 0f, 360f);
        }
    }

    [Fact]
    public void Arc_starts_short_and_is_longest_half_way_through_its_cycle()
    {
        var (_, atStart) = SpinnerGeometry.ArcAt(TimeSpan.Zero);
        var (_, halfWay) = SpinnerGeometry.ArcAt(SpinnerGeometry.SweepPeriod / 2);

        Assert.Equal(SpinnerGeometry.MinSweep, atStart, 3);
        Assert.Equal(SpinnerGeometry.MaxSweep, halfWay, 3);
    }

    [Fact]
    public void Spinner_rotates_a_quarter_turn_in_a_quarter_period()
    {
        var (start, _) = SpinnerGeometry.ArcAt(SpinnerGeometry.RotationPeriod / 4);

        Assert.Equal(90f, start, 3);
    }
}
