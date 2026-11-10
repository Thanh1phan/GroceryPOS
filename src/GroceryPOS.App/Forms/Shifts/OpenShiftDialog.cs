using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Shifts;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Mở ca: the opening float is pre-filled with the cash the previous shift left in the drawer, so the cashier
/// only confirms (or corrects) what they counted; a different amount is flagged in amber with the difference.
/// On success <see cref="Opened"/> holds the new shift.
/// </summary>
internal sealed class OpenShiftDialog : BaseEditDialog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly decimal _suggestedFloat;
    private readonly ModernCurrencyBox _float;
    private readonly ModernTextBox _note;

    public OpenShiftDialog(IServiceScopeFactory scopeFactory, decimal suggestedFloat, string? cashierName)
        : base("Mở ca bán hàng", contentWidth: 376)
    {
        _scopeFactory = scopeFactory;
        _suggestedFloat = suggestedFloat;
        SaveText = "Mở ca";
        CancelDialogButton.Text = "Để sau";
        ConfirmDiscard = false; // "Để sau" only postpones: nothing typed is worth a prompt

        AddNote($"Thu ngân: {cashierName ?? "—"}", NoteTone.Normal, style: FontStyle.Bold);
        AddNote("Đếm tiền trong két rồi xác nhận số tiền đầu ca.", NoteTone.Muted);
        _float = AddField("Tiền đầu ca", new ModernCurrencyBox { Value = suggestedFloat });
        _note = AddField("Ghi chú", new ModernTextBox { PlaceholderText = "Không bắt buộc", MaxLength = 500 });

        _float.ValueChanged += (_, _) => ShowFloatHint();
        ShowFloatHint();
    }

    public ShiftSummaryDto? Opened { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _float.SelectAll();
    }

    /// <summary>Helper under the float: what the previous shift left, or the difference in amber when it does not match.</summary>
    private void ShowFloatHint()
    {
        var difference = _float.ValueOrZero - _suggestedFloat;
        if (_suggestedFloat > 0 && difference != 0)
        {
            var sign = difference > 0 ? "+" : "−";
            _float.SetWarning($"Ca trước để lại {Vnd.Format(_suggestedFloat)} – chênh {sign}{Vnd.Format(Math.Abs(difference))}");
            return;
        }

        _float.ClearValidation();
        _float.HelperText = _suggestedFloat > 0 ? $"Ca trước để lại két {Vnd.Format(_suggestedFloat)}" : "Ca trước không để lại tiền trong két";
    }

    protected override async Task<string?> SaveAsync()
    {
        _float.Commit();
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>()
            .OpenAsync(new OpenShiftRequest(_float.ValueOrZero, string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        Opened = result.Value;
        return null;
    }
}
