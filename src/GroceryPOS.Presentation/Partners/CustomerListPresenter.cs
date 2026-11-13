using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Partners;

/// <summary>The Khách hàng screen as seen by its presenter.</summary>
public interface ICustomerListView : IView
{
    /// <summary>Opens the add (<paramref name="customer"/> = null) or edit dialog; true when it was saved.</summary>
    bool OpenEditor(CustomerDto? customer);

    /// <summary>Shows the (read-only) loyalty points history of <paramref name="customer"/>.</summary>
    void OpenLoyaltyHistory(CustomerDto customer);

    /// <summary>Reloads the list (keeps the selection by key).</summary>
    Task ReloadAsync();
}

/// <summary>
/// Khách hàng screen logic: loading, permission, add/edit, loyalty history and deactivate/reactivate with confirmation.
/// Enter/double-click edits for managers and opens the history for everyone else.
/// </summary>
public sealed class CustomerListPresenter : Presenter<ICustomerListView>
{
    public const string Title = "Khách hàng";
    public const string NoPermission = "Bạn không có quyền quản lý khách hàng.";

    public CustomerListPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.CustomersManage);
    }

    /// <summary>Add, edit and deactivate need <c>customers.manage</c>; the loyalty history is open to anyone who reaches the screen.</summary>
    public bool CanManage { get; }

    /// <summary>Caption of the toggle button for the selected row.</summary>
    public static string ToggleText(CustomerDto? customer) => customer is { IsActive: false } ? "Kích hoạt lại" : "Ngừng hoạt động";

    public static string DeactivateQuestion(CustomerDto customer) =>
        $"Ngừng hoạt động khách hàng \"{customer.Name}\"?\nKhách sẽ không còn được tìm thấy khi bán hàng; điểm tích lũy được giữ nguyên.";

    /// <summary>All customers, or null after showing the error (the view keeps what it had).</summary>
    public async Task<IReadOnlyList<CustomerDto>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await UseAsync<ICustomerService, Result<IReadOnlyList<CustomerDto>>>(
            customers => customers.ListAsync(cancellationToken));
        if (result.IsSuccess)
        {
            return result.Value;
        }

        if (ViewAlive)
        {
            View.ToastError(result.Error.Message, Title);
        }

        return null;
    }

    public Task AddAsync() => OpenEditorAsync(null);

    public Task EditAsync(CustomerDto customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return OpenEditorAsync(customer);
    }

    public void ShowHistory(CustomerDto customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        View.OpenLoyaltyHistory(customer);
    }

    /// <summary>Enter/double-click on a row: edit for managers, otherwise the loyalty history.</summary>
    public Task ActivateAsync(CustomerDto customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (CanManage)
        {
            return EditAsync(customer);
        }

        ShowHistory(customer);
        return Task.CompletedTask;
    }

    /// <summary>Deactivates an active customer (after confirmation) or reactivates an inactive one, then reloads.</summary>
    public async Task ToggleActiveAsync(CustomerDto customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (customer.IsActive && !View.Confirm(DeactivateQuestion(customer), Title, "Ngừng hoạt động"))
        {
            return;
        }

        var result = await UseAsync<ICustomerService, Result>(customers =>
            customer.IsActive ? customers.DeactivateAsync(customer.Id) : customers.ActivateAsync(customer.Id));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess(customer.IsActive
            ? $"Đã ngừng hoạt động khách hàng {customer.Name}."
            : $"Đã kích hoạt lại khách hàng {customer.Name}.");
        await View.ReloadAsync();
    }

    private async Task OpenEditorAsync(CustomerDto? customer)
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (!View.OpenEditor(customer) || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess(customer is null ? "Đã thêm khách hàng mới." : $"Đã lưu khách hàng {customer.Name}.");
        await View.ReloadAsync();
    }
}
