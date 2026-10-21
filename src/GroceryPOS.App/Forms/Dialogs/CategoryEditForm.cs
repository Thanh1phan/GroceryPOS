using FluentValidation;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Validation;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a product category.</summary>
internal sealed class CategoryEditForm : ModernDialogForm
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CategoryDto? _category;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;
    private readonly ModernButton _save;

    public CategoryEditForm(IServiceScopeFactory scopeFactory, CategoryDto? category)
    {
        _scopeFactory = scopeFactory;
        _category = category;

        Text = category is null ? "Thêm danh mục" : "Sửa danh mục";
        ClientSize = new Size(380, 284);

        var nameLabel = new Label { Text = "Tên danh mục", AutoSize = true, Location = new Point(32, 16) };
        _name = new ModernTextBox
        {
            PlaceholderText = "Tên danh mục",
            Location = new Point(32, 40),
            Width = 316,
            Text = category?.Name ?? string.Empty,
        };

        var descriptionLabel = new Label { Text = "Mô tả", AutoSize = true, Location = new Point(32, 96) };
        _description = new ModernTextBox
        {
            PlaceholderText = "Mô tả (không bắt buộc)",
            Location = new Point(32, 120),
            Width = 316,
            Text = category?.Description ?? string.Empty,
        };

        ErrorLabel.Location = new Point(32, 176);
        ErrorLabel.Size = new Size(316, 40);

        _save = new ModernButton
        {
            Text = "Lưu",
            Variant = ButtonVariant.Primary,
            Location = new Point(32, 224),
            Size = new Size(316, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();

        Controls.AddRange([nameLabel, _name, descriptionLabel, _description, _save]);
    }

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        var request = new SaveCategoryRequest(_name.Text, string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text);

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveCategoryRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var result = _category is null
            ? await categories.CreateAsync(request)
            : await categories.UpdateAsync(_category.Id, request);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    });
}
