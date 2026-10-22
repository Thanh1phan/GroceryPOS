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
/// Add/edit dialog for a supplier. The code is pre-filled with the next <c>NCC0001</c> code on add (it may be
/// overwritten) and is read-only on edit; the debt balance is shown but only changes through receipts, payments and returns.
/// </summary>
internal sealed class SupplierEditForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupplierDto? _supplier;
    private readonly ModernTextBox _code;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _contactPerson;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _taxCode;
    private readonly ModernTextBox _address;
    private readonly ModernButton _save;

    public SupplierEditForm(IServiceScopeFactory scopeFactory, SupplierDto? supplier)
    {
        _scopeFactory = scopeFactory;
        _supplier = supplier;

        Text = supplier is null ? "Thêm nhà cung cấp" : "Sửa nhà cung cấp";
        ClientSize = new Size(560, 470);

        const int left = 32;
        const int right = 288;
        const int half = 240;
        const int full = 496;

        Label Caption(string text, int x, int y) => new() { Text = text, AutoSize = true, Location = new Point(x, y) };

        _code = new ModernTextBox
        {
            Location = new Point(left, 40),
            Width = half,
            Text = supplier?.Code ?? string.Empty,
            ReadOnly = supplier is not null,
            PlaceholderText = "Tự động nếu để trống",
        };
        _taxCode = new ModernTextBox { Location = new Point(right, 40), Width = half, Text = supplier?.TaxCode ?? string.Empty, PlaceholderText = "0123456789" };

        _name = new ModernTextBox { Location = new Point(left, 120), Width = full, Text = supplier?.Name ?? string.Empty, PlaceholderText = "Công ty / đại lý" };

        _contactPerson = new ModernTextBox { Location = new Point(left, 200), Width = half, Text = supplier?.ContactPerson ?? string.Empty, PlaceholderText = "Không bắt buộc" };
        _phone = new ModernTextBox { Location = new Point(right, 200), Width = half, Text = supplier?.Phone ?? string.Empty, PlaceholderText = "09xx xxx xxx", IconGlyph = Glyphs.Phone };

        _email = new ModernTextBox { Location = new Point(left, 280), Width = half, Text = supplier?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" };
        var debt = new ModernCurrencyBox { Location = new Point(right, 280), Width = half, Value = supplier?.DebtBalance ?? 0, ReadOnly = true };

        _address = new ModernTextBox { Location = new Point(left, 360), Width = full, Text = supplier?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" };

        ErrorLabel.Location = new Point(left, 410);
        ErrorLabel.Size = new Size(300, 44);

        _save = new ModernButton
        {
            Text = "Lưu",
            Variant = ButtonVariant.Primary,
            Location = new Point(right + 80, 414),
            Size = new Size(160, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();
        AcceptButton = _save;

        Controls.AddRange([
            Caption("Mã nhà cung cấp", left, 16), _code, Caption("Mã số thuế", right, 16), _taxCode,
            Caption("Tên nhà cung cấp", left, 96), _name,
            Caption("Người liên hệ", left, 176), _contactPerson, Caption("Số điện thoại", right, 176), _phone,
            Caption("Email", left, 256), _email, Caption("Công nợ hiện tại", right, 256), debt,
            Caption("Địa chỉ", left, 336), _address,
            _save,
        ]);

        if (supplier is null)
        {
            Load += async (_, _) => await SuggestCodeAsync();
        }
    }

    private async Task SuggestCodeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierService>().SuggestCodeAsync();
        if (!IsDisposed && result.IsSuccess && string.IsNullOrEmpty(_code.Text))
        {
            _code.Text = result.Value;
        }
    }

    private static string? Optional(ModernTextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var request = new SaveSupplierRequest(
            Optional(_code), _name.Text, Optional(_contactPerson), Optional(_phone), Optional(_email), Optional(_address), Optional(_taxCode));

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveSupplierRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierService>();
        var result = _supplier is null
            ? await suppliers.CreateAsync(request)
            : await suppliers.UpdateAsync(_supplier.Id, request);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    });
}
