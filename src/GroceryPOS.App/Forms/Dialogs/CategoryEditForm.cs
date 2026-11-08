using FluentValidation;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a product category (first adopter of <see cref="BaseEditDialog"/>).</summary>
internal sealed class CategoryEditForm : BaseEditDialog
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CategoryDto? _category;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;

    public CategoryEditForm(IServiceScopeFactory scopeFactory, CategoryDto? category)
        : base(category is null ? "Thêm danh mục" : "Sửa danh mục")
    {
        _scopeFactory = scopeFactory;
        _category = category;

        _name = AddField("Tên danh mục", new ModernTextBox { PlaceholderText = "Ví dụ: Sữa & chế phẩm", Text = category?.Name ?? string.Empty });
        _description = AddField("Mô tả", new ModernTextBox { PlaceholderText = "Không bắt buộc", Text = category?.Description ?? string.Empty });
    }

    protected override async Task<string?> SaveAsync()
    {
        var request = new SaveCategoryRequest(_name.Text, string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text);

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveCategoryRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            _name.Select();
            return invalid.ErrorMessageOrNull();
        }

        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var result = _category is null
            ? await categories.CreateAsync(request)
            : await categories.UpdateAsync(_category.Id, request);
        return result.ErrorMessageOrNull();
    }
}
