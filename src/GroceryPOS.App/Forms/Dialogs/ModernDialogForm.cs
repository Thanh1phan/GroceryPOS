using GroceryPOS.Application.Common;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Shared chrome for the add/edit dialogs: fixed-size window, theme tracking, an inline error label and a
/// save helper that disables the button, awaits, and surfaces <see cref="Error.Message"/> on failure.
/// Derived forms still lay out their own fields (same hand-written style as <c>LoginForm</c>/<c>ChangePasswordForm</c>).
/// </summary>
internal abstract class ModernDialogForm : Form
{
    private readonly Label _error;

    protected ModernDialogForm()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _error = new Label
        {
            ForeColor = Color.Firebrick,
            AutoSize = false,
            Visible = false,
        };
        Controls.Add(_error);

        Load += (_, _) => ThemeManager.Track(this);
    }

    protected Label ErrorLabel => _error;

    protected async Task RunSaveAsync(ModernButton saveButton, Func<Task<Result>> action)
    {
        _error.Visible = false;
        saveButton.Enabled = false;
        try
        {
            var result = await action();
            if (result.IsSuccess)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            _error.Text = result.Error.Message;
            _error.Visible = true;
        }
        finally
        {
            saveButton.Enabled = true;
        }
    }
}
