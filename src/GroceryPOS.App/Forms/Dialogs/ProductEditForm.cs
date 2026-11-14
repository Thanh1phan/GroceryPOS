using GroceryPOS.Application.Catalog;
using GroceryPOS.Presentation.Catalog;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a product – passive MVP view of <see cref="ProductEditPresenter"/>. Stock quantity is shown
/// read-only; the gross margin per unit is shown live under the selling price.
/// </summary>
internal sealed class ProductEditForm : BaseEditDialog, IProductEditView
{
    private const string CategoryKey = "category";

    private readonly ProductEditPresenter _presenter;
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

    public ProductEditForm(ProductEditPresenter presenter)
        : base(presenter.Title, columns: 2, contentWidth: 496)
    {
        _presenter = presenter;
        var product = presenter.Product;
        var canChangePrice = presenter.CanChangePrice;

        _code = AddField("Mã sản phẩm", new ModernTextBox { Text = product?.Code ?? string.Empty, ReadOnly = !presenter.IsNew, PlaceholderText = "vd: SUA01" });
        _barcode = AddField("Mã vạch", new ModernTextBox { Text = product?.Barcode ?? string.Empty, PlaceholderText = "Quét hoặc nhập – không bắt buộc", IconGlyph = Glyphs.Barcode });
        _name = AddField("Tên sản phẩm", new ModernTextBox { Text = product?.Name ?? string.Empty, PlaceholderText = "vd: Sữa tươi Vinamilk 180ml" }, fullWidth: true);
        _category = AddField("Danh mục", new ModernComboBox { DisplayMember = nameof(CategoryDto.Name), ValueMember = nameof(CategoryDto.Id) }, trackAs: CategoryKey);
        _unit = AddField("Đơn vị tính", new ModernTextBox { Text = product?.Unit ?? string.Empty, PlaceholderText = "cái, chai, hộp, kg…" });
        _costPrice = AddField("Giá vốn", new ModernCurrencyBox
        {
            Value = product?.CostPrice ?? 0,
            ReadOnly = !canChangePrice,
            HelperText = presenter.CostHelperText,
        });
        _sellPrice = AddField("Giá bán", new ModernCurrencyBox { Value = product?.SellPrice ?? 0, ReadOnly = !canChangePrice });

        _vatRate = AddField("Thuế VAT", new ModernComboBox());
        _vatRate.Items.AddRange(ProductEditPresenter.VatOptions.Cast<object>().ToArray());
        _vatRate.SelectedItem = presenter.InitialVat;
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

        _presenter.Attach(this);
        Load += async (_, _) => ShowFormError(await _presenter.LoadCategoriesAsync());
    }

    string IProductEditView.Code => _code.Text;

    string IProductEditView.Barcode => _barcode.Text;

    string IProductEditView.ProductName => _name.Text;

    int? IProductEditView.CategoryId => _category.SelectedValue is int id ? id : null;

    string IProductEditView.Unit => _unit.Text;

    decimal IProductEditView.CostPrice => _costPrice.ValueOrZero;

    decimal IProductEditView.SellPrice => _sellPrice.ValueOrZero;

    decimal? IProductEditView.VatRate => (_vatRate.SelectedItem as VatOption)?.Rate;

    decimal IProductEditView.MinStockLevel => _minStockLevel.ValueOrZero;

    bool IProductEditView.IsActive => _isActive.Checked;

    bool IView.IsAlive => IsAlive;

    /// <summary>Live gross margin under the selling price: amber for thin margins, red when selling at a loss.</summary>
    private void ShowMargin()
    {
        var margin = ProductEditPresenter.Margin(_costPrice.ValueOrZero, _sellPrice.ValueOrZero);
        _sellPrice.HelperText = margin.Text;
        _sellPrice.ValidationState = margin.Tone switch
        {
            PriceMarginTone.Loss => ValidationState.Error,
            PriceMarginTone.Thin => ValidationState.Warning,
            _ => ValidationState.None,
        };
        _sellPrice.ValidationMessage = margin.Tone is PriceMarginTone.Loss or PriceMarginTone.Thin ? margin.Text : null;
    }

    void IProductEditView.ShowCategories(IReadOnlyList<CategoryDto> categories, int? selectedCategoryId)
    {
        _category.DataSource = categories.ToList();
        _category.SelectedValue = selectedCategoryId;
        Changes.AcceptChange(CategoryKey); // filling the list is not an edit by the user
    }

    void IProductEditView.FocusField(string field)
    {
        Control target = field switch
        {
            nameof(SaveProductRequest.Code) => _code,
            nameof(SaveProductRequest.Barcode) => _barcode,
            nameof(SaveProductRequest.CategoryId) => _category,
            nameof(SaveProductRequest.Unit) => _unit,
            nameof(SaveProductRequest.CostPrice) => _costPrice,
            nameof(SaveProductRequest.SellPrice) => _sellPrice,
            nameof(SaveProductRequest.VatRate) => _vatRate,
            nameof(SaveProductRequest.MinStockLevel) => _minStockLevel,
            _ => _name,
        };
        target.Select();
    }

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
