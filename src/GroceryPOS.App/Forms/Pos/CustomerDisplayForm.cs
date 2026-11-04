using GroceryPOS.Application.Sales;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.App.Forms.Pos;

/// <summary>
/// Màn hình khách: a second window facing the customer with the last scanned item, the newest lines and the amount
/// to pay in very large type. Opens full screen on a second monitor when there is one, otherwise as a normal
/// window the cashier can drag. Only renders <see cref="CustomerDisplayState"/>; it never takes focus from the counter.
/// </summary>
internal sealed class CustomerDisplayForm : Form, IThemeable
{
    private readonly Label _headline;
    private readonly Label _lastName;
    private readonly Label _lastDetail;
    private readonly ListView _lines;
    private readonly Label _items;
    private readonly Label _discount;
    private readonly Label _member;
    private readonly Label _amountCaption;
    private readonly Label _amount;
    private readonly Label _footer;
    private readonly Panel _amountPanel;

    public CustomerDisplayForm()
    {
        Text = "Màn hình khách – GroceryPOS";
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(640, 400);

        _headline = Make(FontRole.Headline, FontStyle.Bold, DockStyle.Top, 64, ContentAlignment.MiddleCenter);
        _lastName = Make(FontRole.Headline, FontStyle.Bold, DockStyle.Top, 60, ContentAlignment.MiddleLeft);
        _lastDetail = Make(FontRole.Title, FontStyle.Regular, DockStyle.Top, 40, ContentAlignment.MiddleLeft);

        _lines = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = false,
            BorderStyle = BorderStyle.None,
            Font = ThemeFonts.Get(FontRole.Subtitle),
            TabStop = false,
        };
        _lines.Columns.Add("Sản phẩm", 360);
        _lines.Columns.Add("SL × giá", 220);
        _lines.Columns.Add("Thành tiền", 160, HorizontalAlignment.Right);
        _lines.Resize += (_, _) => FitColumns();

        _items = Make(FontRole.Subtitle, FontStyle.Regular, DockStyle.Top, 30, ContentAlignment.MiddleRight);
        _discount = Make(FontRole.Subtitle, FontStyle.Bold, DockStyle.Top, 30, ContentAlignment.MiddleRight);
        _member = Make(FontRole.Subtitle, FontStyle.Regular, DockStyle.Top, 30, ContentAlignment.MiddleRight);
        _amountCaption = Make(FontRole.Title, FontStyle.Regular, DockStyle.Top, 40, ContentAlignment.MiddleRight);
        _amount = Make(FontRole.Display, FontStyle.Bold, DockStyle.Top, 96, ContentAlignment.MiddleRight);
        _amount.Font = new Font(ThemeFonts.Get(FontRole.Display).FontFamily, 54, FontStyle.Bold);
        _footer = Make(FontRole.Title, FontStyle.Regular, DockStyle.Top, 44, ContentAlignment.MiddleRight);

        _amountPanel = new Panel { Dock = DockStyle.Bottom, Height = 280, Padding = new Padding(32, 8, 32, 16) };
        _amountPanel.Controls.AddRange([_footer, _amount, _amountCaption, _member, _discount, _items]);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(32, 8, 32, 8) };
        body.Controls.Add(_lines);
        body.Controls.Add(_lastDetail);
        body.Controls.Add(_lastName);

        Controls.Add(body);
        Controls.Add(_amountPanel);
        Controls.Add(_headline);

        PlaceOnScreen();
        Load += (_, _) => ThemeManager.Track(this);
    }

    /// <summary>Never steal focus from the counter when shown or refreshed.</summary>
    protected override bool ShowWithoutActivation => true;

    public bool ThemesChildren => true;

    public void ApplyTheme(Theme theme)
    {
        var c = theme.Colors;
        BackColor = c.Background;
        foreach (var label in new[] { _lastName, _lastDetail, _items, _discount, _member, _amountCaption, _amount, _footer })
        {
            label.BackColor = Color.Transparent;
        }

        _lastName.ForeColor = c.TextPrimary;
        ForeColor = c.TextPrimary;
        _headline.BackColor = c.Primary;
        _headline.ForeColor = c.OnPrimary;
        _lines.BackColor = c.Background;
        _lines.ForeColor = c.TextPrimary;
        _lastDetail.ForeColor = c.TextSecondary;
        _items.ForeColor = c.TextSecondary;
        _member.ForeColor = c.TextSecondary;
        _discount.ForeColor = c.Success;
        _amountPanel.BackColor = c.Surface;
        _amountCaption.ForeColor = c.TextSecondary;
        _amount.ForeColor = c.Primary;
        _footer.ForeColor = c.TextSecondary;
    }

    public void Render(CustomerDisplayState state)
    {
        _headline.Text = state.Headline;
        var shopping = state.Mode == CustomerDisplayMode.Shopping;
        _lastName.Text = shopping ? state.LastItemName : state.Mode == CustomerDisplayMode.Welcome ? "Xin chào quý khách!" : string.Empty;
        _lastDetail.Text = shopping ? state.LastItemDetail : string.Empty;

        _lines.BeginUpdate();
        _lines.Items.Clear();
        foreach (var line in state.Lines)
        {
            _lines.Items.Add(new ListViewItem([line.Name, line.Detail, line.Amount]));
        }

        _lines.EndUpdate();

        _items.Text = state.ItemsText;
        _discount.Text = state.DiscountText ?? string.Empty;
        _member.Text = state.MemberText ?? string.Empty;
        _amountCaption.Text = state.AmountCaption;
        _amount.Text = state.Mode == CustomerDisplayMode.Welcome ? string.Empty : state.AmountText;
        _footer.Text = state.FooterText ?? string.Empty;
    }

    private void PlaceOnScreen()
    {
        var primary = Screen.PrimaryScreen;
        var second = Screen.AllScreens.FirstOrDefault(s => !s.Primary);
        if (second is not null)
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = second.Bounds;
            WindowState = FormWindowState.Maximized;
            return;
        }

        var area = primary?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(900, area.Width), Math.Min(600, area.Height));
        Location = new Point(area.Right - Width - 24, area.Top + 24);
    }

    private void FitColumns()
    {
        var width = _lines.ClientSize.Width;
        if (width <= 0)
        {
            return;
        }

        _lines.Columns[2].Width = Math.Max(140, width / 5);
        _lines.Columns[1].Width = Math.Max(180, width / 4);
        _lines.Columns[0].Width = Math.Max(120, width - _lines.Columns[1].Width - _lines.Columns[2].Width - 4);
    }

    private static Label Make(FontRole role, FontStyle style, DockStyle dock, int height, ContentAlignment align) => new()
    {
        Dock = dock,
        Height = height,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = align,
        Font = ThemeFonts.Get(role, style),
    };
}
