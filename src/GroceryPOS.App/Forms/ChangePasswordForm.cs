using GroceryPOS.Application.Authentication;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms;

/// <summary>Change-password dialog; shown when the current account must change its password, or from the user menu.</summary>
public sealed class ChangePasswordForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernTextBox _current;
    private readonly ModernTextBox _new;
    private readonly ModernTextBox _confirm;
    private readonly ModernButton _save;
    private readonly Label _error;

    public ChangePasswordForm(IServiceScopeFactory scopeFactory, bool mandatory)
    {
        _scopeFactory = scopeFactory;

        Text = "Đổi mật khẩu";
        ClientSize = new Size(380, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = !mandatory;
        StartPosition = FormStartPosition.CenterScreen;

        var hint = new Label
        {
            Text = mandatory
                ? "Bạn cần đổi mật khẩu trước khi tiếp tục."
                : "Nhập mật khẩu hiện tại và mật khẩu mới.",
            Font = ThemeFonts.Get(FontRole.Caption),
            AutoSize = true,
            MaximumSize = new Size(316, 0),
            Location = new Point(32, 24),
        };

        _current = new ModernTextBox { PlaceholderText = "Mật khẩu hiện tại", UseSystemPasswordChar = true, Location = new Point(32, 64), Width = 316 };
        _new = new ModernTextBox { PlaceholderText = "Mật khẩu mới", UseSystemPasswordChar = true, Location = new Point(32, 126), Width = 316 };
        _confirm = new ModernTextBox { PlaceholderText = "Xác nhận mật khẩu mới", UseSystemPasswordChar = true, Location = new Point(32, 188), Width = 316 };

        _error = new Label
        {
            ForeColor = Color.Firebrick,
            Location = new Point(32, 244),
            Size = new Size(316, 40),
            Visible = false,
        };

        _save = new ModernButton
        {
            Text = "Lưu mật khẩu",
            Variant = ButtonVariant.Primary,
            Location = new Point(32, 292),
            Size = new Size(316, 40),
        };
        _save.Click += async (_, _) => await SaveAsync();

        Controls.AddRange([hint, _current, _new, _confirm, _error, _save]);

        Load += (_, _) => ThemeManager.Track(this);
        FormClosing += (_, e) =>
        {
            if (mandatory && DialogResult != DialogResult.OK)
            {
                e.Cancel = true;
            }
        };
    }

    private async Task SaveAsync()
    {
        _error.Visible = false;
        if (_new.Text != _confirm.Text)
        {
            _error.Text = "Mật khẩu mới và xác nhận không khớp.";
            _error.Visible = true;
            return;
        }

        _save.Enabled = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var result = await auth.ChangePasswordAsync(_current.Text, _new.Text);
            if (result.IsSuccess)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _error.Text = result.Error.Message;
            _error.Visible = true;
        }
        finally
        {
            _save.Enabled = true;
        }
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // ChangePasswordForm
        // 
        ClientSize = new Size(284, 261);
        Name = "ChangePasswordForm";
        Text = "admin";
        ResumeLayout(false);

    }
}
