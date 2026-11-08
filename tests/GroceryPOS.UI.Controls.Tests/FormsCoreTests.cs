using System.Drawing;
using GroceryPOS.UI.Controls.Forms.Core;

namespace GroceryPOS.UI.Controls.Tests;

public class LoadSequencerTests
{
    [Fact]
    public void Newer_load_cancels_and_supersedes_the_previous_one()
    {
        using var loads = new LoadSequencer();
        var first = loads.Begin();
        var second = loads.Begin();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(loads.IsCurrent(first));
        Assert.False(loads.Complete(first));
        Assert.True(loads.IsLoading);

        Assert.True(loads.Complete(second));
        Assert.False(loads.IsLoading);
    }

    [Fact]
    public void Slow_first_load_finishing_after_second_is_dropped()
    {
        using var loads = new LoadSequencer();
        var first = loads.Begin();
        var second = loads.Begin();

        Assert.True(loads.Complete(second));
        Assert.False(loads.Complete(first));
        Assert.Equal(2, loads.Version);
    }

    [Fact]
    public void Cancel_stops_the_running_load_and_clears_loading()
    {
        using var loads = new LoadSequencer();
        var ticket = loads.Begin();

        loads.Cancel();

        Assert.True(ticket.Token.IsCancellationRequested);
        Assert.False(loads.IsLoading);
        Assert.False(loads.Complete(ticket));
    }

    [Fact]
    public void Disposed_sequencer_rejects_new_loads_and_results()
    {
        var loads = new LoadSequencer();
        var ticket = loads.Begin();
        loads.Dispose();

        Assert.False(loads.IsCurrent(ticket));
        Assert.False(loads.IsLoading);
        Assert.Throws<ObjectDisposedException>(() => loads.Begin());
        loads.Dispose(); // idempotent
    }

    [Fact]
    public void Fresh_sequencer_is_not_loading()
    {
        using var loads = new LoadSequencer();
        Assert.False(loads.IsLoading);
        Assert.Equal(0, loads.Version);
    }
}

public class DirtyTrackerTests
{
    [Fact]
    public void Tracks_changes_against_the_baseline()
    {
        var name = "Sữa";
        var note = (string?)null;
        var tracker = new DirtyTracker();
        tracker.Track("name", () => name);
        tracker.Track("note", () => note);

        Assert.False(tracker.IsDirty);

        name = "Sữa tươi";
        Assert.True(tracker.IsDirty);
        Assert.Equal(["name"], tracker.ChangedKeys);

        name = "Sữa";
        Assert.False(tracker.IsDirty);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "   ")]
    [InlineData("Bánh", " Bánh ")]
    public void Null_empty_and_surrounding_spaces_are_not_changes(string? initial, string? edited)
    {
        var value = initial;
        var tracker = new DirtyTracker();
        tracker.Track("v", () => value);

        value = edited;

        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void AcceptChanges_takes_current_values_as_baseline()
    {
        var value = "a";
        var tracker = new DirtyTracker();
        tracker.Track("v", () => value);
        value = "b";
        Assert.True(tracker.IsDirty);

        tracker.AcceptChanges();

        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void AcceptChange_rebaselines_only_one_key()
    {
        string code = string.Empty, name = "x";
        var tracker = new DirtyTracker();
        tracker.Track("code", () => code);
        tracker.Track("name", () => name);

        code = "NCC0007"; // suggested by the program after the dialog opened
        name = "y";
        tracker.AcceptChange("code");

        Assert.False(tracker.IsChanged("code"));
        Assert.True(tracker.IsChanged("name"));
        Assert.Equal(["name"], tracker.ChangedKeys);
    }

    [Fact]
    public void Retracking_a_key_replaces_the_reader_and_keeps_its_position()
    {
        var tracker = new DirtyTracker();
        tracker.Track("a", () => "1");
        tracker.Track("b", () => "2");
        tracker.Track("a", () => "3");

        Assert.Equal(["a", "b"], tracker.Keys);
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void Untracked_keys_no_longer_count()
    {
        var value = "a";
        var tracker = new DirtyTracker();
        tracker.Track("v", () => value);
        value = "b";

        Assert.True(tracker.Untrack("v"));
        Assert.False(tracker.Untrack("v"));
        Assert.False(tracker.IsDirty);
        Assert.False(tracker.IsChanged("missing"));
    }

    [Fact]
    public void Rejects_empty_keys()
    {
        var tracker = new DirtyTracker();
        Assert.Throws<ArgumentException>(() => tracker.Track(" ", () => null));
    }
}

public class ListCommandTests
{
    private sealed record Row(string Name, bool IsActive);

    [Fact]
    public void Selection_command_is_disabled_without_a_row()
    {
        var edit = new ListCommand<Row>("Sửa") { RequiresSelection = true };

        Assert.Equal(new CommandState(true, false, "Sửa"), edit.Evaluate(null));
        Assert.Equal(new CommandState(true, true, "Sửa"), edit.Evaluate(new Row("A", true)));
    }

    [Fact]
    public void Command_without_selection_is_enabled_with_or_without_a_row()
    {
        var add = new ListCommand<Row>("Thêm");

        Assert.True(add.Evaluate(null).Enabled);
        Assert.True(add.Evaluate(new Row("A", true)).Enabled);
    }

    [Fact]
    public void Text_follows_the_selected_row()
    {
        var toggle = new ListCommand<Row>("Ngừng dùng")
        {
            RequiresSelection = true,
            TextFor = r => r.IsActive ? "Ngừng dùng" : "Dùng lại",
        };

        Assert.Equal("Dùng lại", toggle.Evaluate(new Row("A", false)).Text);
        Assert.Equal("Ngừng dùng", toggle.Evaluate(new Row("A", true)).Text);
        Assert.Equal("Ngừng dùng", toggle.Evaluate(null).Text);
    }

    [Fact]
    public void Blank_row_text_falls_back_to_the_default_text()
    {
        var command = new ListCommand<Row>("Sửa") { TextFor = _ => " " };
        Assert.Equal("Sửa", command.Evaluate(new Row("A", true)).Text);
    }

    [Fact]
    public void Row_condition_disables_for_rows_that_do_not_qualify()
    {
        var pay = new ListCommand<Row>("Trả nợ") { RequiresSelection = true, EnabledWhen = r => r.IsActive };

        Assert.False(pay.Evaluate(new Row("A", false)).Enabled);
        Assert.True(pay.Evaluate(new Row("A", true)).Enabled);
    }

    [Fact]
    public void Unauthorized_command_is_disabled_or_hidden()
    {
        var disabled = new ListCommand<Row>("Sửa") { Allowed = false };
        var hidden = new ListCommand<Row>("Xóa") { Allowed = false, WhenUnauthorized = UnauthorizedBehavior.Hide };
        var row = new Row("A", true);

        Assert.Equal(new CommandState(true, false, "Sửa"), disabled.Evaluate(row));
        Assert.Equal(new CommandState(false, false, "Xóa"), hidden.Evaluate(row));
    }

    [Fact]
    public void Availability_condition_applies_without_a_row()
    {
        var busy = true;
        var add = new ListCommand<Row>("Thêm") { Available = () => !busy };

        Assert.False(add.Evaluate(null).Enabled);
        busy = false;
        Assert.True(add.Evaluate(null).Enabled);
    }

    [Fact]
    public void Sizing_text_is_the_longer_of_the_texts()
    {
        Assert.Equal("Giao dịch lại xa", new ListCommand<Row>("Ngừng") { LongestText = "Giao dịch lại xa" }.SizingText);
        Assert.Equal("Ngừng giao dịch", new ListCommand<Row>("Ngừng giao dịch") { LongestText = "Ngắn" }.SizingText);
        Assert.Equal("Sửa", new ListCommand<Row>("Sửa").SizingText);
    }

    [Fact]
    public void Rejects_blank_text()
    {
        Assert.Throws<ArgumentException>(() => new ListCommand<Row>(""));
    }
}

public class FieldGridTests
{
    private static readonly FieldGridMetrics M = new(ColumnGap: 16, LabelHeight: 20, LabelGap: 4, RowGap: 14);
    private static readonly Rectangle Area = new(24, 24, 496, 0);

    [Fact]
    public void Single_column_stacks_fields_with_captions()
    {
        var layout = FieldGrid.Arrange([new FieldSlot(36), new FieldSlot(36)], new Rectangle(24, 24, 340, 0), 1, M);

        Assert.Equal(new Rectangle(24, 24, 340, 20), layout.Placements[0].Label);
        Assert.Equal(new Rectangle(24, 48, 340, 36), layout.Placements[0].Field);
        Assert.Equal(new Rectangle(24, 98, 340, 20), layout.Placements[1].Label);
        Assert.Equal(new Rectangle(24, 122, 340, 36), layout.Placements[1].Field);
        Assert.Equal(158, layout.Bottom);
        Assert.Equal(2, layout.Rows);
    }

    [Fact]
    public void Two_columns_flow_left_to_right_and_full_width_takes_its_own_row()
    {
        var layout = FieldGrid.Arrange(
            [new FieldSlot(36), new FieldSlot(36), new FieldSlot(36, FullWidth: true), new FieldSlot(36), new FieldSlot(36)],
            Area, 2, M);

        var p = layout.Placements;
        Assert.Equal((0, 0), (p[0].Row, p[0].Column));
        Assert.Equal((0, 1), (p[1].Row, p[1].Column));
        Assert.Equal((1, 0), (p[2].Row, p[2].Column));
        Assert.Equal((2, 0), (p[3].Row, p[3].Column));
        Assert.Equal((2, 1), (p[4].Row, p[4].Column));

        Assert.Equal(240, p[0].Field.Width);
        Assert.Equal(24 + 240 + 16, p[1].Field.X);
        Assert.Equal(Area.Right, p[1].Field.Right);
        Assert.Equal(496, p[2].Field.Width);
        Assert.Equal(3, layout.Rows);
    }

    [Fact]
    public void Full_width_after_a_half_row_starts_a_new_row()
    {
        var layout = FieldGrid.Arrange([new FieldSlot(36), new FieldSlot(36, FullWidth: true), new FieldSlot(36)], Area, 2, M);

        Assert.Equal(0, layout.Placements[0].Row);
        Assert.Equal(1, layout.Placements[1].Row);
        Assert.Equal((2, 0), (layout.Placements[2].Row, layout.Placements[2].Column));
    }

    [Fact]
    public void StartsRow_forces_a_new_row()
    {
        var layout = FieldGrid.Arrange([new FieldSlot(36), new FieldSlot(36, StartsRow: true)], Area, 2, M);

        Assert.Equal((1, 0), (layout.Placements[1].Row, layout.Placements[1].Column));
    }

    [Fact]
    public void Row_height_is_the_tallest_field_and_inputs_line_up_without_captions()
    {
        var layout = FieldGrid.Arrange(
            [new FieldSlot(56), new FieldSlot(24, HasLabel: false), new FieldSlot(36)],
            Area, 2, M);

        var p = layout.Placements;
        Assert.Equal(p[0].Field.Y, p[1].Field.Y); // the check box lines up with the captioned input
        Assert.Equal(Rectangle.Empty, p[1].Label);
        Assert.Equal(24 + 24 + 56 + 14, p[2].Label.Y);
    }

    [Fact]
    public void Row_without_any_caption_has_no_caption_block()
    {
        var layout = FieldGrid.Arrange([new FieldSlot(24, HasLabel: false)], new Rectangle(0, 10, 100, 0), 1, M);

        Assert.Equal(10, layout.Placements[0].Field.Y);
        Assert.Equal(34, layout.Bottom);
    }

    [Fact]
    public void Three_columns_absorb_the_rounding_remainder_in_the_last_column()
    {
        var layout = FieldGrid.Arrange([new FieldSlot(36), new FieldSlot(36), new FieldSlot(36)], new Rectangle(0, 0, 100, 0), 3, M);

        Assert.Equal(22, layout.Placements[0].Field.Width); // (100 - 32) / 3
        Assert.Equal(100, layout.Placements[2].Field.Right);
        Assert.Equal(1, layout.Rows);
    }

    [Fact]
    public void Empty_grid_ends_at_the_top()
    {
        var layout = FieldGrid.Arrange([], Area, 2, M);

        Assert.Empty(layout.Placements);
        Assert.Equal(Area.Top, layout.Bottom);
        Assert.Equal(0, layout.Rows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Rejects_unsupported_column_counts(int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldGrid.Arrange([new FieldSlot(36)], Area, columns, M));
    }
}
