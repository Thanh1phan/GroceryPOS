using FluentValidation;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a product. Stock quantity is shown read-only: it is owned by the warehouse/inventory
/// ledger and can only change through goods receipt, manual adjustment, stock count or a sale.
/// </summary>
internal sealed class ProductEditForm : ModernDialogForm
{
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
    private readonly ModernNumericBox _stockQuantity;
    private readonly CheckBox _isActive;
    private readonly ModernButton _save;

    public ProductEditForm(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, ProductDetail? product)
    {
        _scopeFactory = scopeFactory;
        _product = product;

        Text = product is null ? "Thêm sản phẩm" : "Sửa sản phẩm";
        ClientSize = new Size(420, 560);

        var y = 16;
        Label Label(string text)
        {
            var label = new Label { Text = text, AutoSize = true, Location = new Point(32, y) };
            y += 24;
            return label;
        }

        void Advance() => y += 56;

        var codeLabel = Label("Mã sản phẩm");
        _code = new ModernTextBox { Location = new Point(32, y), Width = 356, Text = product?.Code ?? string.Empty, ReadOnly = product is not null };
        Advance();

        var barcodeLabel = Label("Mã vạch");
        _barcode = new ModernTextBox { Location = new Point(32, y), Width = 356, Text = product?.Barcode ?? string.Empty, PlaceholderText = "Không bắt buộc" };
        Advance();

        var nameLabel = Label("Tên sản phẩm");
        _name = new ModernTextBox { Location = new Point(32, y), Width = 356, Text = product?.Name ?? string.Empty };
        Advance();

        var categoryLabel = Label("Danh mục");
        _category = new ModernComboBox { Location = new Point(32, y), Width = 356, DisplayMember = nameof(CategoryDto.Name), ValueMember = nameof(CategoryDto.Id) };
        Advance();

        var unitLabel = Label("Đơn vị tính");
        _unit = new ModernTextBox { Location = new Point(32, y), Width = 356, Text = product?.Unit ?? string.Empty, PlaceholderText = "cái, chai, hộp, kg…" };
        Advance();

        var costLabel = Label("Giá vốn");
        _costPrice = new ModernCurrencyBox { Location = new Point(32, y), Width = 356, Value = product?.CostPrice ?? 0 };
        Advance();

        var sellLabel = Label("Giá bán");
        _sellPrice = new ModernCurrencyBox { Location = new Point(32, y), Width = 356, Value = product?.SellPrice ?? 0 };
        Advance();

        if (!currentUser.HasPermission(Permissions.PricesChange))
        {
            _costPrice.ReadOnly = true;
            _sellPrice.ReadOnly = true;
            _costPrice.HelperText = "Bạn không có quyền thay đổi giá.";
            _sellPrice.HelperText = "Bạn không có quyền thay đổi giá.";
        }

        var vatLabel = Label("Thuế VAT");
        _vatRate = new ModernComboBox { Location = new Point(32, y), Width = 356 };
        _vatRate.Items.AddRange(VatRate.Allowed.Select(r => new VatOption(r)).Cast<object>().ToArray());
        _vatRate.SelectedItem = _vatRate.Items.Cast<VatOption>().FirstOrDefault(o => o.Rate == (product?.VatRate ?? VatRate.None));
        Advance();

        var minStockLabel = Label("Mức tồn tối thiểu");
        _minStockLevel = new ModernNumericBox { Location = new Point(32, y), Width = 356, DecimalPlaces = 3, Value = product?.MinStockLevel ?? 0 };
        Advance();

        var stockLabel = Label("Tồn kho hiện tại");
        _stockQuantity = new ModernNumericBox
        {
            Location = new Point(32, y),
            Width = 356,
            DecimalPlaces = 3,
            Value = product?.StockQuantity ?? 0,
            ReadOnly = true,
            HelperText = "Chỉ thay đổi qua nhập hàng, điều chỉnh, kiểm kê hoặc bán hàng.",
        };
        Advance();

        _isActive = new CheckBox { Text = "Đang bán", AutoSize = true, Location = new Point(32, y), Checked = product?.IsActive ?? true };
        y += 32;

        ErrorLabel.Location = new Point(32, y);
        ErrorLabel.Size = new Size(356, 40);
        y += 48;

        _save = new ModernButton { Text = "Lưu", Variant = ButtonVariant.Primary, Location = new Point(32, y), Size = new Size(356, 40) };
        _save.Click += async (_, _) => await SaveAsync();

        Controls.AddRange([
            codeLabel, _code, barcodeLabel, _barcode, nameLabel, _name, categoryLabel, _category, unitLabel, _unit,
            costLabel, _costPrice, sellLabel, _sellPrice, vatLabel, _vatRate, minStockLabel, _minStockLevel,
            stockLabel, _stockQuantity, _isActive, _save,
        ]);
        ClientSize = new Size(420, y + 56);

        Load += async (_, _) => await LoadCategoriesAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var result = await categories.ListAsync();
        if (!result.IsSuccess)
        {
            return;
        }

        _category.DataSource = result.Value.Where(c => c.IsActive || c.Id == _product?.CategoryId).ToList();
        _category.SelectedValue = _product?.CategoryId;
    }

    private async Task SaveAsync() => await RunSaveAsync(_save, async () =>
    {
        if (_category.SelectedValue is not int categoryId)
        {
            return Error.Validation("Vui lòng chọn danh mục.");
        }

        if (_vatRate.SelectedItem is not VatOption vat)
        {
            return Error.Validation("Vui lòng chọn thuế VAT.");
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
            return invalid;
        }

        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var saved = _product is null
            ? await products.CreateAsync(request)
            : await products.UpdateAsync(_product.Id, request);
        if (!saved.IsSuccess)
        {
            return Result.Failure(saved.Error);
        }

        if (saved.Value.IsActive != _isActive.Checked)
        {
            var statusResult = await products.SetActiveAsync(saved.Value.Id, _isActive.Checked);
            if (!statusResult.IsSuccess)
            {
                return statusResult;
            }
        }

        return Result.Success();
    });
}
