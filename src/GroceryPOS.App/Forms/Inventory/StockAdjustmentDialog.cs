using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Inventory;

/// <summary>
/// Điều chỉnh tồn kho for one product: reason (fixing the direction where it has one), quantity or the actual
/// stock on the shelf, note, and a live preview of stock and value after. State lives in <see cref="StockAdjustmentDraft"/>.
/// </summary>
internal sealed class StockAdjustmentDialog : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockAdjustmentDraft _draft;
    private readonly ModernComboBox _reason;
    private readonly RadioButton _decrease;
    private readonly RadioButton _increase;
    private readonly ModernNumericBox _quantity;
    private readonly ModernNumericBox _target;
    private readonly ModernTextBox _note;
    private readonly Label _preview;
    private readonly ModernButton _save;
    private bool _syncing;

    public StockAdjustmentDialog(IServiceScopeFactory scopeFactory, StockItem product)
    {
        _scopeFactory = scopeFactory;
        _draft = new StockAdjustmentDraft(product);
        Text = "Điều chỉnh tồn kho";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 540);

        const int left = 24;
        const int width = 432;

        var title = new Label
        {
            Text = $"{product.ProductCode} – {product.ProductName}",
            AutoSize = false,
            Size = new Size(width, 24),
            Location = new Point(left, 16),
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            AutoEllipsis = true,
        };
        var current = new Label
        {
            Text = $"Tồn hiện tại: {Qty(product.StockQuantity)} {product.Unit} · định mức {Qty(product.MinStockLevel)} · giá vốn {Vnd.Format(product.CostPrice)}",
            AutoSize = true,
            Location = new Point(left, 44),
        };

        var reasonCaption = new Label { Text = "Lý do", AutoSize = true, Location = new Point(left, 76) };
        _reason = new ModernComboBox
        {
            Location = new Point(left, 98),
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            DisplayMember = nameof(ReasonOption.Name),
            ValueMember = nameof(ReasonOption.Reason),
            DataSource = StockAdjustmentDraft.Reasons.Select(r => new ReasonOption(r, Describe(r))).ToList(),
        };

        _decrease = new RadioButton { Text = "Giảm tồn (xuất bớt)", AutoSize = true, Location = new Point(left, 150) };
        _increase = new RadioButton { Text = "Tăng tồn (nhập thêm)", AutoSize = true, Location = new Point(left + 220, 150) };

        var quantityCaption = new Label { Text = $"Số lượng điều chỉnh ({product.Unit})", AutoSize = true, Location = new Point(left, 184) };
        _quantity = new ModernNumericBox { Location = new Point(left, 206), Width = 200, DecimalPlaces = 3, Minimum = 0, Maximum = 100_000, ReserveMessageSpace = false };
        var targetCaption = new Label { Text = "hoặc tồn thực tế sau điều chỉnh", AutoSize = true, Location = new Point(left + 232, 184) };
        _target = new ModernNumericBox { Location = new Point(left + 232, 206), Width = 200, DecimalPlaces = 3, Minimum = 0, Maximum = 1_000_000, AllowEmpty = true, ReserveMessageSpace = false };

        var noteCaption = new Label { Text = "Ghi chú", AutoSize = true, Location = new Point(left, 262) };
        _note = new ModernTextBox { Location = new Point(left, 284), Width = width, MaxLength = 500, PlaceholderText = "Ví dụ: lô hết hạn 20/10, vỡ khi vận chuyển…" };

        _preview = new Label { AutoSize = false, Location = new Point(left, 340), Size = new Size(width, 72) };

        ErrorLabel.Location = new Point(left, 416);
        ErrorLabel.Size = new Size(width, 44);

        _save = new ModernButton { Text = "Lưu điều chỉnh", Variant = ButtonVariant.Primary, Location = new Point(left + width - 250, 476), Size = new Size(140, 40) };
        _save.Click += async (_, _) => await SaveAsync();
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Ghost,
            Location = new Point(left + width - 100, 476),
            Size = new Size(100, 40),
            DialogResult = DialogResult.Cancel,
        };

        _reason.SelectedIndexChanged += (_, _) =>
        {
            if (_reason.SelectedValue is StockAdjustmentReason reason)
            {
                _draft.SetReason(reason);
                SyncFromDraft(keepTarget: false);
            }
        };
        _decrease.CheckedChanged += (_, _) => OnDirectionChanged();
        _increase.CheckedChanged += (_, _) => OnDirectionChanged();
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

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.AddRange([title, current, reasonCaption, _reason, _decrease, _increase, quantityCaption, _quantity, targetCaption, _target, noteCaption, _note, _preview, _save, cancel]);
        SyncFromDraft(keepTarget: false);
        Shown += (_, _) => _quantity.Focus();
    }

    public StockAdjustmentResult? Adjusted { get; private set; }

    private static string Describe(StockAdjustmentReason reason) => reason.Direction() switch
    {
        < 0 => $"{reason.DisplayName()} (giảm)",
        > 0 => $"{reason.DisplayName()} (tăng)",
        _ => $"{reason.DisplayName()} (tăng hoặc giảm)",
    };

    private static string Qty(decimal value) => StockCountSheet.FormatQty(value);

    private void OnDirectionChanged()
    {
        if (_syncing || !_draft.CanChooseDirection)
        {
            return;
        }

        _draft.SetIncrease(_increase.Checked);
        SyncFromDraft(keepTarget: false);
    }

    private void SyncFromDraft(bool keepTarget)
    {
        _syncing = true;
        try
        {
            _decrease.Enabled = _increase.Enabled = _draft.CanChooseDirection;
            _increase.Checked = _draft.Increase;
            _decrease.Checked = !_draft.Increase;
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
        if (_draft.Quantity <= 0)
        {
            _preview.Text = "Nhập số lượng cần điều chỉnh hoặc số tồn thực tế đếm được.";
            _preview.ForeColor = SystemColors.GrayText;
        }
        else
        {
            var sign = _draft.Increase ? "+" : "−";
            _preview.Text =
                $"Tồn: {Qty(p.StockQuantity)} → {Qty(_draft.BalanceAfter)} {p.Unit} ({sign}{Qty(_draft.Quantity)})\n"
                + $"Giá trị điều chỉnh: {Vnd.Format(_draft.CostValue)}"
                + (_draft.WillBeLowStock && _draft.BalanceAfter >= 0 ? "\n⚠ Sau điều chỉnh sản phẩm sẽ dưới định mức tồn kho." : string.Empty);
            _preview.ForeColor = _draft.Increase ? Color.SeaGreen : Color.Firebrick;
        }

        _save.Enabled = _draft.CanSave;
        _save.Variant = _draft.Increase ? ButtonVariant.Primary : ButtonVariant.Danger;
        if (_draft.Blocker is { } blocker && _draft.Quantity > 0)
        {
            ErrorLabel.Text = blocker;
            ErrorLabel.Visible = true;
        }
        else
        {
            ErrorLabel.Visible = false;
        }
    }

    private async Task SaveAsync()
    {
        _quantity.Commit();
        _draft.SetQuantity(_quantity.ValueOrZero);
        var request = _draft.BuildRequest();
        if (request.IsFailure)
        {
            ErrorLabel.Text = request.Error.Message;
            ErrorLabel.Visible = true;
            return;
        }

        await RunSaveAsync(_save, async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IInventoryService>().AdjustAsync(request.Value);
            if (result.IsSuccess)
            {
                Adjusted = result.Value;
            }

            return (Result)result;
        });
    }

    private sealed record ReasonOption(StockAdjustmentReason Reason, string Name);
}
