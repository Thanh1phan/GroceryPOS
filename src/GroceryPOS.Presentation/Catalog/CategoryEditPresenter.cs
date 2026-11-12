using FluentValidation;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Validation;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Catalog;

/// <summary>The add/edit category dialog as seen by its presenter.</summary>
public interface ICategoryEditView : IView
{
    string CategoryName { get; }

    string Description { get; }

    /// <summary>Moves focus to the name box (after a validation error).</summary>
    void FocusName();
}

/// <summary>Add/edit category logic: initial values, request building, validation and the create/update call.</summary>
public sealed class CategoryEditPresenter : Presenter<ICategoryEditView>
{
    public CategoryEditPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    /// <summary>The category being edited, or null when adding.</summary>
    public CategoryDto? Category { get; private set; }

    public bool IsNew => Category is null;

    public string Title => TitleFor(Category);

    public string InitialName => Category?.Name ?? string.Empty;

    public string InitialDescription => Category?.Description ?? string.Empty;

    public static string TitleFor(CategoryDto? category) => category is null ? "Thêm danh mục" : "Sửa danh mục";

    /// <summary>Chooses what the dialog edits; call before the view is built.</summary>
    public CategoryEditPresenter For(CategoryDto? category)
    {
        Category = category;
        return this;
    }

    /// <summary>Blank or whitespace-only description is stored as null.</summary>
    public static SaveCategoryRequest BuildRequest(string? name, string? description) =>
        new(name ?? string.Empty, string.IsNullOrWhiteSpace(description) ? null : description);

    /// <summary>Validates and saves; returns the message for the dialog's error banner, or null when saved.</summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        var request = BuildRequest(View.CategoryName, View.Description);
        var validator = services.GetRequiredService<IValidator<SaveCategoryRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            View.FocusName();
            return invalid.Error.Message;
        }

        var categories = services.GetRequiredService<ICategoryService>();
        Result result = Category is null
            ? await categories.CreateAsync(request)
            : await categories.UpdateAsync(Category.Id, request);
        return result.IsSuccess ? null : result.Error.Message;
    });
}
