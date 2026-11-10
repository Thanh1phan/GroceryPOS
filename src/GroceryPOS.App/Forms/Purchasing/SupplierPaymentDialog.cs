using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Purchasing;
using GroceryPOS.Domain.Sales;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>
/// Phiếu chi trả nợ nhà cung cấp: current debt, amount (pre-filled with the whole debt; "Trả hết" / "Trả một nửa"),
/// cash or bank transfer with a transaction reference, note, and a live "công nợ còn lại" preview.
/// State and rules live in the tested <see cref="SupplierPaymentDraft"/>.
/// </summary>
internal sealed class SupplierPaymentDialog : BaseEditDialog
{
    private static readonly PaymentMethod[] Methods = [PaymentMethod.Cash, PaymentMethod.BankTransfer];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupplierPaymentDraft _draft;
    private readonly ModernCurrencyBox _amount;
    private readonly ModernSegmentedControl _method;
    private readonly ModernTextBox _reference;
    private readonly ModernTextBox _note;
    private readonly FormNote _after;

    public SupplierPaymentDialog(IServiceScopeFactory scopeFactory, SupplierPayableDto supplier)
        : base($"Trả nợ nhà cung cấp – {supplier.Name}", contentWidth: 412)
    {
        _scopeFactory = scopeFactory;
        _draft = new SupplierPaymentDraft(supplier);
        SaveText = "Lập phiếu chi";

        AddNote($"{supplier.Code} – {supplier.Name}", role: FontRole.Subtitle, style: FontStyle.Bold);
        AddNote($"Công nợ hiện tại: {Vnd.Format(supplier.DebtBalance)}", NoteTone.Muted);
        _amount = AddField("Số tiền trả", new ModernCurrencyBox { Minimum = 0, Maximum = Math.Max(0, supplier.DebtBalance), Value = _draft.Amount });
        var chips = AddField(null, new FlowLayoutPanel { Height = 34, WrapContents = false }, fullWidth: true, track: false);
        chips.Controls.AddRange([
            Chip("Trả hết", () => _draft.PayAll()),
            Chip($"Trả một nửa ({Vnd.Format(_draft.HalfAmount)})", () => _draft.PayHalf()),
        ]);
        _method = AddField("Hình thức", new ModernSegmentedControl());
        _method.SetItems(["Tiền mặt", "Chuyển khoản"]);
        _reference = AddField("Chứng từ", new ModernTextBox { PlaceholderText = _draft.ReferencePlaceholder, MaxLength = 100 });
        _note = AddField("Ghi chú", new ModernTextBox { PlaceholderText = "Ví dụ: thanh toán HĐ tháng 10", MaxLength = 500 });
        _after = AddNote(_draft.AfterText, role: FontRole.Subtitle, style: FontStyle.Bold);

        _amount.ValueChanged += (_, _) =>
        {
            _draft.SetAmount(_amount.ValueOrZero);
            _amount.ClearValidation();
            RenderAfter();
        };
        _method.SelectedIndexChanged += (_, _) =>
        {
            _draft.SetMethod(Methods[Math.Max(0, _method.SelectedIndex)]);
            _reference.PlaceholderText = _draft.ReferencePlaceholder;
        };
        RenderAfter();
    }

    public SupplierPaymentDto? Payment { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _amount.SelectAll();
    }

    private ModernButton Chip(string text, Action apply)
    {
        var button = new ModernButton { Text = text, Variant = ButtonVariant.Secondary, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 0), TabStop = false };
        button.Click += (_, _) =>
        {
            apply();
            _amount.Value = _draft.Amount;
            _amount.Focus();
            _amount.SelectAll();
        };
        return button;
    }

    private void RenderAfter() =>
        _after.Set(_draft.AfterText, _draft.DebtAfter < 0 ? NoteTone.Danger : _draft.DebtAfter == 0 ? NoteTone.Success : NoteTone.Normal);

    protected override async Task<string?> SaveAsync()
    {
        _amount.Commit();
        _draft.SetAmount(_amount.ValueOrZero);
        _draft.SetReference(_reference.Text);
        _draft.SetNote(_note.Text);
        if (_draft.Blocker is { } blocker)
        {
            _amount.SetError(blocker);
            _amount.Focus();
            return string.Empty;
        }

        if (!Confirm(_draft.ConfirmText, "Trả nợ nhà cung cấp", "Lập phiếu chi", "Xem lại"))
        {
            return string.Empty;
        }

        var request = _draft.BuildRequest();
        if (request.IsFailure)
        {
            return request.Error.Message;
        }

        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>().PayAsync(request.Value);
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        Payment = result.Value;
        return null;
    }
}
