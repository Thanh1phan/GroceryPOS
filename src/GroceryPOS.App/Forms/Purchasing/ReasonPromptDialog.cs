using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Purchasing;

/// <summary>Asks for a short reason (with one-click suggestions), e.g. before cancelling a draft receipt.</summary>
internal sealed class ReasonPromptDialog : Form
{
    private readonly ModernTextBox _reason;

    public ReasonPromptDialog(string title, string message, IEnumerable<string> suggestions, bool required = false)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 230);
        Padding = new Padding(20);

        var caption = new Label { Dock = DockStyle.Top, Height = 44, Text = message };
        _reason = new ModernTextBox { Dock = DockStyle.Top, PlaceholderText = required ? "Lý do (bắt buộc)" : "Lý do (không bắt buộc)", MaxLength = 500 };
        var chips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        foreach (var suggestion in suggestions)
        {
            var chip = new ModernButton { Text = suggestion, Variant = ButtonVariant.Ghost, AutoSize = true, Height = 32, Margin = new Padding(0, 0, 6, 0) };
            chip.Click += (_, _) => _reason.Text = suggestion;
            chips.Controls.Add(chip);
        }

        var ok = new ModernButton { Text = "Xác nhận", Variant = ButtonVariant.Danger, Dock = DockStyle.Right, Width = 130 };
        ok.Click += (_, _) =>
        {
            if (required && string.IsNullOrWhiteSpace(_reason.Text))
            {
                _reason.SetError("Vui lòng nhập lý do.");
                return;
            }

            DialogResult = DialogResult.OK;
        };
        var cancel = new ModernButton { Text = "Quay lại", Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 110, DialogResult = DialogResult.Cancel };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0) };
        footer.Controls.AddRange([ok, new Panel { Dock = DockStyle.Right, Width = 8 }, cancel]);

        CancelButton = cancel;
        Controls.Add(chips);
        Controls.Add(_reason);
        Controls.Add(caption);
        Controls.Add(footer);
        Load += (_, _) => ThemeManager.Track(this);
        Shown += (_, _) => _reason.Focus();
    }

    public string? Reason => string.IsNullOrWhiteSpace(_reason.Text) ? null : _reason.Text.Trim();
}
