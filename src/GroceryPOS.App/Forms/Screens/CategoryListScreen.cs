using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Category CRUD screen (deactivate/activate only — a category in use can never be hard-deleted).</summary>
internal sealed class CategoryListScreen : BaseListView<CategoryDto>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CategoryListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        var canManage = currentUser.HasPermission(Permissions.CategoriesManage);

        Grid.EmptyText = "Chưa có danh mục nào.";
        Grid.AddTextColumn(nameof(CategoryDto.Name), "Tên danh mục", width: 240);
        Grid.AddTextColumn(nameof(CategoryDto.Description), "Mô tả");
        Grid.AddCheckColumn(nameof(CategoryDto.IsActive), "Đang dùng");

        AddCommand(new ListCommand<CategoryDto>("Thêm danh mục") { Allowed = canManage }, () => Open(null), ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<CategoryDto>("Sửa") { Allowed = canManage }, category => Open(category), iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<CategoryDto>("Ngừng dùng")
            {
                Allowed = canManage,
                TextFor = c => c.IsActive ? "Ngừng dùng" : "Dùng lại",
            },
            ToggleActiveAsync);
    }

    protected override object? KeyOf(CategoryDto item) => item.Id;

    protected override async Task<IReadOnlyList<CategoryDto>?> LoadItemsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync(cancellationToken);
        if (result.IsFailure)
        {
            ToastError(result.Error.Message, "Danh mục");
            return null;
        }

        return result.Value;
    }

    private void Open(CategoryDto? category)
    {
        using var dialog = new CategoryEditForm(_scopeFactory, category);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ToastSuccess(category is null ? "Đã thêm danh mục mới." : $"Đã lưu danh mục {category.Name}.");
            RequestRefresh();
        }
    }

    private async Task ToggleActiveAsync(CategoryDto category)
    {
        using var scope = _scopeFactory.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var result = await (category.IsActive ? categories.DeactivateAsync(category.Id) : categories.ActivateAsync(category.Id));
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowWarning(result.Error.Message, "Danh mục");
            return;
        }

        ToastSuccess(category.IsActive ? $"Đã ngừng dùng danh mục {category.Name}." : $"Đã dùng lại danh mục {category.Name}.");
        await RefreshDataAsync();
    }
}
