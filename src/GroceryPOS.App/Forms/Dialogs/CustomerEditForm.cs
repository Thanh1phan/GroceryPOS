using GroceryPOS.Application.Partners;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Partners;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a (loyalty) customer – passive MVP view of <see cref="CustomerEditPresenter"/>.</summary>
internal sealed class CustomerEditForm : BaseEditDialog, ICustomerEditView
{
    private readonly CustomerEditPresenter _presenter;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _email;
    private readonly ModernTextBox _address;

    public CustomerEditForm(CustomerEditPresenter presenter)
        : base(presenter.Title, columns: 2, contentWidth: 496)
    {
        _presenter = presenter;
        var customer = presenter.Customer;

        _name = AddField("Tên khách hàng", new ModernTextBox { Text = customer?.Name ?? string.Empty, PlaceholderText = "Họ và tên" }, fullWidth: true);
        _phone = AddField("Số điện thoại", new ModernTextBox
        {
            Text = customer?.Phone ?? string.Empty,
            PlaceholderText = "09xx xxx xxx",
            IconGlyph = Glyphs.Phone,
            HelperText = "Dùng để tìm khách khi bán hàng",
        });
        _email = AddField("Email", new ModernTextBox { Text = customer?.Email ?? string.Empty, PlaceholderText = "Không bắt buộc" });
        _address = AddField("Địa chỉ", new ModernTextBox { Text = customer?.Address ?? string.Empty, PlaceholderText = "Không bắt buộc" }, fullWidth: true);

        if (presenter.ShowsLoyalty && customer is not null)
        {
            AddField("Điểm tích lũy", new ModernNumericBox { Value = customer.LoyaltyPoints, ReadOnly = true, TabStop = false, SuffixText = "điểm" }, track: false);
            AddField("Tổng chi tiêu", new ModernCurrencyBox { Value = customer.TotalSpent, ReadOnly = true, TabStop = false }, track: false);
        }

        _presenter.Attach(this);
    }

    string ICustomerEditView.CustomerName => _name.Text;

    string ICustomerEditView.Phone => _phone.Text;

    string ICustomerEditView.Email => _email.Text;

    string ICustomerEditView.Address => _address.Text;

    bool IView.IsAlive => IsAlive;

    void ICustomerEditView.FocusField(string field)
    {
        var box = field switch
        {
            nameof(SaveCustomerRequest.Phone) => _phone,
            nameof(SaveCustomerRequest.Email) => _email,
            nameof(SaveCustomerRequest.Address) => _address,
            _ => _name,
        };
        box.Select();
    }

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    bool IView.Confirm(string message, string title, string confirmText) => Confirm(message, title, confirmText);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
