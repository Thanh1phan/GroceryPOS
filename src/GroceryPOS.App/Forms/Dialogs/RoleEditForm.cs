using GroceryPOS.Application.Users;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Users;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Add/edit dialog for a role's name and description – passive MVP view of <see cref="RoleEditPresenter"/>.
/// A new role can start from another role's permissions ("Sao chép quyền từ"); permissions themselves are edited in
/// the matrix. System role names are read-only.
/// </summary>
internal sealed class RoleEditForm : BaseEditDialog, IRoleEditView
{
    private readonly RoleEditPresenter _presenter;
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _description;
    private readonly ModernComboBox? _copyFrom;

    public RoleEditForm(RoleEditPresenter presenter)
        : base(presenter.Title, contentWidth: 396)
    {
        _presenter = presenter;
        var role = presenter.Role;

        _name = AddField("Tên vai trò", new ModernTextBox
        {
            Text = role?.Name ?? string.Empty,
            ReadOnly = presenter.NameLocked,
            PlaceholderText = "vd: Kế toán",
            HelperText = presenter.NameHelperText,
        });
        _description = AddField("Mô tả", new ModernTextBox { Text = role?.Description ?? string.Empty, PlaceholderText = "Không bắt buộc" });

        if (presenter.IsNew)
        {
            _copyFrom = AddField("Sao chép quyền từ", new ModernComboBox
            {
                DataSource = presenter.CopySources.ToList(),
                DisplayMember = nameof(RoleCopySource.Name),
                ValueMember = nameof(RoleCopySource.Id),
                HelperText = "Quyền chi tiết chỉnh trong bảng phân quyền sau khi tạo.",
            });
        }

        _presenter.Attach(this);
    }

    string IRoleEditView.RoleName => _name.Text;

    string IRoleEditView.Description => _description.Text;

    int? IRoleEditView.CopyFromRoleId => _copyFrom?.SelectedValue is int id ? id : null;

    bool IView.IsAlive => IsAlive;

    void IRoleEditView.FocusField(string field)
    {
        Control target = field == nameof(SaveRoleRequest.Description) ? _description : _name;
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
