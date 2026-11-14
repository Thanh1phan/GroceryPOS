using GroceryPOS.Application.Users;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Users;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Add/edit dialog for a user account – passive MVP view of <see cref="UserEditPresenter"/>.</summary>
internal sealed class UserEditForm : BaseEditDialog, IUserEditView
{
    private const string RoleKey = "role";

    private readonly UserEditPresenter _presenter;
    private readonly ModernTextBox _username;
    private readonly ModernTextBox _fullName;
    private readonly ModernComboBox _role;
    private readonly ModernTextBox? _password;

    public UserEditForm(UserEditPresenter presenter)
        : base(presenter.Title, contentWidth: 416)
    {
        _presenter = presenter;
        var user = presenter.User;

        _username = AddField("Tên đăng nhập", new ModernTextBox
        {
            Text = user?.Username ?? string.Empty,
            ReadOnly = !presenter.IsNew,
            PlaceholderText = "vd: thungan2",
            IconGlyph = Glyphs.Contact,
            HelperText = presenter.UsernameHelperText,
        });
        _fullName = AddField("Họ tên", new ModernTextBox { Text = user?.FullName ?? string.Empty, PlaceholderText = "Họ và tên" });
        _role = AddField("Vai trò", new ModernComboBox
        {
            DisplayMember = nameof(RoleOptionDto.Name),
            ValueMember = nameof(RoleOptionDto.Id),
            Enabled = !presenter.RoleLocked,
            HelperText = presenter.RoleHelperText,
        }, trackAs: RoleKey);

        if (presenter.IsNew)
        {
            _password = AddField("Mật khẩu tạm", new ModernTextBox
            {
                Text = UserEditPresenter.NewTemporaryPassword(),
                IconGlyph = Glyphs.Lock,
                TrailingGlyph = Glyphs.Refresh,
                HelperText = "Mật khẩu tạm – người dùng phải đổi ở lần đăng nhập đầu tiên.",
            }, track: false);
            _password.TrailingIconClick += (_, _) => _password.Text = UserEditPresenter.NewTemporaryPassword();
        }

        _presenter.Attach(this);
        Load += async (_, _) => ShowFormError(await _presenter.LoadRolesAsync());
    }

    string IUserEditView.Username => _username.Text;

    string IUserEditView.FullName => _fullName.Text;

    int? IUserEditView.RoleId => _role.SelectedValue is int id ? id : null;

    string IUserEditView.Password => _password?.Text ?? string.Empty;

    bool IView.IsAlive => IsAlive;

    void IUserEditView.ShowRoles(IReadOnlyList<RoleOptionDto> roles, int? selectedRoleId)
    {
        _role.DataSource = roles.ToList();
        _role.SelectedValue = selectedRoleId;
        Changes.AcceptChange(RoleKey); // filling the list is not an edit by the user
    }

    void IUserEditView.FocusField(string field)
    {
        Control target = field switch
        {
            nameof(CreateUserRequest.Username) => _username,
            nameof(CreateUserRequest.RoleId) => _role,
            nameof(CreateUserRequest.Password) or nameof(CreateUserRequest.ConfirmPassword) => (Control?)_password ?? _fullName,
            _ => _fullName,
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
