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

/// <summary>Supplier list (nhà cung cấp) with add/edit and stop/resume trading. Suppliers are never deleted: receipts and payments refer to them.</summary>
internal sealed class SupplierListScreen : BaseListView<SupplierDto>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SupplierListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        var canManage = currentUser.HasPermission(Permissions.SuppliersManage);

        Grid.EmptyText = "Chưa có nhà cung cấp nào.";
        Grid.AddTextColumn(nameof(SupplierDto.Code), "Mã NCC", width: 100);
        Grid.AddTextColumn(nameof(SupplierDto.Name), "Tên nhà cung cấp");
        Grid.AddTextColumn(nameof(SupplierDto.ContactPerson), "Người liên hệ", width: 150);
        Grid.AddTextColumn(nameof(SupplierDto.Phone), "Điện thoại", width: 120);
        Grid.AddTextColumn(nameof(SupplierDto.TaxCode), "Mã số thuế", width: 130);
        Grid.AddMoneyColumn(nameof(SupplierDto.DebtBalance), "Công nợ", width: 140);
        Grid.AddCheckColumn(nameof(SupplierDto.IsActive), "Đang giao dịch", width: 110);

        AddCommand(new ListCommand<SupplierDto>("Thêm nhà cung cấp") { Allowed = canManage }, () => Open(null), ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<SupplierDto>("Sửa") { Allowed = canManage }, supplier => Open(supplier), iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<SupplierDto>("Ngừng giao dịch")
            {
                Allowed = canManage,
                TextFor = s => s.IsActive ? "Ngừng giao dịch" : "Giao dịch lại",
            },
            ToggleActiveAsync);
    }

    protected override object? KeyOf(SupplierDto item) => item.Id;

    protected override async Task<IReadOnlyList<SupplierDto>?> LoadItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISupplierService>().ListAsync(cancellationToken: cancellationToken);
        if (result.IsFailure)
        {
            ToastError(result.Error.Message, "Nhà cung cấp");
            return null;
        }

        return result.Value;
    }

    private void Open(SupplierDto? supplier)
    {
        using var dialog = new SupplierEditForm(_scopeFactory, supplier);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ToastSuccess(supplier is null ? "Đã thêm nhà cung cấp mới." : $"Đã lưu nhà cung cấp {supplier.Name}.");
            RequestRefresh();
        }
    }

    private async Task ToggleActiveAsync(SupplierDto supplier)
    {
        if (supplier.IsActive
            && !Confirm($"Ngừng giao dịch với \"{supplier.Name}\"?\nNhà cung cấp sẽ không còn trong danh sách chọn khi nhập hàng.",
                "Nhà cung cấp", "Ngừng giao dịch", "Hủy"))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var suppliers = scope.ServiceProvider.GetRequiredService<ISupplierService>();
        var result = await (supplier.IsActive ? suppliers.DeactivateAsync(supplier.Id) : suppliers.ActivateAsync(supplier.Id));
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, "Nhà cung cấp");
            return;
        }

        await RefreshDataAsync();
    }
}
