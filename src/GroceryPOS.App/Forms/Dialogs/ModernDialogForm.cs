using GroceryPOS.Application.Common;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Forms;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>
/// Shared chrome for the hand-laid-out dialogs: fixed-size window, theme tracking (via <see cref="BaseForm"/>), an inline error label and a
/// save helper that disables the button, awaits, and surfaces <see cref="Error.Message"/> on failure.
/// Derived forms still lay out their own fields; new add/edit dialogs should derive from <see cref="BaseEditDialog"/> instead,
/// which lays the fields out, adds Hủy/Lưu and guards unsaved changes.
/// </summary>
internal abstract class ModernDialogForm : BaseForm
{
    private readonly Label _error;

    protected ModernDialogForm()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = false; // keep the original key routing of these dialogs

        _error = new Label
        {
            ForeColor = Color.Firebrick,
            AutoSize = false,
            Visible = false,
        };
        Controls.Add(_error);
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
