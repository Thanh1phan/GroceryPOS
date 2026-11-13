using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Partners;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Partners;

/// <summary>The Nhà cung cấp screen as seen by its presenter.</summary>
public interface ISupplierListView : IView
{
    /// <summary>Opens the add (<paramref name="supplier"/> = null) or edit dialog; true when it was saved.</summary>
    bool OpenEditor(SupplierDto? supplier);

    /// <summary>Reloads the list (keeps the selection by key).</summary>
    Task ReloadAsync();
}

/// <summary>
/// Nhà cung cấp screen logic: loading, permission, add/edit flow and stop/resume trading with confirmation.
/// Suppliers are never deleted – receipts and payments refer to them.
/// </summary>
public sealed class SupplierListPresenter : Presenter<ISupplierListView>
{
    public const string Title = "Nhà cung cấp";
    public const string NoPermission = "Bạn không có quyền quản lý nhà cung cấp.";

    public SupplierListPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.SuppliersManage);
    }

    /// <summary>Add, edit and stop/resume need <c>suppliers.manage</c>.</summary>
    public bool CanManage { get; }

    /// <summary>Caption of the toggle button for the selected row.</summary>
    public static string ToggleText(SupplierDto? supplier) => supplier is { IsActive: false } ? "Giao dịch lại" : "Ngừng giao dịch";

    public static string StopQuestion(SupplierDto supplier) =>
        $"Ngừng giao dịch với \"{supplier.Name}\"?\nNhà cung cấp sẽ không còn trong danh sách chọn khi nhập hàng.";

    /// <summary>All suppliers, or null after showing the error (the view keeps what it had).</summary>
    public async Task<IReadOnlyList<SupplierDto>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await UseAsync<ISupplierService, Result<IReadOnlyList<SupplierDto>>>(
            suppliers => suppliers.ListAsync(cancellationToken));
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

    public Task EditAsync(SupplierDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return OpenEditorAsync(supplier);
    }

    /// <summary>Stops trading with an active supplier (after confirmation) or resumes a stopped one, then reloads.</summary>
    public async Task ToggleActiveAsync(SupplierDto supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (supplier.IsActive && !View.Confirm(StopQuestion(supplier), Title, "Ngừng giao dịch"))
        {
            return;
        }

        var result = await UseAsync<ISupplierService, Result>(suppliers =>
            supplier.IsActive ? suppliers.DeactivateAsync(supplier.Id) : suppliers.ActivateAsync(supplier.Id));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess(supplier.IsActive
            ? $"Đã ngừng giao dịch với {supplier.Name}."
            : $"Đã giao dịch lại với {supplier.Name}.");
        await View.ReloadAsync();
    }

    private async Task OpenEditorAsync(SupplierDto? supplier)
    {
        if (!CanManage)
        {
            View.ShowWarning(NoPermission, Title);
            return;
        }

        if (!View.OpenEditor(supplier) || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess(supplier is null ? "Đã thêm nhà cung cấp mới." : $"Đã lưu nhà cung cấp {supplier.Name}.");
        await View.ReloadAsync();
    }
}
