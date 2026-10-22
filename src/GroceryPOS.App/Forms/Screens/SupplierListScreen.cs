using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Partners;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Supplier list (nhà cung cấp) with add/edit and stop/resume trading. Suppliers are never deleted: receipts and payments refer to them.</summary>
internal sealed class SupplierListScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canManage;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _edit;
    private readonly ModernButton _toggleActive;

    public SupplierListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canManage = currentUser.HasPermission(Permissions.SuppliersManage);
        Dock = DockStyle.Fill;

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có nhà cung cấp nào." };
        _grid.AddTextColumn(nameof(SupplierDto.Code), "Mã NCC", width: 100);
        _grid.AddTextColumn(nameof(SupplierDto.Name), "Tên nhà cung cấp");
        _grid.AddTextColumn(nameof(SupplierDto.ContactPerson), "Người liên hệ", width: 150);
        _grid.AddTextColumn(nameof(SupplierDto.Phone), "Điện thoại", width: 120);
        _grid.AddTextColumn(nameof(SupplierDto.TaxCode), "Mã số thuế", width: 130);
        _grid.AddMoneyColumn(nameof(SupplierDto.DebtBalance), "Công nợ", width: 140);
        _grid.AddCheckColumn(nameof(SupplierDto.IsActive), "Đang giao dịch", width: 110);
        _grid.RowActivated += (_, e) => Edit((SupplierDto)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateToolbarState();

        var add = new ModernButton { Text = "Thêm nhà cung cấp", Variant = ButtonVariant.Primary, Enabled = _canManage };
        add.Click += (_, _) => Open(null);

        _edit = new ModernButton { Text = "Sửa", Variant = ButtonVariant.Secondary, Enabled = false };
        _edit.Click += (_, _) =>
        {
            if (_grid.GetSelected<SupplierDto>() is { } supplier)
            {
                Edit(supplier);
            }
        };

        _toggleActive = new ModernButton { Text = "Ngừng giao dịch", Variant = ButtonVariant.Secondary, Enabled = false };
        _toggleActive.Click += async (_, _) => await ToggleActiveAsync();

        _grid.ToolbarItems.Controls.AddRange([add, _edit, _toggleActive]);
        Controls.Add(_grid);

        Load += async (_, _) => await ReloadAsync();
    }

    private void UpdateToolbarState()
    {
        var selected = _grid.GetSelected<SupplierDto>();
        _edit.Enabled = _canManage && selected is not null;
        _toggleActive.Enabled = _canManage && selected is not null;
        _toggleActive.Text = selected is { IsActive: false } ? "Giao dịch lại" : "Ngừng giao dịch";
    }

    private void Edit(SupplierDto supplier)
    {
        if (_canManage)
        {
            Open(supplier);
        }
    }

    private void Open(SupplierDto? supplier)
    {
        using var dialog = new SupplierEditForm(_scopeFactory, supplier);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (_grid.GetSelected<SupplierDto>() is not { } supplier)
        {
            return;
        }

        if (supplier.IsActive
            && MessageBox.Show(this, $"Ngừng giao dịch với \"{supplier.Name}\"?\nNhà cung cấp sẽ không còn trong danh sách chọn khi nhập hàng.",
                "Nhà cung cấp", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierService>();
        var result = await (supplier.IsActive ? suppliers.DeactivateAsync(supplier.Id) : suppliers.ActivateAsync(supplier.Id));
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, "Nhà cung cấp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierService>().ListAsync();
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
