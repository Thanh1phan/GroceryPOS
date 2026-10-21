using GroceryPOS.Application.Authentication;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms;

/// <summary>Sign-in screen. On success the session is already stored in <c>IUserSessionStore</c>; the caller reads it from there.</summary>
public sealed class LoginForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ModernTextBox _username;
    private readonly ModernTextBox _password;
    private readonly ModernButton _signIn;
    private readonly Label _error;

    public LoginForm(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;

        Text = "GroceryPOS - Đăng nhập";
        ClientSize = new Size(380, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            Text = "GroceryPOS",
            Font = ThemeFonts.Get(FontRole.Headline, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(32, 36),
        };

        var subtitle = new Label
        {
            Text = "Đăng nhập để tiếp tục",
            Font = ThemeFonts.Get(FontRole.Caption),
            AutoSize = true,
            Location = new Point(32, 72),
        };

        _username = new ModernTextBox
        {
            PlaceholderText = "Tên đăng nhập",
            Location = new Point(32, 108),
            Width = 316,
        };

        _password = new ModernTextBox
        {
            PlaceholderText = "Mật khẩu",
            UseSystemPasswordChar = true,
            Location = new Point(32, 170),
            Width = 316,
        };

        _error = new Label
        {
            ForeColor = Color.Firebrick,
            Location = new Point(32, 226),
            Size = new Size(316, 40),
            Visible = false,
        };

        _signIn = new ModernButton
        {
            Text = "Đăng nhập",
            Variant = ButtonVariant.Primary,
            Location = new Point(32, 272),
            Size = new Size(316, 40),
        };
        _signIn.Click += async (_, _) => await SignInAsync();

        _username.EnterPressed += (_, _) => _password.Focus();
        _password.EnterPressed += async (_, _) => await SignInAsync();

        Controls.AddRange([title, subtitle, _username, _password, _error, _signIn]);
        AcceptButton = null;

        Load += (_, _) => ThemeManager.Track(this);
    }

    private async Task SignInAsync()
    {
        _error.Visible = false;
        _signIn.Enabled = false;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var result = await auth.LoginAsync(_username.Text, _password.Text);
            if (result.IsSuccess)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            _error.Text = result.Error.Message;
            _error.Visible = true;
            _password.Clear();
            _password.Focus();
        }
        finally
        {
            _signIn.Enabled = true;
        }
    }
}
