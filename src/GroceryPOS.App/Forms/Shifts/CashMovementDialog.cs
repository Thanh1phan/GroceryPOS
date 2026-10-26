using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Thu / chi tiền mặt ngoài bán hàng on the cashier's open shift: amount, reason (with one-click common reasons)
/// and the cash currently in the drawer, so a pay-out larger than the drawer is caught before saving.
/// </summary>
internal sealed class CashMovementDialog : ModernDialogForm
{
    private static readonly string[] PayInReasons = ["Đổi tiền lẻ", "Bổ sung tiền thối", "Chủ cửa hàng nộp thêm"];
    private static readonly string[] PayOutReasons = ["Mua đá / nước", "Trả tiền giao hàng", "Rút tiền nộp chủ", "Chi vặt"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly decimal _cashInDrawer;
    private readonly RadioButton _payIn;
    private readonly RadioButton _payOut;
    private readonly ModernCurrencyBox _amount;
    private readonly ModernTextBox _reason;
    private readonly FlowLayoutPanel _quickReasons;
    private readonly ModernButton _save;

    public CashMovementDialog(IServiceScopeFactory scopeFactory, ShiftSummaryDto shift, CashMovementType type)
    {
        _scopeFactory = scopeFactory;
        _cashInDrawer = shift.ExpectedCash;
        Text = $"Thu / chi tiền mặt – ca {shift.Code}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 400);

        const int left = 24;
        const int width = 392;

        _payIn = new RadioButton { Text = "Nộp tiền vào két", AutoSize = true, Location = new Point(left, 16), Checked = type == CashMovementType.PayIn };
        _payOut = new RadioButton { Text = "Chi tiền từ két", AutoSize = true, Location = new Point(left + 180, 16), Checked = type == CashMovementType.PayOut };

        var drawer = new Label
        {
            Text = $"Tiền mặt trong két hiện tại: {Vnd.Format(_cashInDrawer)}",
            AutoSize = true,
            Location = new Point(left, 48),
        };

        var amountCaption = new Label { Text = "Số tiền", AutoSize = true, Location = new Point(left, 80) };
        _amount = new ModernCurrencyBox { Location = new Point(left, 102), Width = width, Minimum = 0 };

        var reasonCaption = new Label { Text = "Lý do", AutoSize = true, Location = new Point(left, 176) };
        _reason = new ModernTextBox { Location = new Point(left, 198), Width = width, PlaceholderText = "Bắt buộc", MaxLength = 200 };
        _quickReasons = new FlowLayoutPanel { Location = new Point(left, 252), Size = new Size(width, 72), WrapContents = true };

        ErrorLabel.Location = new Point(left, 326);
        ErrorLabel.Size = new Size(width - 180, 60);

        _save = new ModernButton { Text = "Lưu", Variant = ButtonVariant.Primary, Location = new Point(left + width - 220, 344), Size = new Size(110, 40) };
        _save.Click += async (_, _) => await SaveAsync();
        var cancel = new ModernButton
        {
            Text = "Hủy",
            Variant = ButtonVariant.Ghost,
            Location = new Point(left + width - 100, 344),
            Size = new Size(100, 40),
            DialogResult = DialogResult.Cancel,
        };

        _payIn.CheckedChanged += (_, _) => RenderReasons();
        RenderReasons();

        AcceptButton = _save;
        CancelButton = cancel;
        Controls.AddRange([_payIn, _payOut, drawer, amountCaption, _amount, reasonCaption, _reason, _quickReasons, _save, cancel]);
        Shown += (_, _) => _amount.Focus();
    }

    public ShiftSummaryDto? Updated { get; private set; }

    private CashMovementType Type => _payOut.Checked ? CashMovementType.PayOut : CashMovementType.PayIn;

    private void RenderReasons()
    {
        _quickReasons.Controls.Clear();
        foreach (var reason in Type == CashMovementType.PayIn ? PayInReasons : PayOutReasons)
        {
            var button = new ModernButton { Text = reason, Variant = ButtonVariant.Secondary, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 6) };
            button.Click += (_, _) =>
            {
                _reason.Text = reason;
                _reason.Focus();
            };
            _quickReasons.Controls.Add(button);
        }

        _save.Text = Type == CashMovementType.PayIn ? "Nộp vào két" : "Chi tiền";
        _save.Variant = Type == CashMovementType.PayIn ? ButtonVariant.Primary : ButtonVariant.Danger;
    }

    private async Task SaveAsync()
    {
        _amount.Commit();
        _amount.ClearValidation();
        _reason.ClearValidation();
        if (_amount.ValueOrZero <= 0)
        {
            _amount.SetError("Số tiền phải lớn hơn 0.");
            _amount.Focus();
            return;
        }

        if (Type == CashMovementType.PayOut && _amount.ValueOrZero > _cashInDrawer)
        {
            _amount.SetError($"Trong két chỉ còn {Vnd.Format(Math.Max(0, _cashInDrawer))}.");
            _amount.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_reason.Text))
        {
            _reason.SetError("Vui lòng nhập lý do.");
            _reason.Focus();
            return;
        }

        await RunSaveAsync(_save, async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IShiftService>()
                .RecordCashMovementAsync(new CashMovementRequest(Type, _amount.ValueOrZero, _reason.Text.Trim()));
            if (result.IsSuccess)
            {
                Updated = result.Value;
            }

            return (Result)result;
        });
    }
}
