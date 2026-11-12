using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Catalog;

/// <summary>The Danh mục screen as seen by its presenter.</summary>
public interface ICategoryListView : IView
{
    /// <summary>Opens the add (<paramref name="category"/> = null) or edit dialog; true when it was saved.</summary>
    bool OpenEditor(CategoryDto? category);

    /// <summary>Reloads the list (keeps the selection by key).</summary>
    Task ReloadAsync();
}

/// <summary>
/// Danh mục screen logic: loading, permission, add/edit flow and the stop/resume toggle with its feedback.
/// No hard delete – a category in use can never be removed.
/// </summary>
public sealed class CategoryListPresenter : Presenter<ICategoryListView>
{
    public const string Title = "Danh mục";

    public CategoryListPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanManage = currentUser.HasPermission(Permissions.CategoriesManage);
    }

    /// <summary>Add, edit and stop/resume need <c>categories.manage</c>; viewing is open to anyone who reaches the screen.</summary>
    public bool CanManage { get; }

    /// <summary>Caption of the toggle button for the selected row.</summary>
    public static string ToggleText(CategoryDto? category) => category is { IsActive: false } ? "Dùng lại" : "Ngừng dùng";

    /// <summary>All categories, or null after showing the error (the view keeps what it had).</summary>
    public async Task<IReadOnlyList<CategoryDto>?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await UseAsync<ICategoryService, Result<IReadOnlyList<CategoryDto>>>(
            categories => categories.ListAsync(cancellationToken));
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

    public Task EditAsync(CategoryDto category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return OpenEditorAsync(category);
    }

    /// <summary>Stops an active category or resumes a stopped one, then reloads.</summary>
    public async Task ToggleActiveAsync(CategoryDto category)
    {
        ArgumentNullException.ThrowIfNull(category);
        if (!CanManage)
        {
            View.ShowWarning("Bạn không có quyền quản lý danh mục.", Title);
            return;
        }

        var result = await UseAsync<ICategoryService, Result>(categories =>
            category.IsActive ? categories.DeactivateAsync(category.Id) : categories.ActivateAsync(category.Id));
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowWarning(result.Error.Message, Title);
            return;
        }

        View.ToastSuccess(category.IsActive
            ? $"Đã ngừng dùng danh mục {category.Name}."
            : $"Đã dùng lại danh mục {category.Name}.");
        await View.ReloadAsync();
    }

    private async Task OpenEditorAsync(CategoryDto? category)
    {
        if (!CanManage)
        {
            View.ShowWarning("Bạn không có quyền quản lý danh mục.", Title);
            return;
        }

        if (!View.OpenEditor(category) || !ViewAlive)
        {
            return;
        }

        View.ToastSuccess(category is null ? "Đã thêm danh mục mới." : $"Đã lưu danh mục {category.Name}.");
        await View.ReloadAsync();
    }
}
