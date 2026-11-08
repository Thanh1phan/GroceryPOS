using FluentValidation;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Partners;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a supplier. The code is pre-filled with the next <c>NCC0001</c> code on add (it may be
/// overwritten) and is read-only on edit; the debt balance is shown but only changes through receipts, payments and returns.
/// </summary>
internal sealed class SupplierEditForm : BaseEditDialog
{
    private const string CodeKey = "code";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupplierDto? _supplier;
    private readonly ModernTextBox _code;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _contactPerson;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _taxCode;
    private readonly ModernTextBox _address;

    public SupplierEditForm(IServiceScopeFactory scopeFactory, SupplierDto? supplier)
        : base(supplier is null ? "Thêm nhà cung cấp" : "Sửa nhà cung cấp", columns: 2, contentWidth: 496)
    {
        _scopeFactory = scopeFactory;
        _supplier = supplier;

        _code = AddField("Mã nhà cung cấp", new ModernTextBox
        {
            Text = supplier?.Code ?? string.Empty,
            ReadOnly = supplier is not null,
            PlaceholderText = "Tự động nếu để trống",
        }, trackAs: CodeKey);
        _taxCode = AddField("Mã số thuế", new ModernTextBox { Text = supplier?.TaxCode ?? string.Empty, PlaceholderText = "0123456789" });
        _name = AddField("Tên nhà cung cấp", new ModernTextBox { Text = supplier?.Name ?? string.Empty, PlaceholderText = "Công ty / đại lý" }, fullWidth: true);
        _contactPerson = AddField("Người liên hệ", new ModernTextBox { Text = supplier?.ContactPerson ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        _phone = AddField("Số điện thoại", new ModernTextBox { Text = supplier?.Phone ?? string.Empty, PlaceholderText = "09xx xxx xxx", IconGlyph = Glyphs.Phone });
        _email = AddField("Email", new ModernTextBox { Text = supplier?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        AddField("Công nợ hiện tại", new ModernCurrencyBox { Value = supplier?.DebtBalance ?? 0, ReadOnly = true, TabStop = false }, track: false);
        _address = AddField("Địa chỉ", new ModernTextBox { Text = supplier?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" }, fullWidth: true);

        if (supplier is null)
        {
            Load += async (_, _) => await SuggestCodeAsync();
        }
    }

    private async Task SuggestCodeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierService>().SuggestCodeAsync();
        if (IsAlive && result.IsSuccess && string.IsNullOrEmpty(_code.Text))
        {
            _code.Text = result.Value;
            Changes.AcceptChange(CodeKey); // a suggested code is not an edit by the user
        }
    }

    private static string? Optional(ModernTextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    protected override async Task<string?> SaveAsync()
    {
        var request = new SaveSupplierRequest(
            Optional(_code), _name.Text, Optional(_contactPerson), Optional(_phone), Optional(_email), Optional(_address), Optional(_taxCode));

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveSupplierRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid.ErrorMessageOrNull();
        }

        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierService>();
        var result = _supplier is null
            ? await suppliers.CreateAsync(request)
            : await suppliers.UpdateAsync(_supplier.Id, request);
        return result.ErrorMessageOrNull();
    }
}
