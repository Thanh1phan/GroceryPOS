using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Điều chỉnh tồn kho for one product: reason (fixing the direction where it has one), quantity or the actual
/// stock on the shelf, note, and a live preview of stock and value after – or, in red, why it cannot be saved.
/// State lives in <see cref="StockAdjustmentDraft"/>.
/// </summary>
internal sealed class StockAdjustmentDialog : BaseEditDialog
{
    private const string ReasonKey = "reason";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockAdjustmentDraft _draft;
    private readonly ModernComboBox _reason;
    private readonly ModernSegmentedControl _direction;
    private readonly ModernNumericBox _quantity;
    private readonly ModernNumericBox _target;
    private readonly ModernTextBox _note;
    private readonly FormNote _preview;
    private bool _syncing;

    public StockAdjustmentDialog(IServiceScopeFactory scopeFactory, StockItem product)
        : base("Điều chỉnh tồn kho", columns: 2, contentWidth: 432)
    {
        _scopeFactory = scopeFactory;
        _draft = new StockAdjustmentDraft(product);
        SaveText = "Lưu điều chỉnh";

        AddNote($"{product.ProductCode} – {product.ProductName}", role: FontRole.Subtitle, style: FontStyle.Bold);
        AddNote(
            $"Tồn hiện tại: {Qty(product.StockQuantity)} {product.Unit} · định mức {Qty(product.MinStockLevel)} · giá vốn {Vnd.Format(product.CostPrice)}",
            NoteTone.Muted);
        _reason = AddField("Lý do", new ModernComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(ReasonOption.Name),
            ValueMember = nameof(ReasonOption.Reason),
            DataSource = StockAdjustmentDraft.Reasons.Select(r => new ReasonOption(r, Describe(r))).ToList(),
        }, fullWidth: true, trackAs: ReasonKey);
        _direction = AddField("Hướng điều chỉnh", new ModernSegmentedControl(), fullWidth: true);
        _direction.SetItems(["Giảm tồn (xuất bớt)", "Tăng tồn (nhập thêm)"]);
        _quantity = AddField($"Số lượng điều chỉnh ({product.Unit})", new ModernNumericBox { DecimalPlaces = 3, Minimum = 0, Maximum = 100_000 });
        _target = AddField("hoặc tồn thực tế sau điều chỉnh", new ModernNumericBox { DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, AllowEmpty = true }, track: false);
        _note = AddField("Ghi chú", new ModernTextBox { MaxLength = 500, PlaceholderText = "Ví dụ: lô hết hạn 20/10, vỡ khi vận chuyển…" }, fullWidth: true);
        _preview = AddNote(null, minLines: 2);

        _reason.SelectedIndexChanged += (_, _) =>
        {
            if (_reason.SelectedValue is StockAdjustmentReason reason)
            {
                _draft.SetReason(reason);
                SyncFromDraft(keepTarget: false);
            }
        };
        _direction.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncing && _draft.CanChooseDirection)
            {
                _draft.SetIncrease(_direction.SelectedIndex == 1);
                SyncFromDraft(keepTarget: false);
            }
        };
        _quantity.ValueChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _draft.SetQuantity(_quantity.ValueOrZero);
                SyncFromDraft(keepTarget: false);
            }
        };
        _target.ValueChanged += (_, _) =>
        {
            if (_syncing || _target.Value is not { } target)
            {
                return;
            }

            if (_draft.SetTargetBalance(target))
            {
                _target.ClearValidation();
                SyncFromDraft(keepTarget: true);
            }
            else
            {
                _target.SetWarning($"Lý do \"{_draft.Reason.DisplayName()}\" không thể đưa tồn về {Qty(target)}.");
            }
        };
        _note.InnerTextBox.TextChanged += (_, _) =>
        {
            _draft.SetNote(_note.Text);
            Render();
        };

        SyncFromDraft(keepTarget: false);
    }

    public StockAdjustmentResult? Adjusted { get; private set; }

    protected override bool CanSave => _draft.CanSave;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _quantity.Select(); // reason defaults to "Hư hỏng": usually only the quantity is typed
    }

    private static string Describe(StockAdjustmentReason reason) => reason.Direction() switch
    {
        < 0 => $"{reason.DisplayName()} (giảm)",
        > 0 => $"{reason.DisplayName()} (tăng)",
        _ => $"{reason.DisplayName()} (tăng hoặc giảm)",
    };

    private static string Qty(decimal value) => StockCountSheet.FormatQty(value);

    private void SyncFromDraft(bool keepTarget)
    {
        _syncing = true;
        try
        {
            _direction.Enabled = _draft.CanChooseDirection;
            _direction.SelectedIndex = _draft.Increase ? 1 : 0;
            if (_quantity.ValueOrZero != _draft.Quantity)
            {
                _quantity.Value = _draft.Quantity;
            }

            if (!keepTarget)
            {
                _target.Value = _draft.Quantity > 0 ? Math.Max(0, _draft.BalanceAfter) : null;
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
        var p = _draft.Product;
        _note.PlaceholderText = _draft.NoteRequired ? "Bắt buộc với lý do khác" : "Ví dụ: lô hết hạn 20/10, vỡ khi vận chuyển…";
        _direction.Accent = _draft.Increase ? SegmentAccent.Primary : SegmentAccent.Danger;
        if (_draft.Quantity <= 0)
        {
            _preview.Set("Nhập số lượng cần điều chỉnh hoặc số tồn thực tế đếm được.", NoteTone.Muted);
        }
        else if (_draft.Blocker is { } blocker)
        {
            _preview.Set(blocker, NoteTone.Danger);
        }
        else
        {
            var sign = _draft.Increase ? "+" : "−";
            var low = _draft.WillBeLowStock ? "\n⚠ Sau điều chỉnh sản phẩm sẽ dưới định mức tồn kho." : string.Empty;
            _preview.Set(
                $"Tồn: {Qty(p.StockQuantity)} → {Qty(_draft.BalanceAfter)} {p.Unit} ({sign}{Qty(_draft.Quantity)})\n"
                + $"Giá trị điều chỉnh: {Vnd.Format(_draft.CostValue)}{low}",
                _draft.WillBeLowStock ? NoteTone.Warning : _draft.Increase ? NoteTone.Success : NoteTone.Danger);
        }

        if (!IsSaving)
        {
            SaveButton.Enabled = _draft.CanSave;
        }

        SaveButton.Variant = _draft.Increase ? ButtonVariant.Primary : ButtonVariant.Danger;
    }

    protected override async Task<string?> SaveAsync()
    {
        _quantity.Commit();
        _draft.SetQuantity(_quantity.ValueOrZero);
        var request = _draft.BuildRequest();
        if (request.IsFailure)
        {
            return request.Error.Message;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IInventoryService>().AdjustAsync(request.Value);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        Adjusted = result.Value;
        return null;
    }

    private sealed record ReasonOption(StockAdjustmentReason Reason, string Name);
}
