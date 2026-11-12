using GroceryPOS.Presentation.Catalog;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a product category – passive MVP view of <see cref="CategoryEditPresenter"/>.</summary>
internal sealed class CategoryEditForm : BaseEditDialog, ICategoryEditView
{
    private readonly CategoryEditPresenter _presenter;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;

    public CategoryEditForm(CategoryEditPresenter presenter)
        : base(presenter.Title)
    {
        _presenter = presenter;
        _name = AddField("Tên danh mục", new ModernTextBox { PlaceholderText = "Ví dụ: Sữa & chế phẩm", Text = presenter.InitialName });
        _description = AddField("Mô tả", new ModernTextBox { PlaceholderText = "Không bắt buộc", Text = presenter.InitialDescription });
        _presenter.Attach(this);
    }

    string ICategoryEditView.CategoryName => _name.Text;

    string ICategoryEditView.Description => _description.Text;

    bool IView.IsAlive => IsAlive;

    void ICategoryEditView.FocusName() => _name.Select();

    protected override Task<string?> SaveAsync() => _presenter.SaveAsync();

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);
}
