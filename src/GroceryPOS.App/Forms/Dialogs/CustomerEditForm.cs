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
/// Add/edit dialog for a (loyalty) customer. The phone number is how the cashier finds the member at the counter,
/// so it must be unique. Points and total spent are read-only: only sales, voids and returns change them.
/// </summary>
internal sealed class CustomerEditForm : BaseEditDialog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CustomerDto? _customer;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _address;

    public CustomerEditForm(IServiceScopeFactory scopeFactory, CustomerDto? customer)
        : base(customer is null ? "Thêm khách hàng" : $"Sửa khách hàng – {customer.Code}", columns: 2, contentWidth: 496)
    {
        _scopeFactory = scopeFactory;
        _customer = customer;

        _name = AddField("Tên khách hàng", new ModernTextBox { Text = customer?.Name ?? string.Empty, PlaceholderText = "Họ và tên" }, fullWidth: true);
        _phone = AddField("Số điện thoại", new ModernTextBox
        {
            Text = customer?.Phone ?? string.Empty,
            PlaceholderText = "09xx xxx xxx",
            IconGlyph = Glyphs.Phone,
            HelperText = "Dùng để tìm khách khi bán hàng",
        });
        _email = AddField("Email", new ModernTextBox { Text = customer?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        _address = AddField("Địa chỉ", new ModernTextBox { Text = customer?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" }, fullWidth: true);

        if (customer is not null)
        {
            AddField("Điểm tích lũy", new ModernNumericBox { Value = customer.LoyaltyPoints, ReadOnly = true, TabStop = false, SuffixText = "điểm" }, track: false);
            AddField("Tổng chi tiêu", new ModernCurrencyBox { Value = customer.TotalSpent, ReadOnly = true, TabStop = false }, track: false);
        }
    }

    private static string? Optional(ModernTextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    protected override async Task<string?> SaveAsync()
    {
        var request = new SaveCustomerRequest(_name.Text, Optional(_phone), Optional(_email), Optional(_address));

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveCustomerRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid.ErrorMessageOrNull();
        }

        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var result = _customer is null
            ? await customers.CreateAsync(request)
            : await customers.UpdateAsync(_customer.Id, request);
        return result.ErrorMessageOrNull();
    }
}
