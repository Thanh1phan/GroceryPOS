using FluentValidation;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a (loyalty) customer. The phone number is how the cashier finds the member at the counter,
/// so it must be unique. Points and total spent are read-only: only sales, voids and returns change them.
/// </summary>
internal sealed class CustomerEditForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomerDto? _customer;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _address;
    private readonly ModernButton _save;

    public CustomerEditForm(IServiceScopeFactory scopeFactory, CustomerDto? customer)
    {
        _scopeFactory = scopeFactory;
        _customer = customer;

        Text = customer is null ? "Thêm khách hàng" : $"Sửa khách hàng – {customer.Code}";
        ClientSize = new Size(560, customer is null ? 390 : 470);

        const int left = 32;
        const int right = 288;
        const int half = 240;
        const int full = 496;

        Label Caption(string text, int x, int y) => new() { Text = text, AutoSize = true, Location = new Point(x, y) };

        _name = new ModernTextBox { Location = new Point(left, 40), Width = full, Text = customer?.Name ?? string.Empty, PlaceholderText = "Họ và tên" };
        _phone = new ModernTextBox
        {
            Location = new Point(left, 120),
            Width = half,
            Text = customer?.Phone ?? string.Empty,
            PlaceholderText = "09xx xxx xxx",
            IconGlyph = Glyphs.Phone,
            HelperText = "Dùng để tìm khách khi bán hàng",
        };
        _email = new ModernTextBox { Location = new Point(right, 120), Width = half, Text = customer?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" };
        _address = new ModernTextBox { Location = new Point(left, 200), Width = full, Text = customer?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" };

        Controls.AddRange([
            Caption("Tên khách hàng", left, 16), _name,
            Caption("Số điện thoại", left, 96), _phone, Caption("Email", right, 96), _email,
            Caption("Địa chỉ", left, 176), _address,
        ]);

        var y = 256;
        if (customer is not null)
        {
            var points = new ModernNumericBox { Location = new Point(left, y + 24), Width = half, Value = customer.LoyaltyPoints, ReadOnly = true, SuffixText = "điểm" };
            var spent = new ModernCurrencyBox { Location = new Point(right, y + 24), Width = half, Value = customer.TotalSpent, ReadOnly = true };
            Controls.AddRange([Caption("Điểm tích lũy", left, y), points, Caption("Tổng chi tiêu", right, y), spent]);
            y += 80;
        }

        ErrorLabel.Location = new Point(left, y + 8);
        ErrorLabel.Size = new Size(300, 44);

        _save = new ModernButton
        {
            Text = "Lưu",
            Variant = ButtonVariant.Primary,
            Location = new Point(right + 80, y + 12),
            Size = new Size(160, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;
        Controls.Add(_save);
    }

    private static string? Optional(ModernTextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var request = new SaveCustomerRequest(_name.Text, Optional(_phone), Optional(_email), Optional(_address));

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveCustomerRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var result = _customer is null
            ? await customers.CreateAsync(request)
            : await customers.UpdateAsync(_customer.Id, request);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    });
}
