using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Presentation.Inventory;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Điều chỉnh tồn kho for one product: reason (fixing the direction where it has one), quantity or the actual
/// stock on the shelf, note, and a live preview of stock and value after – or, in red, why it cannot be saved.
/// Passive MVP view of <see cref="StockAdjustmentPresenter"/>; the state lives in its <see cref="StockAdjustmentDraft"/>.
/// </summary>
internal sealed class StockAdjustmentDialog : BaseEditDialog, IStockAdjustmentView
{
    private const string ReasonKey = "reason";

    private readonly StockAdjustmentPresenter _presenter;
    private readonly ModernComboBox _reason;
    private readonly ModernSegmentedControl _direction;
    private readonly ModernNumericBox _quantity;
    private readonly ModernNumericBox _target;
    private readonly ModernTextBox _note;
    private readonly FormNote _preview;
    private bool _syncing;

    public StockAdjustmentDialog(StockAdjustmentPresenter presenter)
        : base(StockAdjustmentPresenter.Title, columns: 2, contentWidth: 432)
    {
        _presenter = presenter;
        SaveText = "Lưu điều chỉnh";

        AddNote(presenter.Heading, role: FontRole.Subtitle, style: FontStyle.Bold);
        AddNote(presenter.CurrentStockText, NoteTone.Muted);
        _reason = AddField("Lý do", new ModernComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(StockAdjustmentReasonOption.Name),
            ValueMember = nameof(StockAdjustmentReasonOption.Reason),
            DataSource = StockAdjustmentPresenter.ReasonOptions.ToList(),
        }, fullWidth: true, trackAs: ReasonKey);
        _direction = AddField("Hướng điều chỉnh", new ModernSegmentedControl(), fullWidth: true);
        _direction.SetItems(["Giảm tồn (xuất bớt)", "Tăng tồn (nhập thêm)"]);
        _quantity = AddField(presenter.QuantityCaption, new ModernNumericBox { DecimalPlaces = 3, Minimum = 0, Maximum = 100_000 });
        _target = AddField("hoặc tồn thực tế sau điều chỉnh", new ModernNumericBox { DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, AllowEmpty = true }, track: false);
        _note = AddField("Ghi chú", new ModernTextBox { MaxLength = 500, PlaceholderText = StockAdjustmentPresenter.DefaultNotePlaceholder }, fullWidth: true);
        _preview = AddNote(null, minLines: 2);

        _reason.SelectedIndexChanged += (_, _) =>
        {
            if (_reason.SelectedValue is StockAdjustmentReason reason)
            {
                _presenter.SetReason(reason);
                SyncFromDraft(keepTarget: false);
            }
        };
        _direction.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncing && _presenter.Draft.CanChooseDirection)
            {
                _presenter.SetIncrease(_direction.SelectedIndex == 1);
                SyncFromDraft(keepTarget: false);
            }
        };
        _quantity.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _presenter.SetQuantity(_quantity.ValueOrZero);
                SyncFromDraft(keepTarget: false);
            }
        };
        _target.ValueChanged += (_, _) =>
        {
            if (_syncing || _target.Value is not { } target)
            {
                return;
            }

            if (_presenter.SetTargetBalance(target) is { } warning)
            {
                _target.SetWarning(warning);
            }
            else
            {
                _target.ClearValidation();
                SyncFromDraft(keepTarget: true);
            }
        };
        _note.InnerTextBox.TextChanged += (_, _) =>
        {
            _presenter.SetNote(_note.Text);
            Render();
        };

        _presenter.Attach(this);
        SyncFromDraft(keepTarget: false);
    }

    /// <summary>The saved adjustment once the dialog closed with OK.</summary>
    public StockAdjustmentResult? Adjusted => _presenter.Adjusted;

    bool IView.IsAlive => IsAlive;

    protected override bool CanSave => _presenter.Draft.CanSave;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _quantity.Select(); // reason defaults to "Hư hỏng": usually only the quantity is typed
    }

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    decimal IStockAdjustmentView.CommitQuantity()
    {
        _quantity.Commit();
        return _quantity.ValueOrZero;
    }

    void IStockAdjustmentView.FocusQuantity() => _quantity.Select();

    private void SyncFromDraft(bool keepTarget)
    {
        var draft = _presenter.Draft;
        _syncing = true;
        try
        {
            _direction.Enabled = draft.CanChooseDirection;
            _direction.SelectedIndex = draft.Increase ? 1 : 0;
            if (_quantity.ValueOrZero != draft.Quantity)
            {
                _quantity.Value = draft.Quantity;
            }

            if (!keepTarget)
            {
                _target.Value = _presenter.SuggestedTarget;
                _target.ClearValidation();
            }
        }
        finally
        {
            _syncing = false;
        }

        Render();
    }

    private void Render()
    {
        _note.PlaceholderText = _presenter.NotePlaceholder;
        _direction.Accent = _presenter.SaveIsDanger ? SegmentAccent.Danger : SegmentAccent.Primary;
        var (text, tone) = _presenter.Preview;
        _preview.Set(text, ToNoteTone(tone));

        if (!IsSaving)
        {
            SaveButton.Enabled = _presenter.Draft.CanSave;
        }

        SaveButton.Variant = _presenter.SaveIsDanger ? ButtonVariant.Danger : ButtonVariant.Primary;
    }

    private static NoteTone ToNoteTone(Tone tone) => tone switch
    {
        Tone.Muted => NoteTone.Muted,
        Tone.Success => NoteTone.Success,
        Tone.Warning => NoteTone.Warning,
        Tone.Danger => NoteTone.Danger,
        _ => NoteTone.Normal,
    };

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
