using FluentValidation;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a product. Stock quantity is shown read-only: it is owned by the warehouse/inventory
/// ledger and can only change through goods receipt, manual adjustment, stock count or a sale. The gross margin
/// per unit is shown live under the selling price; saving a price at or below cost asks for confirmation.
/// </summary>
internal sealed class ProductEditForm : BaseEditDialog
{
    private const string CategoryKey = "category";
    private const string CostKey = "cost";
    private const string SellKey = "sell";

    private sealed record VatOption(decimal Rate)
    {
        public override string ToString() => Rate == 0 ? "Không chịu thuế" : $"{Rate}%";
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProductDetail? _product;
    private readonly ModernTextBox _code;
    private readonly ModernTextBox _barcode;
    private readonly ModernTextBox _name;
    private readonly ModernComboBox _category;
    private readonly ModernTextBox _unit;
    private readonly ModernCurrencyBox _costPrice;
    private readonly ModernCurrencyBox _sellPrice;
    private readonly ModernComboBox _vatRate;
    private readonly ModernNumericBox _minStockLevel;
    private readonly CheckBox _isActive;

    public ProductEditForm(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, ProductDetail? product)
        : base(product is null ? "Thêm sản phẩm" : $"Sửa sản phẩm – {product.Code}", columns: 2, contentWidth: 496)
    {
        _scopeFactory = scopeFactory;
        _product = product;
        var canChangePrice = currentUser.HasPermission(Permissions.PricesChange);

        _code = AddField("Mã sản phẩm", new ModernTextBox { Text = product?.Code ?? string.Empty, ReadOnly = product is not null, PlaceholderText = "vd: SUA01" });
        _barcode = AddField("Mã vạch", new ModernTextBox { Text = product?.Barcode ?? string.Empty, PlaceholderText = "Quét hoặc nhập – không bắt buộc", IconGlyph = Glyphs.Barcode });
        _name = AddField("Tên sản phẩm", new ModernTextBox { Text = product?.Name ?? string.Empty, PlaceholderText = "vd: Sữa tươi Vinamilk 180ml" }, fullWidth: true);
        _category = AddField("Danh mục", new ModernComboBox { DisplayMember = nameof(CategoryDto.Name), ValueMember = nameof(CategoryDto.Id) }, trackAs: CategoryKey);
        _unit = AddField("Đơn vị tính", new ModernTextBox { Text = product?.Unit ?? string.Empty, PlaceholderText = "cái, chai, hộp, kg…" });
        _costPrice = AddField("Giá vốn", new ModernCurrencyBox
        {
            Value = product?.CostPrice ?? 0,
            ReadOnly = !canChangePrice,
            HelperText = canChangePrice ? "Tự cập nhật theo bình quân khi nhập hàng." : "Bạn không có quyền thay đổi giá.",
        }, trackAs: CostKey);
        _sellPrice = AddField("Giá bán", new ModernCurrencyBox { Value = product?.SellPrice ?? 0, ReadOnly = !canChangePrice }, trackAs: SellKey);

        _vatRate = AddField("Thuế VAT", new ModernComboBox());
        _vatRate.Items.AddRange(VatRate.Allowed.Select(r => new VatOption(r)).Cast<object>().ToArray());
        _vatRate.SelectedItem = _vatRate.Items.Cast<VatOption>().FirstOrDefault(o => o.Rate == (product?.VatRate ?? VatRate.None));
        _minStockLevel = AddField("Mức tồn tối thiểu", new ModernNumericBox
        {
            DecimalPlaces = 3,
            Value = product?.MinStockLevel ?? 0,
            HelperText = "Dưới mức này sẽ báo sắp hết hàng.",
        });

        AddField("Tồn kho hiện tại", new ModernNumericBox
        {
            DecimalPlaces = 3,
            Value = product?.StockQuantity ?? 0,
            ReadOnly = true,
            TabStop = false,
            HelperText = "Chỉ đổi qua nhập hàng, điều chỉnh, kiểm kê hoặc bán hàng.",
        }, track: false);
        _isActive = AddField("Trạng thái", new CheckBox { Text = "Đang bán", AutoSize = false, Height = 40, Checked = product?.IsActive ?? true });

        _costPrice.ValueChanged += (_, _) => ShowMargin();
        _sellPrice.ValueChanged += (_, _) => ShowMargin();
        ShowMargin();

        Load += async (_, _) => await LoadCategoriesAsync();
    }

    private PriceMargin CurrentMargin => PriceMargin.Of(_costPrice.ValueOrZero, _sellPrice.ValueOrZero);

    /// <summary>Live gross margin under the selling price: amber for thin margins, red when selling at a loss.</summary>
    private void ShowMargin()
    {
        var margin = CurrentMargin;
        _sellPrice.HelperText = margin.Text;
        _sellPrice.ValidationState = margin.Tone switch
        {
            PriceMarginTone.Loss => ValidationState.Error,
            PriceMarginTone.Thin => ValidationState.Warning,
            _ => ValidationState.None,
        };
        _sellPrice.ValidationMessage = margin.Tone is PriceMarginTone.Loss or PriceMarginTone.Thin ? margin.Text : null;
    }

    private async Task LoadCategoriesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync();
        if (!IsAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            ShowFormError(result.Error.Message);
            return;
        }

        _category.DataSource = result.Value.Where(c => c.IsActive || c.Id == _product?.CategoryId).ToList();
        _category.SelectedValue = _product?.CategoryId;
        Changes.AcceptChange(CategoryKey); // filling the list is not an edit by the user
    }

    protected override async Task<string?> SaveAsync()
    {
        if (_category.SelectedValue is not int categoryId)
        {
            _category.Select();
            return "Vui lòng chọn danh mục.";
        }

        if (_vatRate.SelectedItem is not VatOption vat)
        {
            return "Vui lòng chọn thuế VAT.";
        }

        var pricesTouched = _product is null || Changes.IsChanged(CostKey) || Changes.IsChanged(SellKey);
        if (pricesTouched
            && _sellPrice.ValueOrZero > 0
            && CurrentMargin.Tone == PriceMarginTone.Loss
            && !Confirm($"{CurrentMargin.Text}\nVẫn lưu giá bán này?", Text, "Vẫn lưu", "Sửa lại giá", danger: true))
        {
            _sellPrice.Select();
            return string.Empty;
        }

        var request = new SaveProductRequest(
            _code.Text,
            string.IsNullOrWhiteSpace(_barcode.Text) ? null : _barcode.Text,
            _name.Text,
            categoryId,
            _unit.Text,
            _costPrice.ValueOrZero,
            _sellPrice.ValueOrZero,
            vat.Rate,
            _minStockLevel.ValueOrZero);

        using var scope = _scopeFactory.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<SaveProductRequest>>();
        if (validator.Check(request) is { IsFailure: true } invalid)
        {
            return invalid.ErrorMessageOrNull();
        }

        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var saved = _product is null
            ? await products.CreateAsync(request)
            : await products.UpdateAsync(_product.Id, request);
        if (saved.IsFailure)
        {
            return saved.Error.Message;
        }

        if (saved.Value.IsActive != _isActive.Checked)
        {
            var status = await products.SetActiveAsync(saved.Value.Id, _isActive.Checked);
            if (status.IsFailure)
            {
                return status.Error.Message;
            }
        }

        return null;
    }
}
