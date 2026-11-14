using GroceryPOS.Application.Catalog;
using GroceryPOS.App.Forms.Dialogs;
using GroceryPOS.Presentation.Catalog;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Forms.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>Danh mục screen – passive MVP view; the logic lives in <see cref="CategoryListPresenter"/>.</summary>
internal sealed class CategoryListScreen : BaseListView<CategoryDto>, ICategoryListView
{
    private readonly IServiceProvider _services;
    private readonly CategoryListPresenter _presenter;

    public CategoryListScreen(IServiceProvider services)
    {
        _services = services;
        _presenter = services.GetRequiredService<CategoryListPresenter>();
        _presenter.Attach(this);
        var canManage = _presenter.CanManage;

        Grid.EmptyText = "Chưa có danh mục nào.";
        Grid.AddTextColumn(nameof(CategoryDto.Name), "Tên danh mục", width: 240);
        Grid.AddTextColumn(nameof(CategoryDto.Description), "Mô tả");
        Grid.AddCheckColumn(nameof(CategoryDto.IsActive), "Đang dùng");

        AddCommand(new ListCommand<CategoryDto>("Thêm danh mục") { Allowed = canManage }, _presenter.AddAsync, ButtonVariant.Primary, Glyphs.Add);
        AddCommand(new ListCommand<CategoryDto>("Sửa") { Allowed = canManage }, _presenter.EditAsync, iconGlyph: Glyphs.Edit, isDefault: true);
        AddCommand(
            new ListCommand<CategoryDto>("Ngừng dùng")
            {
                Allowed = canManage,
                TextFor = CategoryListPresenter.ToggleText,
            },
            _presenter.ToggleActiveAsync);
    }

    bool IView.IsAlive => IsAlive;

    protected override object? KeyOf(CategoryDto item) => item.Id;

    protected override Task<IReadOnlyList<CategoryDto>?> LoadItemsAsync(CancellationToken cancellationToken) =>
        _presenter.LoadAsync(cancellationToken);

    bool ICategoryListView.OpenEditor(CategoryDto? category)
    {
        using var dialog = new CategoryEditForm(_services.GetRequiredService<CategoryEditPresenter>().For(category));
        return dialog.ShowDialog(this) == DialogResult.OK;
    }

    Task ICategoryListView.ReloadAsync() => RefreshDataAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
