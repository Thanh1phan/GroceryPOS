using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Phiếu chi trả nợ nhà cung cấp: current debt, amount (pre-filled with the whole debt; "Trả hết" / "Trả một nửa"),
/// cash or bank transfer with a transaction reference, note, and a live "công nợ còn lại" preview.
/// </summary>
internal sealed class SupplierPaymentDialog : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupplierPayableDto _supplier;
    private readonly ModernCurrencyBox _amount;
    private readonly RadioButton _cash;
    private readonly RadioButton _transfer;
    private readonly ModernTextBox _reference;
    private readonly ModernTextBox _note;
    private readonly Label _after;
    private readonly ModernButton _save;

    public SupplierPaymentDialog(IServiceScopeFactory scopeFactory, SupplierPayableDto supplier)
    {
        _scopeFactory = scopeFactory;
        _supplier = supplier;
        Text = $"Trả nợ nhà cung cấp – {supplier.Name}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 520);

        const int left = 24;
        const int width = 412;

        var title = new Label
        {
            Text = $"{supplier.Code} – {supplier.Name}",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            AutoEllipsis = true,
            Location = new Point(left, 16),
            Size = new Size(width, 28),
        };
        var debt = new Label
        {
            Text = $"Công nợ hiện tại: {Vnd.Format(supplier.DebtBalance)}",
            AutoSize = true,
            Location = new Point(left, 48),
        };

        var amountCaption = new Label { Text = "Số tiền trả", AutoSize = true, Location = new Point(left, 80) };
        _amount = new ModernCurrencyBox { Location = new Point(left, 102), Width = width, Minimum = 0, Maximum = supplier.DebtBalance, Value = supplier.DebtBalance };
        _amount.ValueChanged += (_, _) => RenderAfter();
        var all = Chip("Trả hết", () => _amount.Value = supplier.DebtBalance);
        var half = Chip("Trả một nửa", () => _amount.Value = Math.Round(supplier.DebtBalance / 2 / 1_000, MidpointRounding.AwayFromZero) * 1_000);
        var chips = new FlowLayoutPanel { Location = new Point(left, 160), Size = new Size(width, 36), WrapContents = false };
        chips.Controls.AddRange([all, half]);

        _cash = new RadioButton { Text = "Tiền mặt", AutoSize = true, Location = new Point(left, 206), Checked = true };
        _transfer = new RadioButton { Text = "Chuyển khoản", AutoSize = true, Location = new Point(left + 140, 206) };

        var referenceCaption = new Label { Text = "Chứng từ", AutoSize = true, Location = new Point(left, 240) };
        _reference = new ModernTextBox { Location = new Point(left, 262), Width = width, PlaceholderText = "Số chứng từ (không bắt buộc)", MaxLength = 100 };
        _transfer.CheckedChanged += (_, _) => _reference.PlaceholderText = _transfer.Checked ? "Mã giao dịch / số UNC" : "Số chứng từ (không bắt buộc)";
        var noteCaption = new Label { Text = "Ghi chú", AutoSize = true, Location = new Point(left, 320) };
        _note = new ModernTextBox { Location = new Point(left, 342), Width = width, PlaceholderText = "Ví dụ: thanh toán HĐ tháng 10", MaxLength = 500 };

        _after = new Label { Location = new Point(left, 400), Size = new Size(width, 24), Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };

        ErrorLabel.Location = new Point(left, 428);
        ErrorLabel.Size = new Size(width - 230, 80);

        _save = new ModernButton { Text = "Lập phiếu chi", Variant = ButtonVariant.Primary, Location = new Point(left + width - 230, 464), Size = new Size(130, 40) };
        _save.Click += async (_, _) => await SaveAsync();
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Ghost,
            Location = new Point(left + width - 90, 464),
            Size = new Size(90, 40),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.AddRange([title, debt, amountCaption, _amount, chips, _cash, _transfer, referenceCaption, _reference, noteCaption, _note, _after, _save, cancel]);
        Shown += (_, _) =>
        {
            RenderAfter();
            _amount.Focus();
            _amount.SelectAll();
        };
    }

    public SupplierPaymentDto? Payment { get; private set; }

    private ModernButton Chip(string text, Action onClick)
    {
        var button = new ModernButton { Text = text, Variant = ButtonVariant.Secondary, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0) };
        button.Click += (_, _) =>
        {
            onClick();
            _amount.Focus();
        };
        return button;
    }

    private void RenderAfter()
    {
        var after = _supplier.DebtBalance - _amount.ValueOrZero;
        var colors = ThemeManager.Current.Colors;
        _after.Text = after <= 0 ? "Sau khi trả: hết nợ nhà cung cấp." : $"Công nợ còn lại: {Vnd.Format(after)}";
        _after.ForeColor = after < 0 ? colors.Danger : after == 0 ? colors.Success : colors.TextPrimary;
    }

    private async Task SaveAsync()
    {
        _amount.Commit();
        _amount.ClearValidation();
        var amount = _amount.ValueOrZero;
        if (amount <= 0)
        {
            _amount.SetError("Số tiền phải lớn hơn 0.");
            _amount.Focus();
            return;
        }

        if (amount > _supplier.DebtBalance)
        {
            _amount.SetError($"Không được vượt quá công nợ {Vnd.Format(_supplier.DebtBalance)}.");
            _amount.Focus();
            return;
        }

        var method = _transfer.Checked ? PaymentMethod.BankTransfer : PaymentMethod.Cash;
        var confirm = $"Lập phiếu chi {Vnd.Format(amount)} ({(method == PaymentMethod.Cash ? "tiền mặt" : "chuyển khoản")}) trả nợ {_supplier.Name}?";
        if (MessageBox.Show(this, confirm, "Trả nợ nhà cung cấp", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        await RunSaveAsync(_save, async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().PayAsync(new SupplierPaymentRequest(
                _supplier.SupplierId,
                amount,
                method,
                string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim(),
                string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
            if (result.IsSuccess)
            {
                Payment = result.Value;
            }

            return (Result)result;
        });
    }
}
