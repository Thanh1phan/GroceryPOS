using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Partners;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Customer (loyalty member) list: add/edit, loyalty history, deactivate/reactivate. Search covers code, name, phone and email.</summary>
internal sealed class CustomerListScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canManage;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _edit;
    private readonly ModernButton _history;
    private readonly ModernButton _toggleActive;

    public CustomerListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canManage = currentUser.HasPermission(Permissions.CustomersManage);
        Dock = DockStyle.Fill;

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có khách hàng nào." };
        _grid.AddTextColumn(nameof(CustomerDto.Code), "Mã KH", width: 100);
        _grid.AddTextColumn(nameof(CustomerDto.Name), "Tên khách hàng");
        _grid.AddTextColumn(nameof(CustomerDto.Phone), "Điện thoại", width: 120);
        _grid.AddTextColumn(nameof(CustomerDto.Email), "Email", width: 180);
        _grid.AddNumberColumn(nameof(CustomerDto.LoyaltyPoints), "Điểm", width: 90);
        _grid.AddMoneyColumn(nameof(CustomerDto.TotalSpent), "Tổng chi tiêu", width: 140);
        _grid.AddCheckColumn(nameof(CustomerDto.IsActive), "Hoạt động");
        _grid.RowActivated += (_, e) => Activate((CustomerDto)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateToolbarState();

        var add = new ModernButton { Text = "Thêm khách hàng", Variant = ButtonVariant.Primary, Enabled = _canManage };
        add.Click += (_, _) => Open(null);

        _edit = new ModernButton { Text = "Sửa", Variant = ButtonVariant.Secondary, Enabled = false };
        _edit.Click += (_, _) =>
        {
            if (_grid.GetSelected<CustomerDto>() is { } customer)
            {
                Open(customer);
            }
        };

        _history = new ModernButton { Text = "Lịch sử điểm", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.History, Enabled = false };
        _history.Click += (_, _) =>
        {
            if (_grid.GetSelected<CustomerDto>() is { } customer)
            {
                ShowHistory(customer);
            }
        };

        _toggleActive = new ModernButton { Text = "Ngừng hoạt động", Variant = ButtonVariant.Secondary, Enabled = false };
        _toggleActive.Click += async (_, _) => await ToggleActiveAsync();

        _grid.ToolbarItems.Controls.AddRange([add, _edit, _history, _toggleActive]);
        Controls.Add(_grid);

        Load += async (_, _) => await ReloadAsync();
    }

    private void UpdateToolbarState()
    {
        var selected = _grid.GetSelected<CustomerDto>();
        _edit.Enabled = _canManage && selected is not null;
        _history.Enabled = selected is not null;
        _toggleActive.Enabled = _canManage && selected is not null;
        _toggleActive.Text = selected is { IsActive: false } ? "Kích hoạt lại" : "Ngừng hoạt động";
    }

    /// <summary>Enter/double-click: edit for managers, otherwise the (read-only) loyalty history.</summary>
    private void Activate(CustomerDto customer)
    {
        if (_canManage)
        {
            Open(customer);
        }
        else
        {
            ShowHistory(customer);
        }
    }

    private void Open(CustomerDto? customer)
    {
        using var dialog = new CustomerEditForm(_scopeFactory, customer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private void ShowHistory(CustomerDto customer)
    {
        using var dialog = new CustomerLoyaltyHistoryForm(_scopeFactory, customer);
        dialog.ShowDialog(this);
    }

    private async Task ToggleActiveAsync()
    {
        if (_grid.GetSelected<CustomerDto>() is not { } customer)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var result = await (customer.IsActive ? customers.DeactivateAsync(customer.Id) : customers.ActivateAsync(customer.Id));
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Khách hàng", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICustomerService>().ListAsync();
        if (IsDisposed)
        {
            return;
        }

        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value);
        }

        UpdateToolbarState();
    }
}
