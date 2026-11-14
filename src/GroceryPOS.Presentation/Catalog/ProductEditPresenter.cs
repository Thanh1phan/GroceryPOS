using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Catalog;

/// <summary>A choice in the VAT drop-down.</summary>
public sealed record VatOption(decimal Rate)
{
    public string Text => Rate == 0 ? "Không chịu thuế" : $"{Rate}%";

    public override string ToString() => Text;
}

/// <summary>
/// The add/edit product dialog as seen by its presenter. Field names are <see cref="SaveProductRequest"/> property
/// names (<c>Code</c>, <c>Barcode</c>, <c>Name</c>, <c>CategoryId</c>, <c>Unit</c>, <c>CostPrice</c>, <c>SellPrice</c>,
/// <c>VatRate</c>, <c>MinStockLevel</c>).
/// </summary>
public interface IProductEditView : IView
{
    string Code { get; }

    string Barcode { get; }

    string ProductName { get; }

    int? CategoryId { get; }

    string Unit { get; }

    decimal CostPrice { get; }

    decimal SellPrice { get; }

    decimal? VatRate { get; }

    decimal MinStockLevel { get; }

    bool IsActive { get; }

    /// <summary>Fills the category list and selects <paramref name="selectedCategoryId"/>; filling it is not an edit by the user.</summary>
    void ShowCategories(IReadOnlyList<CategoryDto> categories, int? selectedCategoryId);

    /// <summary>Moves focus to the field for <paramref name="field"/>.</summary>
    void FocusField(string field);
}

/// <summary>
/// Add/edit product logic. Stock is read-only here (only goods receipt, adjustments, counts and sales change it);
/// prices are read-only without <c>prices.change</c>. A live gross margin follows the selling price, and saving a
/// selling price at or below cost asks first – but only when a price was actually typed, so renaming an
/// old loss-leader does not nag.
/// </summary>
public sealed class ProductEditPresenter : Presenter<IProductEditView>
{
    public const string ChooseCategory = "Vui lòng chọn danh mục.";
    public const string ChooseVat = "Vui lòng chọn thuế VAT.";

    public ProductEditPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        CanChangePrice = currentUser.HasPermission(Permissions.PricesChange);
    }

    /// <summary>The product being edited (fresh details), or null when adding.</summary>
    public ProductDetail? Product { get; private set; }

    public bool IsNew => Product is null;

    public string Title => Product is null ? "Thêm sản phẩm" : $"Sửa sản phẩm – {Product.Code}";

    /// <summary>Cost and selling price are editable only with <c>prices.change</c>.</summary>
    public bool CanChangePrice { get; }

    public string CostHelperText => CanChangePrice ? "Tự cập nhật theo bình quân khi nhập hàng." : "Bạn không có quyền thay đổi giá.";

    /// <summary>VAT choices in display order (0 %, 5 %, 8 %, 10 %).</summary>
    public static IReadOnlyList<VatOption> VatOptions { get; } = VatRate.Allowed.Select(r => new VatOption(r)).ToList();

    /// <summary>The option to preselect: the product's rate, or "không chịu thuế" for a new product.</summary>
    public VatOption InitialVat => VatOptions.FirstOrDefault(o => o.Rate == (Product?.VatRate ?? VatRate.None)) ?? VatOptions[0];

    public ProductEditPresenter For(ProductDetail? product)
    {
        Product = product;
        return this;
    }

    /// <summary>Live gross margin for the typed prices.</summary>
    public static PriceMargin Margin(decimal costPrice, decimal sellPrice) => PriceMargin.Of(costPrice, sellPrice);

    /// <summary>Active categories, plus the product's current one even when it was stopped.</summary>
    public static IReadOnlyList<CategoryDto> SelectableCategories(IEnumerable<CategoryDto> categories, int? currentCategoryId)
    {
        ArgumentNullException.ThrowIfNull(categories);
        return categories.Where(c => c.IsActive || c.Id == currentCategoryId).ToList();
    }

    /// <summary>True when the dialog should ask before saving these prices (selling at or below cost, prices typed).</summary>
    public bool NeedsLossConfirmation(decimal costPrice, decimal sellPrice)
    {
        var pricesTouched = Product is null || costPrice != Product.CostPrice || sellPrice != Product.SellPrice;
        return pricesTouched && sellPrice > 0 && Margin(costPrice, sellPrice).Tone == PriceMarginTone.Loss;
    }

    /// <summary>Loads the category list into the view; returns the message for the error banner, or null.</summary>
    public async Task<string?> LoadCategoriesAsync()
    {
        var result = await UseAsync<ICategoryService, Result<IReadOnlyList<CategoryDto>>>(categories => categories.ListAsync());
        if (!ViewAlive)
        {
            return null;
        }

        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        View.ShowCategories(SelectableCategories(result.Value, Product?.CategoryId), Product?.CategoryId);
        return null;
    }

    /// <summary>
    /// Validates and saves (and applies the "Đang bán" checkbox); returns the message for the dialog's error banner,
    /// an empty string to stay open silently (the user chose to fix a loss-making price), or null when saved.
    /// </summary>
    public Task<string?> SaveAsync() => UseScopeAsync<string?>(async services =>
    {
        if (View.CategoryId is not int categoryId)
        {
            View.FocusField(nameof(SaveProductRequest.CategoryId));
            return ChooseCategory;
        }

        if (View.VatRate is not decimal vatRate)
        {
            View.FocusField(nameof(SaveProductRequest.VatRate));
            return ChooseVat;
        }

        var cost = View.CostPrice;
        var sell = View.SellPrice;
        if (NeedsLossConfirmation(cost, sell)
            && !View.Confirm($"{Margin(cost, sell).Text}\nVẫn lưu giá bán này?", Title, "Vẫn lưu", danger: true))
        {
            View.FocusField(nameof(SaveProductRequest.SellPrice));
            return string.Empty;
        }

        var request = new SaveProductRequest(
            View.Code.Trim(),
            FormInput.Optional(View.Barcode),
            View.ProductName.Trim(),
            categoryId,
            View.Unit.Trim(),
            cost,
            sell,
            vatRate,
            View.MinStockLevel);
        if (FormInput.FirstError(services.GetRequiredService<IValidator<SaveProductRequest>>(), request) is { } invalid)
        {
            View.FocusField(invalid.Field);
            return invalid.Message;
        }

        var products = services.GetRequiredService<IProductService>();
        var saved = Product is null
            ? await products.CreateAsync(request)
            : await products.UpdateAsync(Product.Id, request);
        if (saved.IsFailure)
        {
            return saved.Error.Message;
        }

        var wantActive = View.IsActive;
        if (saved.Value.IsActive != wantActive)
        {
            var status = await products.SetActiveAsync(saved.Value.Id, wantActive);
            if (status.IsFailure)
            {
                return status.Error.Message;
            }
        }

        return null;
    });
}
