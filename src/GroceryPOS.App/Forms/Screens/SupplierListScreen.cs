using GroceryPOS.Application.Partners;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Partners;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Nhà cung cấp screen – passive MVP view; the logic lives in <see cref="SupplierListPresenter"/>.</summary>
internal sealed class SupplierListScreen : BaseListView<SupplierDto>, ISupplierListView
{
    private readonly IServiceProvider _services;
    private readonly SupplierListPresenter _presenter;

    public SupplierListScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<SupplierListPresenter>();
        _presenter.Attach(this);
        var canManage = _presenter.CanManage;

        Grid.EmptyText = "Chưa có nhà cung cấp nào.";
        Grid.AddTextColumn(nameof(SupplierDto.Code), "Mã NCC", width: 100);
        Grid.AddTextColumn(nameof(SupplierDto.Name), "Tên nhà cung cấp");
        Grid.AddTextColumn(nameof(SupplierDto.ContactPerson), "Người liên hệ", width: 150);
        Grid.AddTextColumn(nameof(SupplierDto.Phone), "Điện thoại", width: 120);
        Grid.AddTextColumn(nameof(SupplierDto.TaxCode), "Mã số thuế", width: 130);
        Grid.AddMoneyColumn(nameof(SupplierDto.DebtBalance), "Công nợ", width: 140);
        Grid.AddCheckColumn(nameof(SupplierDto.IsActive), "Đang giao dịch", width: 110);

        AddCommand(new ListCommand<SupplierDto>("Thêm nhà cung cấp") { Allowed = canManage }, _presenter.AddAsync, ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<SupplierDto>("Sửa") { Allowed = canManage }, _presenter.EditAsync, iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<SupplierDto>("Ngừng giao dịch")
            {
                Allowed = canManage,
                TextFor = SupplierListPresenter.ToggleText,
            },
            _presenter.ToggleActiveAsync);
    }

    bool IView.IsAlive => IsAlive;

    protected override object? KeyOf(SupplierDto item) => item.Id;

    protected override Task<IReadOnlyList<SupplierDto>?> LoadItemsAsync(CancellationToken cancellationToken) =>
        _presenter.LoadAsync(cancellationToken);

    bool ISupplierListView.OpenEditor(SupplierDto? supplier)
    {
        using var dialog = new SupplierEditForm(_services.GetRequiredService<SupplierEditPresenter>().For(supplier));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    Task ISupplierListView.ReloadAsync() => RefreshDataAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    bool IView.Confirm(string message, string title, string confirmText) => Confirm(message, title, confirmText);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
