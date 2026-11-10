using GroceryPOS.Application.Shifts;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Thu / chi tiền mặt ngoài bán hàng on the cashier's open shift: direction, amount, reason (with one-click common
/// reasons) and a live "tiền trong két: trước → sau" line, so a pay-out larger than the drawer is caught before saving.
/// State and rules live in the tested <see cref="CashMovementDraft"/>.
/// </summary>
internal sealed class CashMovementDialog : BaseEditDialog
{
    private static readonly CashMovementType[] Types = [CashMovementType.PayIn, CashMovementType.PayOut];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CashMovementDraft _draft;
    private readonly ModernSegmentedControl _type;
    private readonly FormNote _drawer;
    private readonly ModernCurrencyBox _amount;
    private readonly ModernTextBox _reason;
    private readonly FlowLayoutPanel _quickReasons;

    public CashMovementDialog(IServiceScopeFactory scopeFactory, ShiftSummaryDto shift, CashMovementType type)
        : base($"Thu / chi tiền mặt – ca {shift.Code}", contentWidth: 392)
    {
        _scopeFactory = scopeFactory;
        _draft = new CashMovementDraft(shift.ExpectedCash, type);

        _type = AddField("Loại phiếu", new ModernSegmentedControl());
        _type.SetItems(["Nộp tiền vào két", "Chi tiền từ két"], Array.IndexOf(Types, type));
        _drawer = AddNote(_draft.Preview, NoteTone.Muted);
        _amount = AddField("Số tiền", new ModernCurrencyBox { Minimum = 0 });
        _reason = AddField("Lý do", new ModernTextBox { PlaceholderText = "Bắt buộc – chọn nhanh bên dưới hoặc tự nhập", MaxLength = 200 });
        _quickReasons = AddField(null, new FlowLayoutPanel { Height = 72, WrapContents = true }, fullWidth: true, track: false);

        _type.SelectedIndexChanged += (_, _) =>
        {
            _draft.SetType(Types[Math.Max(0, _type.SelectedIndex)]);
            _amount.ClearValidation();
            RenderReasons();
            Render();
        };
        _amount.ValueChanged += (_, _) =>
        {
            _draft.SetAmount(_amount.ValueOrZero);
            _amount.ClearValidation();
            Render();
        };
        _reason.InnerTextBox.TextChanged += (_, _) =>
        {
            _draft.SetReason(_reason.Text);
            _reason.ClearValidation();
        };

        RenderReasons();
        Render();
    }

    public ShiftSummaryDto? Updated { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _amount.Select(); // the direction was chosen by the button that opened the dialog
    }

    private void RenderReasons()
    {
        _quickReasons.SuspendLayout();
        foreach (Control old in _quickReasons.Controls.Cast<Control>().ToList())
        {
            old.Dispose();
        }

        foreach (var reason in _draft.QuickReasons)
        {
            var button = new ModernButton { Text = reason, Variant = ButtonVariant.Secondary, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 6), TabStop = false };
            button.Click += (_, _) =>
            {
                _reason.Text = reason;
                _reason.Focus();
            };
            _quickReasons.Controls.Add(button);
        }

        _quickReasons.ResumeLayout(true);
    }

    private void Render()
    {
        var overdrawn = _draft.IsPayOut && _draft.DrawerAfter < 0;
        _drawer.Set(_draft.Preview, overdrawn ? NoteTone.Danger : _draft.Amount > 0 ? NoteTone.Normal : NoteTone.Muted);
        _type.Accent = _draft.IsPayOut ? SegmentAccent.Danger : SegmentAccent.Primary;
        SaveText = _draft.SaveText;
        SaveButton.Variant = _draft.IsPayOut ? ButtonVariant.Danger : ButtonVariant.Primary;
    }

    protected override async Task<string?> SaveAsync()
    {
        _amount.Commit();
        _draft.SetAmount(_amount.ValueOrZero);
        _draft.SetReason(_reason.Text);
        if (_draft.AmountError is { } amountError)
        {
            _amount.SetError(amountError);
            _amount.Focus();
            return string.Empty;
        }

        if (_draft.ReasonError is { } reasonError)
        {
            _reason.SetError(reasonError);
            _reason.Focus();
            return string.Empty;
        }

        var request = _draft.BuildRequest();
        if (request.IsFailure)
        {
            return request.Error.Message;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>().RecordCashMovementAsync(request.Value);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        Updated = result.Value;
        return null;
    }
}
