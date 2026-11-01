using System.ComponentModel;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Inputs;

/// <summary>
/// Period filter for reports and lists: ◀ [Tháng này ▾] [01/11/2026] – [19/11/2026] ▶.
/// Picking a preset fills both dates; typing a date switches the list to the matching preset or "Tùy chọn";
/// ◀/▶ (also Alt+←/Alt+→) step to the previous/next period of the same kind. State and rules live in the
/// tested <see cref="DateRangeSelection"/>; <see cref="RangeChanged"/> fires once per effective change.
/// </summary>
[DefaultEvent(nameof(RangeChanged))]
public class ModernDateRangePicker : UserControl
{
    private readonly ModernButton _previous;
    private readonly ModernButton _next;
    private readonly ModernComboBox _presets;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly FlowLayoutPanel _layout;
    private DateRangeSelection _selection;
    private bool _syncing;

    public ModernDateRangePicker()
    {
        _selection = new DateRangeSelection(DateOnly.FromDateTime(DateTime.Today));

        _previous = ArrowButton(Glyphs.ChevronLeft, "Kỳ trước (Alt+←)");
        _previous.Click += (_, _) => Shift(-1);
        _next = ArrowButton(Glyphs.ChevronRight, "Kỳ sau (Alt+→)");
        _next.Click += (_, _) => Shift(1);

        _presets = new ModernComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            Margin = new Padding(4, 0, 0, 0),
        };
        foreach (var preset in DatePresets.All)
        {
            _presets.Items.Add(DatePresets.Label(preset));
        }

        _presets.Items.Add(DateRangeSelection.CustomLabel);
        _presets.SelectedIndexChanged += OnPresetChanged;

        _from = new ModernDatePicker { Width = 136, ReserveMessageSpace = false, Margin = new Padding(8, 0, 0, 0) };
        _to = new ModernDatePicker { Width = 136, ReserveMessageSpace = false, Margin = new Padding(0, 0, 0, 0) };
        _from.ValueChanged += (_, _) => OnDateEdited(isFrom: true);
        _to.ValueChanged += (_, _) => OnDateEdited(isFrom: false);
        var dash = new Label { Text = "–", AutoSize = true, Margin = new Padding(6, 12, 6, 0) };

        _layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        _layout.Controls.AddRange([_previous, _presets, _from, dash, _to, _next]);
        Controls.Add(_layout);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);
        SyncControls();
    }

    /// <summary>Raised after the range (or only its preset label) changed through the UI or code.</summary>
    public event EventHandler? RangeChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateRange Range => _selection.Range;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DatePreset? Preset => _selection.Preset;

    /// <summary>"Tháng này: 01/11/2026 – 19/11/2026" for report subtitles and export headers.</summary>
    public string Describe() => _selection.Describe();

    /// <summary>Restarts the picker on <paramref name="preset"/>, allowing at most <paramref name="maxDays"/> days.</summary>
    public void Initialize(DatePreset preset, int maxDays = DateRangeSelection.DefaultMaxDays)
    {
        _selection = new DateRangeSelection(DateOnly.FromDateTime(DateTime.Today), preset, maxDays);
        SyncControls();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectPreset(DatePreset preset) => Update(_selection.SelectPreset(preset));

    public void SetRange(DateOnly from, DateOnly to) => Update(_selection.SetRange(from, to));

    /// <summary>Call when the screen is shown again or on refresh: follows a preset into a new day.</summary>
    public void RefreshToday() => Update(_selection.Rebase(DateOnly.FromDateTime(DateTime.Today)));

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.Left:
                Shift(-1);
                return true;
            case Keys.Alt | Keys.Right:
                Shift(1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static ModernButton ArrowButton(string glyph, string tip)
    {
        var button = new ModernButton
        {
            IconGlyph = glyph,
            Variant = ButtonVariant.Ghost,
            Size = new Size(36, 40),
            Margin = new Padding(0),
            TabStop = false,
            AccessibleName = tip,
        };
        new ToolTip().SetToolTip(button, tip);
        return button;
    }

    private void Shift(int direction) => Update(_selection.Shift(direction));

    private void OnPresetChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var index = _presets.SelectedIndex;
        if (index >= 0 && index < DatePresets.All.Count)
        {
            Update(_selection.SelectPreset(DatePresets.All[index]));
        }
        else
        {
            // "Tùy chọn" only hands focus to the dates; the range itself does not change.
            _from.Focus();
            SyncControls();
        }
    }

    private void OnDateEdited(bool isFrom)
    {
        if (_syncing)
        {
            return;
        }

        var picker = isFrom ? _from : _to;
        if (picker.Date is not { } date)
        {
            return;
        }

        Update(isFrom ? _selection.SetFrom(date) : _selection.SetTo(date));
    }

    private void Update(bool changed)
    {
        SyncControls();
        if (changed)
        {
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncControls()
    {
        _syncing = true;
        try
        {
            var max = _selection.MaxDate.ToDateTime(TimeOnly.MinValue);
            _from.MaxDate = max;
            _to.MaxDate = max;
            _from.Date = _selection.Range.From;
            _to.Date = _selection.Range.To;
            _presets.SelectedIndex = _selection.Preset is { } preset
                ? IndexOf(preset)
                : DatePresets.All.Count;
            _next.Enabled = _selection.CanShiftForward;
        }
        finally
        {
            _syncing = false;
        }
    }

    private static int IndexOf(DatePreset preset)
    {
        for (var i = 0; i < DatePresets.All.Count; i++)
        {
            if (DatePresets.All[i] == preset)
            {
                return i;
            }
        }

        return -1;
    }
}
