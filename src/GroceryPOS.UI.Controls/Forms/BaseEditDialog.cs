using System.ComponentModel;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Forms;

/// <summary>
/// Base class for add/edit dialogs. Derived dialogs only declare fields with <see cref="AddField{T}"/> and
/// implement <see cref="SaveAsync"/>; the base lays the fields out in a 1–3 column grid (<see cref="FieldGrid"/>),
/// sizes the window to fit, shows a themed error banner, puts <c>Hủy</c> / <c>Lưu</c> in the footer
/// (Enter saves from single-line fields, Esc cancels), blocks closing while saving, and asks before
/// discarding unsaved changes (<see cref="DirtyTracker"/>).
/// </summary>
public abstract class BaseEditDialog : BaseForm
{
    private const int OuterPadding = 24;
    private const int FooterGap = 20;

    private readonly List<(Label? Caption, Control Field, FieldSlot Slot)> _fields = new();
    private readonly FormErrorBanner _error;
    private readonly ModernButton _save;
    private readonly ModernButton _cancel;
    private readonly int _columns;
    private readonly int _contentWidth;
    private bool _saving;
    private bool _layingOut;

    protected BaseEditDialog(string title, int columns = 1, int contentWidth = 340)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, 3);
        _columns = columns;
        _contentWidth = contentWidth;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        CloseOnEscape = true;

        _error = new FormErrorBanner { Width = contentWidth };
        _error.SizeChanged += (_, _) => PerformFieldLayout();
        _error.VisibleChanged += (_, _) => PerformFieldLayout();

        _save = new ModernButton { Text = "Lưu", Variant = ButtonVariant.Primary, IconGlyph = Glyphs.Save, Width = 120 };
        _save.Click += async (_, _) => await SaveCoreAsync();
        _cancel = new ModernButton { Text = "Hủy", Variant = ButtonVariant.Secondary, Width = 100 };
        _cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        AcceptButton = _save;
        Controls.AddRange([_error, _save, _cancel]);
    }

    /// <summary>Tracks the values the dialog opened with; derived dialogs may track extra values or accept program-filled ones.</summary>
    protected DirtyTracker Changes { get; } = new();

    protected ModernButton SaveButton => _save;

    protected ModernButton CancelDialogButton => _cancel;

    /// <summary>Text of the save button ("Lưu", "Tạo", "Đặt lại mật khẩu"…).</summary>
    [Category("Appearance")]
    public string SaveText
    {
        get => _save.Text;
        set => _save.Text = value;
    }

    /// <summary>True while <see cref="SaveAsync"/> runs.</summary>
    protected bool IsSaving => _saving;

    /// <summary>
    /// Whether the save button may be enabled once a save attempt has finished; dialogs that disable it while their
    /// state has a blocker override this so a failed save does not re-enable it.
    /// </summary>
    protected virtual bool CanSave => true;

    /// <summary>When true (default), closing with unsaved changes asks for confirmation.</summary>
    protected bool ConfirmDiscard { get; set; } = true;

    /// <summary>
    /// Adds a field with an optional caption and returns the control, so it can be kept in a field:
    /// <c>_name = AddField("Tên danh mục", new ModernTextBox());</c>. Its value is tracked for unsaved changes
    /// under <paramref name="trackAs"/> (default: the caption) unless <paramref name="track"/> is false.
    /// </summary>
    protected T AddField<T>(string? caption, T control, bool fullWidth = false, bool startsRow = false, bool track = true, string? trackAs = null)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        Label? label = null;
        if (!string.IsNullOrEmpty(caption))
        {
            label = new Label { Text = caption, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.BottomLeft };
            Controls.Add(label);
        }

        Controls.Add(control);
        _fields.Add((label, control, new FieldSlot(control.Height, label is not null, fullWidth, startsRow)));
        control.SizeChanged += (_, _) => PerformFieldLayout(); // e.g. a FormNote preview growing to two lines

        if (track)
        {
            var key = trackAs ?? caption ?? $"field{_fields.Count}";
            Changes.Track(key, () => ReadValue(control));
        }

        PerformFieldLayout();
        return control;
    }

    /// <summary>
    /// Adds a caption-less, full-width line of wrapped text (context, hint or live preview). Its height follows
    /// the text, and the dialog re-lays out when it changes.
    /// </summary>
    protected FormNote AddNote(string? text, NoteTone tone = NoteTone.Normal, FontRole role = FontRole.Body, FontStyle style = FontStyle.Regular, int minLines = 1)
    {
        var note = new FormNote { FontRole = role, TextStyle = style, Tone = tone, MinLines = minLines, Width = _contentWidth };
        note.Text = text;
        return AddField(null, note, fullWidth: true, track: false);
    }

    /// <summary>
    /// Saves the dialog. Return null on success (the dialog closes with <see cref="DialogResult.OK"/>) or a
    /// Vietnamese error message to show in the banner (the dialog stays open). An empty string keeps the dialog
    /// open without a message (e.g. the user declined a "save anyway?" confirmation).
    /// </summary>
    protected abstract Task<string?> SaveAsync();

    /// <summary>Shows (or with null, clears) the error banner.</summary>
    protected void ShowFormError(string? message) => _error.Text = message;

    /// <summary>Places captions, fields, the error banner and the footer, and sizes the window to fit.</summary>
    protected void PerformFieldLayout()
    {
        if (_layingOut || _error is null || _save is null)
        {
            return;
        }

        _layingOut = true;
        SuspendLayout();
        try
        {
            var area = new Rectangle(OuterPadding, OuterPadding, _contentWidth, 0);
            FieldGridLayout layout;
            var pass = 0;
            bool heightsChanged;
            do
            {
                // A field whose height follows its width (wrapped notes) can change height when placed: lay out again.
                var heights = _fields.Select(f => f.Field.Height).ToArray();
                layout = FieldGrid.Arrange(
                    _fields.Select((f, i) => f.Slot with { FieldHeight = heights[i] }).ToList(), area, _columns, FieldGridMetrics.Default);
                for (var i = 0; i < _fields.Count; i++)
                {
                    var (caption, field, _) = _fields[i];
                    var place = layout.Placements[i];
                    if (caption is not null)
                    {
                        caption.Bounds = place.Label;
                    }

                    field.Location = place.Field.Location;
                    field.Width = place.Field.Width;
                }

                heightsChanged = _fields.Where((f, i) => f.Field.Height != heights[i]).Any();
            }
            while (heightsChanged && ++pass < 3);

            var y = layout.Bottom + FooterGap;
            _error.Location = new Point(OuterPadding, y);
            _error.Width = _contentWidth;
            if (!string.IsNullOrWhiteSpace(_error.Text))
            {
                y = _error.Bottom + 12;
            }

            var right = OuterPadding + _contentWidth;
            _save.Location = new Point(right - _save.Width, y);
            _cancel.Location = new Point(_save.Left - 8 - _cancel.Width, y);
            ClientSize = new Size(_contentWidth + (OuterPadding * 2), _save.Bottom + OuterPadding);
        }
        finally
        {
            ResumeLayout(true);
            _layingOut = false;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        PerformFieldLayout();
        base.OnLoad(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Changes.AcceptChanges();
        if (_fields.FirstOrDefault(f => f.Field.Enabled && f.Field.CanSelect && !IsReadOnly(f.Field)).Field is { } first)
        {
            first.Select();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (_saving)
        {
            e.Cancel = true;
            return;
        }

        if (DialogResult != DialogResult.OK
            && e.CloseReason == CloseReason.UserClosing
            && ConfirmDiscard
            && Changes.IsDirty
            && !Confirm("Bạn có thay đổi chưa lưu. Đóng và bỏ các thay đổi này?", Text, "Bỏ thay đổi", "Tiếp tục sửa", danger: true))
        {
            e.Cancel = true;
            DialogResult = DialogResult.None;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            _save.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task SaveCoreAsync()
    {
        if (_saving || !_save.Enabled)
        {
            return;
        }

        _saving = true;
        _save.Enabled = false;
        _cancel.Enabled = false;
        ShowFormError(null);
        string? error;
        try
        {
            error = await SaveAsync();
        }
        finally
        {
            _saving = false;
            if (IsAlive)
            {
                _save.Enabled = CanSave;
                _cancel.Enabled = true;
            }
        }

        if (!IsAlive)
        {
            return;
        }

        if (error is null)
        {
            Changes.AcceptChanges();
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        ShowFormError(error);
    }

    private static bool IsReadOnly(Control control) => control switch
    {
        ModernTextBox box => box.ReadOnly,
        TextBoxBase box => box.ReadOnly,
        _ => false,
    };

    private static string? ReadValue(Control control) => control switch
    {
        ModernNumericBox numeric => numeric.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ModernComboBox combo => (combo.SelectedValue ?? combo.SelectedItem)?.ToString(),
        ModernSegmentedControl segments => segments.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CheckBox check => check.Checked ? "1" : "0",
        _ => control.Text,
    };
}
