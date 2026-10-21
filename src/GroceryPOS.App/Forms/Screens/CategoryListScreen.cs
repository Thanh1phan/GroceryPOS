using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Category CRUD screen (deactivate/activate only — a category in use can never be hard-deleted).</summary>
internal sealed class CategoryListScreen : UserControl
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _canManage;
    private readonly ModernDataGrid _grid;
    private readonly ModernButton _edit;
    private readonly ModernButton _toggleActive;

    public CategoryListScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _canManage = currentUser.HasPermission(Permissions.CategoriesManage);
        Dock = DockStyle.Fill;

        _grid = new ModernDataGrid { Dock = DockStyle.Fill, EmptyText = "Chưa có danh mục nào." };
        _grid.AddTextColumn(nameof(CategoryDto.Name), "Tên danh mục", width: 240);
        _grid.AddTextColumn(nameof(CategoryDto.Description), "Mô tả");
        _grid.AddCheckColumn(nameof(CategoryDto.IsActive), "Đang dùng");
        _grid.RowActivated += (_, e) => Edit((CategoryDto)e.Item);
        _grid.SelectionChanged += (_, _) => UpdateToolbarState();

        var add = new ModernButton { Text = "Thêm danh mục", Variant = ButtonVariant.Primary, Enabled = _canManage };
        add.Click += (_, _) => Add();

        _edit = new ModernButton { Text = "Sửa", Variant = ButtonVariant.Secondary, Enabled = false };
        _edit.Click += (_, _) =>
        {
            if (_grid.GetSelected<CategoryDto>() is { } category)
            {
                Edit(category);
            }
        };

        _toggleActive = new ModernButton { Text = "Ngừng dùng", Variant = ButtonVariant.Secondary, Enabled = false };
        _toggleActive.Click += async (_, _) => await ToggleActiveAsync();

        _grid.ToolbarItems.Controls.AddRange([add, _edit, _toggleActive]);
        Controls.Add(_grid);

        Load += async (_, _) => await ReloadAsync();
    }

    private void UpdateToolbarState()
    {
        var selected = _grid.GetSelected<CategoryDto>();
        _edit.Enabled = _canManage && selected is not null;
        _toggleActive.Enabled = _canManage && selected is not null;
        _toggleActive.Text = selected is { IsActive: true } ? "Ngừng dùng" : "Dùng lại";
    }

    private void Add()
    {
        using var dialog = new CategoryEditForm(_scopeFactory, category: null);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private void Edit(CategoryDto category)
    {
        if (!_canManage)
        {
            return;
        }

        using var dialog = new CategoryEditForm(_scopeFactory, category);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ = ReloadAsync();
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (_grid.GetSelected<CategoryDto>() is not { } category)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        await (category.IsActive ? categories.DeactivateAsync(category.Id) : categories.ActivateAsync(category.Id));
        if (!IsDisposed)
        {
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var result = await categories.ListAsync();
        if (IsDisposed)
        {
            // The user navigated away before this load finished; touching a torn-down grid would crash.
            return;
        }

        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value);
        }

        UpdateToolbarState();
    }
}
