using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Shifts;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Shifts;

/// <summary>
/// Mở ca: the opening float is pre-filled with the cash the previous shift left in the drawer, so the cashier
/// only confirms (or corrects) what they counted. On success <see cref="Opened"/> holds the new shift.
/// </summary>
internal sealed class OpenShiftDialog : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernCurrencyBox _float;
    private readonly ModernTextBox _note;
    private readonly ModernButton _open;

    public OpenShiftDialog(IServiceScopeFactory scopeFactory, decimal suggestedFloat, string? cashierName)
    {
        _scopeFactory = scopeFactory;
        Text = "Mở ca bán hàng";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 330);

        const int left = 24;
        const int width = 372;

        var intro = new Label
        {
            Text = $"Thu ngân: {cashierName ?? "—"}\nĐếm tiền trong két rồi xác nhận số tiền đầu ca.",
            Location = new Point(left, 16),
            Size = new Size(width, 40),
        };

        var floatCaption = new Label { Text = "Tiền đầu ca", AutoSize = true, Location = new Point(left, 64) };
        _float = new ModernCurrencyBox
        {
            Location = new Point(left, 86),
            Width = width,
            Value = suggestedFloat,
            HelperText = suggestedFloat > 0 ? $"Ca trước để lại két {Vnd.Format(suggestedFloat)}" : "Ca trước không để lại tiền trong két",
        };

        var noteCaption = new Label { Text = "Ghi chú", AutoSize = true, Location = new Point(left, 160) };
        _note = new ModernTextBox { Location = new Point(left, 182), Width = width, PlaceholderText = "Không bắt buộc", MaxLength = 500 };

        ErrorLabel.Location = new Point(left, 236);
        ErrorLabel.Size = new Size(width, 36);

        _open = new ModernButton { Text = "Mở ca (Enter)", Variant = ButtonVariant.Primary, Location = new Point(left + width - 260, 276), Size = new Size(150, 40) };
        _open.Click += async (_, _) => await OpenAsync();
        var cancel = new ModernButton
        {
            Text = "Để sau",
            Variant = ButtonVariant.Ghost,
            Location = new Point(left + width - 100, 276),
            Size = new Size(100, 40),
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = _open;
        CancelButton = cancel;
        Controls.AddRange([intro, floatCaption, _float, noteCaption, _note, _open, cancel]);
        Shown += (_, _) =>
        {
            _float.Focus();
            _float.SelectAll();
        };
    }

    public ShiftSummaryDto? Opened { get; private set; }

    private Task OpenAsync() => RunSaveAsync(_open, async () =>
    {
        _float.Commit();
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IShiftService>()
            .OpenAsync(new OpenShiftRequest(_float.ValueOrZero, string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        if (result.IsSuccess)
        {
            Opened = result.Value;
        }

        return (Result)result;
    });
}
