using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Partners;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a supplier – passive MVP view of <see cref="SupplierEditPresenter"/>.</summary>
internal sealed class SupplierEditForm : BaseEditDialog, ISupplierEditView
{
    private const string CodeKey = "code";

    private readonly SupplierEditPresenter _presenter;
    private readonly ModernTextBox _code;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _contactPerson;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _taxCode;
    private readonly ModernTextBox _address;

    public SupplierEditForm(SupplierEditPresenter presenter)
        : base(presenter.Title, columns: 2, contentWidth: 496)
    {
        _presenter = presenter;
        var supplier = presenter.Supplier;

        _code = AddField("Mã nhà cung cấp", new ModernTextBox
        {
            Text = supplier?.Code ?? string.Empty,
            ReadOnly = presenter.CodeReadOnly,
            PlaceholderText = "Tự động nếu để trống",
        }, trackAs: CodeKey);
        _taxCode = AddField("Mã số thuế", new ModernTextBox { Text = supplier?.TaxCode ?? string.Empty, PlaceholderText = "0123456789" });
        _name = AddField("Tên nhà cung cấp", new ModernTextBox { Text = supplier?.Name ?? string.Empty, PlaceholderText = "Công ty / đại lý" }, fullWidth: true);
        _contactPerson = AddField("Người liên hệ", new ModernTextBox { Text = supplier?.ContactPerson ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        _phone = AddField("Số điện thoại", new ModernTextBox { Text = supplier?.Phone ?? string.Empty, PlaceholderText = "09xx xxx xxx", IconGlyph = Glyphs.Phone });
        _email = AddField("Email", new ModernTextBox { Text = supplier?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        AddField("Công nợ hiện tại", new ModernCurrencyBox { Value = presenter.DebtBalance, ReadOnly = true, TabStop = false }, track: false);
        _address = AddField("Địa chỉ", new ModernTextBox { Text = supplier?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" }, fullWidth: true);

        _presenter.Attach(this);
        if (presenter.IsNew)
        {
            Load += async (_, _) => await SuggestCodeAsync();
        }
    }

    string ISupplierEditView.Code => _code.Text;

    string ISupplierEditView.SupplierName => _name.Text;

    string ISupplierEditView.ContactPerson => _contactPerson.Text;

    string ISupplierEditView.Phone => _phone.Text;

    string ISupplierEditView.Email => _email.Text;

    string ISupplierEditView.Address => _address.Text;

    string ISupplierEditView.TaxCode => _taxCode.Text;

    bool IView.IsAlive => IsAlive;

    void ISupplierEditView.FocusField(string field)
    {
        var box = field switch
        {
            nameof(SaveSupplierRequest.Code) => _code,
            nameof(SaveSupplierRequest.ContactPerson) => _contactPerson,
            nameof(SaveSupplierRequest.Phone) => _phone,
            nameof(SaveSupplierRequest.Email) => _email,
            nameof(SaveSupplierRequest.Address) => _address,
            nameof(SaveSupplierRequest.TaxCode) => _taxCode,
            _ => _name,
        };
        box.Select();
    }

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    private async Task SuggestCodeAsync()
    {
        var code = await _presenter.SuggestCodeAsync();
        if (code is not null && IsAlive && string.IsNullOrEmpty(_code.Text))
        {
            _code.Text = code;
            Changes.AcceptChange(CodeKey); // a suggested code is not an edit by the user
        }
    }

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
