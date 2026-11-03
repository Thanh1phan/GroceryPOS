using System.ComponentModel;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Feedback.Core;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Feedback;

/// <summary>
/// Themed replacement for <see cref="MessageBox"/>: icon by kind, title, wrapped message, optional "Chi tiết"
/// section, Vietnamese buttons, Enter = default button, Esc = safe answer, letter keys press buttons
/// (C/Y = Có, K/N = Không…), Ctrl+C copies the text. The owner window is dimmed while it is open.
/// </summary>
/// <example><code>
/// ModernDialog.Warning(this, result.Error.Message, "Khách hàng");
/// if (!ModernDialog.Confirm(this, $"Xóa vai trò \"{name}\"?", "Phân quyền", "Xóa vai trò", danger: true)) return;
/// </code></example>
public static class ModernDialog
{
    /// <summary>Shows <paramref name="spec"/> and returns the chosen answer.</summary>
    public static DialogChoice Show(IWin32Window? owner, DialogSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (owner is Control { InvokeRequired: true } control)
        {
            return (DialogChoice)control.Invoke(() => Show(owner, spec));
        }

        using var form = new MessageDialogForm(spec);
        ModalScrim.ShowDialog(owner, form);
        return form.Choice;
    }

    /// <summary>Drop-in for <see cref="MessageBox.Show(IWin32Window, string, string, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton)"/>.</summary>
    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        var spec = DialogSpec.Standard(KindOf(icon, buttons), caption, text, SetOf(buttons), DefaultIndexOf(defaultButton));
        return ToDialogResult(Show(owner, spec));
    }

    /// <summary>Drop-in for <see cref="MessageBox.Show(string, string, MessageBoxButtons, MessageBoxIcon)"/> (no owner).</summary>
    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) =>
        Show(null, text, caption, buttons, icon);

    public static void Info(IWin32Window? owner, string message, string title = "Thông báo", string? details = null) =>
        Show(owner, DialogSpec.Alert(FeedbackKind.Info, title, message, details));

    public static void Success(IWin32Window? owner, string message, string title = "Thành công", string? details = null) =>
        Show(owner, DialogSpec.Alert(FeedbackKind.Success, title, message, details));

    public static void Warning(IWin32Window? owner, string message, string title = "Cảnh báo", string? details = null) =>
        Show(owner, DialogSpec.Alert(FeedbackKind.Warning, title, message, details));

    public static void Error(IWin32Window? owner, string message, string title = "Lỗi", string? details = null) =>
        Show(owner, DialogSpec.Alert(FeedbackKind.Error, title, message, details));

    /// <summary>
    /// Yes/no question with action wording. With <paramref name="danger"/> the confirm button is red and
    /// Enter answers "no". Returns true only when the user confirmed.
    /// </summary>
    public static bool Confirm(IWin32Window? owner, string message, string title, string confirmText = "Đồng ý", string cancelText = "Hủy", bool danger = false) =>
        Show(owner, DialogSpec.Confirm(title, message, confirmText, cancelText, danger)) == DialogChoice.Yes;

    public static DialogResult ToDialogResult(DialogChoice choice) => choice switch
    {
        DialogChoice.Ok => DialogResult.OK,
        DialogChoice.Cancel => DialogResult.Cancel,
        DialogChoice.Yes => DialogResult.Yes,
        DialogChoice.No => DialogResult.No,
        DialogChoice.Retry => DialogResult.Retry,
        DialogChoice.Abort => DialogResult.Abort,
        DialogChoice.Ignore => DialogResult.Ignore,
        _ => DialogResult.None,
    };

    private static DialogButtonSet SetOf(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OKCancel => DialogButtonSet.OkCancel,
        MessageBoxButtons.YesNo => DialogButtonSet.YesNo,
        MessageBoxButtons.YesNoCancel => DialogButtonSet.YesNoCancel,
        MessageBoxButtons.RetryCancel => DialogButtonSet.RetryCancel,
        MessageBoxButtons.AbortRetryIgnore => DialogButtonSet.AbortRetryIgnore,
        _ => DialogButtonSet.Ok,
    };

    private static int DefaultIndexOf(MessageBoxDefaultButton button) => button switch
    {
        MessageBoxDefaultButton.Button2 => 1,
        MessageBoxDefaultButton.Button3 => 2,
        MessageBoxDefaultButton.Button4 => 3,
        _ => 0,
    };

    /// <remarks>MessageBoxIcon has aliases (Hand = Stop = Error, Exclamation = Warning, Asterisk = Information).</remarks>
    private static FeedbackKind KindOf(MessageBoxIcon icon, MessageBoxButtons buttons) => icon switch
    {
        MessageBoxIcon.Error => FeedbackKind.Error,
        MessageBoxIcon.Warning => FeedbackKind.Warning,
        MessageBoxIcon.Question => FeedbackKind.Question,
        MessageBoxIcon.Information => FeedbackKind.Info,
        _ => buttons is MessageBoxButtons.YesNo or MessageBoxButtons.YesNoCancel ? FeedbackKind.Question : FeedbackKind.Info,
    };

    /// <summary>The dialog window itself: borderless, painted header/footer, draggable by its body.</summary>
    [DesignerCategory("Code")]
    private sealed class MessageDialogForm : Form
    {
        private const int WmNcHitTest = 0x84;
        private const int HtCaption = 2;
        private const int MinWidth = 380;
        private const int MaxWidth = 560;
        private const int IconDesign = 40;
        private const int PadDesign = 24;

        private readonly DialogSpec _spec;
        private readonly Label _title;
        private readonly Label _message;
        private readonly TextBox? _details;
        private readonly ModernButton? _detailsToggle;
        private readonly FlowLayoutPanel _buttonBar;
        private readonly Dictionary<DialogChoice, ModernButton> _buttons = new();

        public MessageDialogForm(DialogSpec spec)
        {
            _spec = spec;
            Choice = spec.CancelChoice;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MaximizeBox = MinimizeBox = false;
            KeyPreview = true;
            Text = spec.Title;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = ThemeFonts.Get(FontRole.Body);
            AccessibleRole = AccessibleRole.Dialog;
            AccessibleName = spec.Title;
            AccessibleDescription = spec.Message;

            _title = new Label
            {
                Text = spec.Title,
                AutoSize = true,
                UseMnemonic = false,
                Font = ThemeFonts.Get(FontRole.Subtitle, FontStyle.Bold),
            };
            _message = new Label
            {
                Text = spec.Message,
                AutoSize = true,
                UseMnemonic = false,
                Font = ThemeFonts.Get(FontRole.Body),
            };
            Controls.Add(_title);
            Controls.Add(_message);

            if (spec.Details is not null)
            {
                _detailsToggle = new ModernButton
                {
                    Text = "Chi tiết",
                    Variant = ButtonVariant.Ghost,
                    IconGlyph = Icons.Glyphs.ChevronDown,
                    AutoSize = false,
                    TabStop = true,
                };
                _detailsToggle.Click += (_, _) => ToggleDetails();
                _details = new TextBox
                {
                    Text = spec.Details.ReplaceLineEndings("\r\n"),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    WordWrap = true,
                    Visible = false,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font(FontFamily.GenericMonospace, 8.25f),
                };
                Controls.Add(_detailsToggle);
                Controls.Add(_details);
            }

            _buttonBar = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
            };
            // RightToLeft flow: add in reverse so the first spec button ends up leftmost.
            foreach (var buttonSpec in spec.Buttons.Reverse())
            {
                var button = new ModernButton
                {
                    Text = buttonSpec.Text,
                    Variant = buttonSpec.Style switch
                    {
                        DialogButtonStyle.Danger => ButtonVariant.Danger,
                        DialogButtonStyle.Primary => ButtonVariant.Primary,
                        _ => ButtonVariant.Secondary,
                    },
                    AutoSize = false,
                    UseMnemonic = false,
                    Margin = new Padding(8, 0, 0, 0),
                };
                var choice = buttonSpec.Choice;
                button.Click += (_, _) => Finish(choice);
                _buttons[choice] = button;
                _buttonBar.Controls.Add(button);
            }

            Controls.Add(_buttonBar);
            AcceptButton = _buttons[spec.DefaultChoice];
            CancelButton = _buttons[spec.CancelChoice];
        }

        public DialogChoice Choice { get; private set; }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CsDropShadow;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyColors();
            LayoutDialog();
            ActiveControl = _buttons[_spec.DefaultChoice];
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_spec.Kind is FeedbackKind.Error or FeedbackKind.Danger)
            {
                System.Media.SystemSounds.Hand.Play();
            }
            else if (_spec.Kind == FeedbackKind.Warning)
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C && !(_details?.Focused == true && _details.SelectionLength > 0))
            {
                try
                {
                    Clipboard.SetText(_spec.ToClipboardText());
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // Clipboard busy (another app holds it) – copying is a convenience, ignore.
                }

                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_details?.Focused == true || char.IsControl(e.KeyChar))
            {
                return;
            }

            if (_spec.ChoiceForKey(e.KeyChar) is { } choice)
            {
                e.Handled = true;
                Finish(choice);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Borderless: let the user drag the dialog by any empty area.
            if (m.Msg == WmNcHitTest && (int)m.Result == 1)
            {
                m.Result = HtCaption;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var theme = ThemeManager.Current;
            var colors = FeedbackPalette.Resolve(theme, _spec.Kind);
            g.UseHighQuality();

            // Footer band behind the buttons.
            var footerTop = _buttonBar.Top - this.ScaleDpi(16);
            using (var footer = new SolidBrush(theme.Colors.SurfaceAlt))
            {
                g.FillRectangle(footer, 0, footerTop, Width, Height - footerTop);
            }

            using (var line = new Pen(theme.Colors.Border))
            {
                g.DrawLine(line, 0, footerTop, Width, footerTop);
            }

            // Accent strip on top and icon.
            using (var accent = new SolidBrush(colors.Accent))
            {
                g.FillRectangle(accent, 0, 0, Width, this.ScaleDpi(4));
            }

            var icon = IconBounds;
            g.FillRoundedRectangle(colors.IconBackground, icon, icon.Width / 2f);
            TextRenderer.DrawText(g, FeedbackGlyphs.For(_spec.Kind), ThemeFonts.Icon(15f), icon, colors.IconForeground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 1 px window border (borderless forms have none).
            using var border = new Pen(theme.IsDark ? ColorMath.Lighten(theme.Colors.Border, 0.1) : theme.Colors.Border);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        private Rectangle IconBounds
        {
            get
            {
                var pad = this.ScaleDpi(PadDesign);
                var size = this.ScaleDpi(IconDesign);
                return new Rectangle(pad, pad + this.ScaleDpi(4), size, size);
            }
        }

        private void Finish(DialogChoice choice)
        {
            Choice = choice;
            var result = ToDialogResult(choice);
            DialogResult = result == DialogResult.None ? DialogResult.Cancel : result;
            Close();
        }

        private void ToggleDetails()
        {
            if (_details is null || _detailsToggle is null)
            {
                return;
            }

            _details.Visible = !_details.Visible;
            _detailsToggle.IconGlyph = _details.Visible ? Icons.Glyphs.ChevronUp : Icons.Glyphs.ChevronDown;
            _detailsToggle.Text = _details.Visible ? "Ẩn chi tiết" : "Chi tiết";
            var center = new Point(Left + (Width / 2), Top + (Height / 2));
            LayoutDialog();
            Location = new Point(center.X - (Width / 2), Math.Max(Screen.FromControl(this).WorkingArea.Top, center.Y - (Height / 2)));
        }

        /// <summary>Sizes the window to its text: as narrow as possible between 380 and 560 px, as tall as needed.</summary>
        private void LayoutDialog()
        {
            var pad = this.ScaleDpi(PadDesign);
            var icon = IconBounds;
            var textLeft = icon.Right + this.ScaleDpi(16);
            var minWidth = this.ScaleDpi(MinWidth);
            var maxWidth = Math.Min(this.ScaleDpi(MaxWidth), Screen.FromControl(this).WorkingArea.Width - this.ScaleDpi(40));

            var oneLine = Math.Max(
                TextRenderer.MeasureText(_spec.Message, _message.Font, Size.Empty, TextFormatFlags.NoPrefix).Width,
                TextRenderer.MeasureText(_spec.Title, _title.Font, Size.Empty, TextFormatFlags.NoPrefix).Width);
            var buttonHeight = this.ScaleDpi(ThemeManager.Current.Spacing.ControlHeight);
            var buttonsWidth = 0;
            foreach (var button in _buttons.Values)
            {
                var width = Math.Max(this.ScaleDpi(96), TextRenderer.MeasureText(button.Text, button.Font).Width + this.ScaleDpi(32));
                button.Size = new Size(width, buttonHeight);
                buttonsWidth += width + button.Margin.Horizontal;
            }

            var dialogWidth = Math.Clamp(Math.Max(textLeft + oneLine + pad + this.ScaleDpi(4), buttonsWidth + (2 * pad)), minWidth, Math.Max(minWidth, maxWidth));
            var textWidth = dialogWidth - textLeft - pad;

            _title.MaximumSize = new Size(textWidth, 0);
            _title.Location = new Point(textLeft, icon.Top + this.ScaleDpi(2));
            _message.MaximumSize = new Size(textWidth, 0);
            _message.Location = new Point(textLeft, _title.Bottom + this.ScaleDpi(8));
            var y = Math.Max(_message.Bottom, icon.Bottom);

            if (_detailsToggle is not null && _details is not null)
            {
                _detailsToggle.Size = new Size(this.ScaleDpi(120), this.ScaleDpi(30));
                _detailsToggle.Location = new Point(textLeft - this.ScaleDpi(8), y + this.ScaleDpi(8));
                y = _detailsToggle.Bottom;
                if (_details.Visible)
                {
                    _details.Bounds = new Rectangle(pad, y + this.ScaleDpi(6), dialogWidth - (2 * pad), this.ScaleDpi(140));
                    y = _details.Bottom;
                }
            }

            var footerTop = y + pad;
            _buttonBar.Bounds = new Rectangle(pad, footerTop + this.ScaleDpi(16), dialogWidth - (2 * pad), buttonHeight);
            ClientSize = new Size(dialogWidth, _buttonBar.Bottom + this.ScaleDpi(16));
            Invalidate();
        }

        private void ApplyColors()
        {
            var theme = ThemeManager.Current;
            var colors = FeedbackPalette.Resolve(theme, _spec.Kind);
            BackColor = colors.Background;
            ForeColor = colors.Title;
            _title.ForeColor = colors.Title;
            _message.ForeColor = colors.Text;
            _title.BackColor = _message.BackColor = colors.Background;
            if (_details is not null)
            {
                _details.BackColor = theme.Colors.SurfaceAlt;
                _details.ForeColor = theme.Colors.TextPrimary;
            }

            if (_detailsToggle is not null)
            {
                _detailsToggle.BackColor = colors.Background;
            }

            _buttonBar.BackColor = theme.Colors.SurfaceAlt;
            foreach (var button in _buttons.Values)
            {
                button.BackColor = theme.Colors.SurfaceAlt;
                button.Invalidate();
            }

            Invalidate(true);
        }

        private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => ApplyColors();
    }
}
