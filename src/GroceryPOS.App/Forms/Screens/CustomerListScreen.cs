using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Partners;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Customer (loyalty member) list: add/edit, loyalty history, deactivate/reactivate. Search covers code, name, phone and email.</summary>
internal sealed class CustomerListScreen : BaseListView<CustomerDto>
{
    private const string Caption = "Khách hàng";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canManage;

    public CustomerListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canManage = currentUser.HasPermission(Permissions.CustomersManage);

        Grid.EmptyText = "Chưa có khách hàng nào.";
        Grid.AddTextColumn(nameof(CustomerDto.Code), "Mã KH", width: 100);
        Grid.AddTextColumn(nameof(CustomerDto.Name), "Tên khách hàng");
        Grid.AddTextColumn(nameof(CustomerDto.Phone), "Điện thoại", width: 120);
        Grid.AddTextColumn(nameof(CustomerDto.Email), "Email", width: 180);
        Grid.AddNumberColumn(nameof(CustomerDto.LoyaltyPoints), "Điểm", width: 90);
        Grid.AddMoneyColumn(nameof(CustomerDto.TotalSpent), "Tổng chi tiêu", width: 140);
        Grid.AddCheckColumn(nameof(CustomerDto.IsActive), "Hoạt động");

        AddCommand(new ListCommand<CustomerDto>("Thêm khách hàng") { Allowed = _canManage }, () => Open(null), ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<CustomerDto>("Sửa") { Allowed = _canManage }, customer => Open(customer), iconGlyph: Glyphs.Edit);
        AddCommand(new ListCommand<CustomerDto>("Lịch sử điểm"), ShowHistory, iconGlyph: Glyphs.History);
        AddCommand(
            new ListCommand<CustomerDto>("Ngừng hoạt động")
            {
                Allowed = _canManage,
                TextFor = c => c.IsActive ? "Ngừng hoạt động" : "Kích hoạt lại",
            },
            ToggleActiveAsync);
    }

    protected override object? KeyOf(CustomerDto item) => item.Id;

    protected override async Task<IReadOnlyList<CustomerDto>?> LoadItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICustomerService>().ListAsync(cancellationToken: cancellationToken);
        if (result.IsFailure)
        {
            ToastError(result.Error.Message, Caption);
            return null;
        }

        return result.Value;
    }

    /// <summary>Enter/double-click: edit for managers, otherwise the (read-only) loyalty history.</summary>
    protected override void OnItemActivated(CustomerDto item)
    {
        if (_canManage)
        {
            Open(item);
        }
        else
        {
            ShowHistory(item);
        }
    }

    private void Open(CustomerDto? customer)
    {
        using var dialog = new CustomerEditForm(_scopeFactory, customer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ToastSuccess(customer is null ? "Đã thêm khách hàng mới." : $"Đã lưu khách hàng {customer.Name}.");
            RequestRefresh();
        }
    }

    private void ShowHistory(CustomerDto customer)
    {
        using var dialog = new CustomerLoyaltyHistoryForm(_scopeFactory, customer);
        dialog.ShowDialog(this);
    }

    private async Task ToggleActiveAsync(CustomerDto customer)
    {
        if (customer.IsActive
            && !Confirm($"Ngừng hoạt động khách hàng \"{customer.Name}\"?\nKhách sẽ không còn được tìm thấy khi bán hàng; điểm tích lũy được giữ nguyên.",
                Caption, "Ngừng hoạt động", "Hủy"))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var result = await (customer.IsActive ? customers.DeactivateAsync(customer.Id) : customers.ActivateAsync(customer.Id));
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, Caption);
            return;
        }

        ToastSuccess(customer.IsActive ? $"Đã ngừng hoạt động khách hàng {customer.Name}." : $"Đã kích hoạt lại khách hàng {customer.Name}.");
        await RefreshDataAsync();
    }
}
