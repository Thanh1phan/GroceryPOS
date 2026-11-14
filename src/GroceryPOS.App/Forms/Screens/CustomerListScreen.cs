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

/// <summary>Khách hàng screen – passive MVP view; the logic lives in <see cref="CustomerListPresenter"/>. Search covers code, name, phone and email.</summary>
internal sealed class CustomerListScreen : BaseListView<CustomerDto>, ICustomerListView
{
    private readonly IServiceProvider _services;
    private readonly CustomerListPresenter _presenter;

    public CustomerListScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<CustomerListPresenter>();
        _presenter.Attach(this);
        var canManage = _presenter.CanManage;

        Grid.EmptyText = "Chưa có khách hàng nào.";
        Grid.AddTextColumn(nameof(CustomerDto.Code), "Mã KH", width: 100);
        Grid.AddTextColumn(nameof(CustomerDto.Name), "Tên khách hàng");
        Grid.AddTextColumn(nameof(CustomerDto.Phone), "Điện thoại", width: 120);
        Grid.AddTextColumn(nameof(CustomerDto.Email), "Email", width: 180);
        Grid.AddNumberColumn(nameof(CustomerDto.LoyaltyPoints), "Điểm", width: 90);
        Grid.AddMoneyColumn(nameof(CustomerDto.TotalSpent), "Tổng chi tiêu", width: 140);
        Grid.AddCheckColumn(nameof(CustomerDto.IsActive), "Hoạt động");

        AddCommand(new ListCommand<CustomerDto>("Thêm khách hàng") { Allowed = canManage }, _presenter.AddAsync, ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<CustomerDto>("Sửa") { Allowed = canManage }, _presenter.EditAsync, iconGlyph: Glyphs.Edit);
        AddCommand(new ListCommand<CustomerDto>("Lịch sử điểm"), _presenter.ShowHistory, iconGlyph: Glyphs.History);
        AddCommand(
            new ListCommand<CustomerDto>("Ngừng hoạt động")
            {
                Allowed = canManage,
                TextFor = CustomerListPresenter.ToggleText,
            },
            _presenter.ToggleActiveAsync);
    }

    bool IView.IsAlive => IsAlive;

    protected override object? KeyOf(CustomerDto item) => item.Id;

    protected override Task<IReadOnlyList<CustomerDto>?> LoadItemsAsync(CancellationToken cancellationToken) =>
        _presenter.LoadAsync(cancellationToken);

    /// <summary>
    /// Enter/double-click: edit for managers, otherwise the (read-only) loyalty history. Exceptions surface
    /// through the WinForms synchronization context to the global handler.
    /// </summary>
    protected override async void OnItemActivated(CustomerDto item) => await _presenter.ActivateAsync(item);

    bool ICustomerListView.OpenEditor(CustomerDto? customer)
    {
        using var dialog = new CustomerEditForm(_services.GetRequiredService<CustomerEditPresenter>().For(customer));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    void ICustomerListView.OpenLoyaltyHistory(CustomerDto customer)
    {
        using var dialog = new CustomerLoyaltyHistoryForm(_services.GetRequiredService<IServiceScopeFactory>(), customer);
        dialog.ShowDialog(this);
    }

    Task ICustomerListView.ReloadAsync() => RefreshDataAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
