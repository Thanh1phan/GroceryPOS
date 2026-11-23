using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Application.Validation;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.Presentation.Settings;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Navigation;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Settings;

/// <summary>
/// Cấu hình (<c>settings.manage</c>): store identity, receipt footer and paper, loyalty policy and sales rules – shared by
/// every counter through the database – plus this machine's light/dark theme. Fields are validated as they are typed, the
/// receipt on the right is re-laid out live with the edited values, and Ctrl+S saves after listing what will change.
/// Passive MVP view – the logic lives in <see cref="SettingsPresenter"/>; only the per-machine theme is handled here.
/// </summary>
internal sealed class SettingsScreen : BaseUserControl, ISettingsView
{
    private const int Col = 8;
    private const int FullWidth = 560;
    private const int HalfWidth = 272;
    private const int RightColumn = Col + HalfWidth + 16;

    private readonly SettingsPresenter _presenter;
    private readonly ModernTabControl _tabs;

    private readonly ModernBadge _status;
    private readonly ModernButton _save;
    private readonly ModernButton _undo;
    private readonly ModernButton _defaults;

    // Cửa hàng
    private readonly ModernTextBox _name;
    private readonly ModernTextBox _address;
    private readonly ModernTextBox _phone;
    private readonly ModernTextBox _taxCode;

    // Hóa đơn
    private readonly TextBox _footer;
    private readonly ToneLabel _footerInfo;
    private readonly RadioButton _paper80;
    private readonly RadioButton _paper58;
    private readonly CheckBox _previewAfterCheckout;

    // Tích điểm
    private readonly ModernCurrencyBox _vndPerPoint;
    private readonly ModernCurrencyBox _pointValue;
    private readonly ToneLabel _loyaltySummary;

    // Bán hàng
    private readonly ModernNumericBox _returnDays;
    private readonly CheckBox _allowNegativeStock;
    private readonly CheckBox _requireOpenShift;
    private readonly ModernCurrencyBox _varianceThreshold;

    // Giao diện (per machine)
    private readonly RadioButton _themeLight;
    private readonly RadioButton _themeDark;

    private readonly ToneLabel _warnings;
    private readonly TextBox _receiptPreview;
    private readonly Dictionary<string, ModernTextBox> _fieldBoxes;

    private bool _populating;

    public SettingsScreen(IServiceProvider services)
    {
        _presenter = services.GetRequiredService<SettingsPresenter>();

        // ---- Header -------------------------------------------------------------------------------
        var title = new Label { Text = "Cấu hình cửa hàng", AutoSize = true, Location = new Point(0, 2), Font = new Font(Font.FontFamily, 14, FontStyle.Bold) };
        var subtitle = new ToneLabel
        {
            Text = "Áp dụng cho mọi quầy dùng chung cơ sở dữ liệu; các màn hình mở sau khi lưu dùng giá trị mới.",
            AutoSize = true,
            Location = new Point(0, 34),
            Tone = LabelTone.Muted,
        };
        _status = new ModernBadge { Location = new Point(230, 8) };

        _save = HeaderButton("Lưu thay đổi (Ctrl+S)", Glyphs.Save, ButtonVariant.Primary, 190);
        _save.Click += async (_, _) => await _presenter.SaveAsync();
        _undo = HeaderButton("Hoàn tác", Glyphs.Refresh, ButtonVariant.Secondary, 120);
        _undo.Click += (_, _) => _presenter.Undo();
        _defaults = HeaderButton("Khôi phục mặc định", Glyphs.History, ButtonVariant.Ghost, 170);
        _defaults.Click += async (_, _) => await _presenter.RestoreDefaultsAsync();

        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        headerActions.Controls.AddRange([_defaults, _undo, _save]);

        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new Padding(24, 20, 24, 0) };
        var headerText = new Panel { Dock = DockStyle.Fill };
        headerText.Controls.AddRange([title, _status, subtitle]);
        header.Controls.Add(headerText);
        header.Controls.Add(headerActions);

        // ---- Cửa hàng -----------------------------------------------------------------------------
        var storePage = Page("Cửa hàng", Glyphs.Shop, out var store);
        _name = Box(store, "Tên cửa hàng", Col, 8, FullWidth, "Tạp hóa Cô Ba");
        _address = Box(store, "Địa chỉ", Col, 88, FullWidth, "Số nhà, đường, phường/xã, quận/huyện, tỉnh/thành phố");
        _phone = Box(store, "Điện thoại", Col, 168, HalfWidth, "0909 000 000");
        _phone.IconGlyph = Glyphs.Phone;
        _taxCode = Box(store, "Mã số thuế (nếu có)", RightColumn, 168, HalfWidth, "0123456789");
        Hint(store, "Tên, địa chỉ, điện thoại và mã số thuế được in ở đầu hóa đơn, phiếu trả hàng, báo cáo ca và các file Excel xuất ra.", Col, 252, FullWidth);

        // ---- Hóa đơn ------------------------------------------------------------------------------
        var receiptPage = Page("Hóa đơn", Glyphs.Print, out var receipt);
        Caption(receipt, "Lời cảm ơn cuối hóa đơn (mỗi dòng in giữa khổ giấy)", Col, 8);
        _footer = new TextBox
        {
            Location = new Point(Col, 30),
            Size = new Size(FullWidth, 110),
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = StoreSettingsValidator.MaxFooterLength + 50,
            Font = new Font("Consolas", 10f),
        };
        _footerInfo = new ToneLabel { AutoSize = true, Location = new Point(Col, 146), Tone = LabelTone.Muted };

        Caption(receipt, "Khổ giấy in hóa đơn", Col, 184);
        _paper80 = new RadioButton { Text = "80mm (48 ký tự/dòng)", AutoSize = true, Location = new Point(Col, 208) };
        _paper58 = new RadioButton { Text = "58mm (32 ký tự/dòng)", AutoSize = true, Location = new Point(Col + 220, 208) };
        _previewAfterCheckout = new CheckBox { Text = "Tự động hiện bản xem trước hóa đơn sau mỗi lần thanh toán", AutoSize = true, Location = new Point(Col, 252) };
        Hint(receipt, "Khi tắt, quầy bán hàng chuyển ngay sang khách tiếp theo; vẫn in lại được hóa đơn bất kỳ bằng Ctrl+P.", Col, 278, FullWidth);
        receipt.Controls.AddRange([_footer, _footerInfo, _paper80, _paper58, _previewAfterCheckout]);

        // ---- Tích điểm ----------------------------------------------------------------------------
        var loyaltyPage = Page("Tích điểm", Glyphs.People, out var loyalty);
        _vndPerPoint = Money(loyalty, "Số tiền mua để được 1 điểm", Col, 8, StoreSettingsValidator.MaxVndPerPoint);
        _vndPerPoint.HelperText = "Nhập 0 để tắt cộng điểm.";
        _pointValue = Money(loyalty, "Giá trị 1 điểm khi đổi (giảm giá)", RightColumn, 8, StoreSettingsValidator.MaxVndPerPoint);
        _pointValue.HelperText = "Nhập 0 để không cho đổi điểm.";
        _loyaltySummary = new ToneLabel { AutoSize = false, Location = new Point(Col, 100), Size = new Size(FullWidth, 72), Tone = LabelTone.Primary };
        Hint(loyalty, "Thay đổi áp dụng cho các hóa đơn mới; điểm khách đang có được giữ nguyên. Hủy hóa đơn và trả hàng vẫn trừ lại đúng số điểm đã cộng lúc mua.", Col, 180, FullWidth);
        loyalty.Controls.Add(_loyaltySummary);

        // ---- Bán hàng -----------------------------------------------------------------------------
        var salesPage = Page("Bán hàng", Glyphs.Cart, out var sales);
        Caption(sales, "Hạn đổi trả hàng", Col, 8);
        _returnDays = new ModernNumericBox
        {
            Location = new Point(Col, 30),
            Width = 160,
            DecimalPlaces = 0,
            Minimum = 0,
            Maximum = StoreSettingsValidator.MaxReturnWindowDays,
            SuffixText = "ngày",
        };
        Hint(sales, "Thu ngân nhận trả hàng trong số ngày này kể từ ngày bán; hóa đơn cũ hơn cần quyền \"Hủy hóa đơn\" (quản lý).", Col + 176, 34, FullWidth - 176);
        _varianceThreshold = Money(sales, "Lệch két từ mức này phải ghi chú khi kết ca", Col, 100, StoreSettingsValidator.MaxVarianceThreshold);
        _requireOpenShift = new CheckBox { Text = "Bắt buộc mở ca trước khi bán (tiền bán hàng được đối chiếu với két khi kết ca)", AutoSize = true, Location = new Point(Col, 196) };
        _allowNegativeStock = new CheckBox { Text = "Cho bán khi tồn kho không đủ (nhập phiếu nhập hàng sau, tồn kho tạm âm)", AutoSize = true, Location = new Point(Col, 228) };
        sales.Controls.AddRange([_returnDays, _requireOpenShift, _allowNegativeStock]);

        // ---- Giao diện ----------------------------------------------------------------------------
        var themePage = Page("Giao diện", Glyphs.Sun, out var theme);
        Caption(theme, "Giao diện trên máy này", Col, 8);
        _themeLight = new RadioButton { Text = "Sáng", AutoSize = true, Location = new Point(Col, 34) };
        _themeDark = new RadioButton { Text = "Tối", AutoSize = true, Location = new Point(Col + 120, 34) };
        Hint(theme, "Áp dụng ngay và chỉ cho máy này (không cần bấm Lưu); cũng đổi được bằng nút mặt trời/mặt trăng trên thanh trên cùng.", Col, 70, FullWidth);
        theme.Controls.AddRange([_themeLight, _themeDark]);
        SyncThemeRadios();
        _themeLight.CheckedChanged += (_, _) => { if (_themeLight.Checked) ThemeManager.SetMode(ThemeMode.Light); };
        _themeDark.CheckedChanged += (_, _) => { if (_themeDark.Checked) ThemeManager.SetMode(ThemeMode.Dark); };
        ThemeManager.ThemeChanged += OnThemeChanged;

        _tabs = new ModernTabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(storePage);
        _tabs.TabPages.Add(receiptPage);
        _tabs.TabPages.Add(loyaltyPage);
        _tabs.TabPages.Add(salesPage);
        _tabs.TabPages.Add(themePage);

        _warnings = new ToneLabel { Dock = DockStyle.Bottom, Height = 84, Tone = LabelTone.Warning, Padding = new Padding(4, 8, 0, 0) };

        var formCard = new ModernCard { Dock = DockStyle.Fill, Title = "Thiết lập", HeaderGlyph = Glyphs.Settings };
        formCard.Controls.Add(_tabs);
        formCard.Controls.Add(_warnings);

        // ---- Receipt preview ----------------------------------------------------------------------
        _receiptPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
        };
        var printPreview = new ModernButton { Text = "Xem bản in…", Variant = ButtonVariant.Secondary, IconGlyph = Glyphs.Print, Dock = DockStyle.Bottom, Height = 40 };
        printPreview.Click += (_, _) => _presenter.PrintPreview();
        var previewCard = new ModernCard { Dock = DockStyle.Fill, Title = "Xem trước hóa đơn", Subtitle = "Hóa đơn mẫu với thông tin đang sửa", HeaderGlyph = Glyphs.Document };
        previewCard.Controls.Add(_receiptPreview);
        previewCard.Controls.Add(printPreview);
        var previewHost = new Panel { Dock = DockStyle.Right, Width = 480, Padding = new Padding(16, 0, 0, 0) };
        previewHost.Controls.Add(previewCard);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 8, 24, 24) };
        body.Controls.Add(formCard);
        body.Controls.Add(previewHost);

        Controls.Add(body);
        Controls.Add(header);

        _fieldBoxes = new Dictionary<string, ModernTextBox>(StringComparer.Ordinal)
        {
            [nameof(StoreSettings.StoreName)] = _name,
            [nameof(StoreSettings.Address)] = _address,
            [nameof(StoreSettings.Phone)] = _phone,
            [nameof(StoreSettings.TaxCode)] = _taxCode,
            [nameof(StoreSettings.VndPerPoint)] = _vndPerPoint,
            [nameof(StoreSettings.PointValueVnd)] = _pointValue,
            [nameof(StoreSettings.ReturnWindowDays)] = _returnDays,
            [nameof(StoreSettings.ShiftVarianceNoteThreshold)] = _varianceThreshold,
        };

        foreach (var box in new ModernTextBox[] { _name, _address, _phone, _taxCode })
        {
            box.InnerTextBox.TextChanged += (_, _) => OnEdited();
        }

        foreach (var number in new ModernNumericBox[] { _vndPerPoint, _pointValue, _returnDays, _varianceThreshold })
        {
            number.ValueChanged += (_, _) => OnEdited();
        }

        _footer.TextChanged += (_, _) => OnEdited();
        foreach (var check in new ButtonBase[] { _paper80, _paper58, _previewAfterCheckout, _requireOpenShift, _allowNegativeStock })
        {
            switch (check)
            {
                case RadioButton radio:
                    radio.CheckedChanged += (_, _) => OnEdited();
                    break;
                case CheckBox box:
                    box.CheckedChanged += (_, _) => OnEdited();
                    break;
            }
        }

        _presenter.Attach(this);
    }

    /// <summary>F5 reloads, but asks first when there are unsaved changes.</summary>
    public override Task RefreshDataAsync() => _presenter.ReloadAsync();

    public override bool CanLeave() => _presenter.ConfirmLeave();

    protected override Task OnFirstLoadAsync() => _presenter.LoadAsync();

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            if (_save.Enabled)
            {
                _ = _presenter.SaveAsync();
            }

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    // ---- ISettingsView --------------------------------------------------------------------------------

    bool IView.IsAlive => IsAlive;

    void ISettingsView.ShowSettings(StoreSettings settings)
    {
        _populating = true;
        try
        {
            _name.Text = settings.StoreName;
            _address.Text = settings.Address ?? string.Empty;
            _phone.Text = settings.Phone ?? string.Empty;
            _taxCode.Text = settings.TaxCode ?? string.Empty;
            _footer.Text = settings.ReceiptFooter.Replace("\n", Environment.NewLine);
            _paper58.Checked = settings.ReceiptPaperWidthMm <= 58;
            _paper80.Checked = settings.ReceiptPaperWidthMm > 58;
            _previewAfterCheckout.Checked = settings.PreviewReceiptAfterCheckout;
            _vndPerPoint.Value = settings.VndPerPoint;
            _pointValue.Value = settings.PointValueVnd;
            _returnDays.Value = settings.ReturnWindowDays;
            _requireOpenShift.Checked = settings.RequireOpenShift;
            _allowNegativeStock.Checked = settings.AllowNegativeStock;
            _varianceThreshold.Value = settings.ShiftVarianceNoteThreshold;
        }
        finally
        {
            _populating = false;
        }
    }

    StoreSettings ISettingsView.ReadSettings() => new()
    {
        StoreName = _name.Text,
        Address = _address.Text,
        Phone = _phone.Text,
        TaxCode = _taxCode.Text,
        ReceiptFooter = _footer.Text,
        ReceiptPaperWidthMm = _paper58.Checked ? 58 : 80,
        PreviewReceiptAfterCheckout = _previewAfterCheckout.Checked,
        VndPerPoint = _vndPerPoint.ValueOrZero,
        PointValueVnd = _pointValue.ValueOrZero,
        ReturnWindowDays = (int)_returnDays.ValueOrZero,
        RequireOpenShift = _requireOpenShift.Checked,
        AllowNegativeStock = _allowNegativeStock.Checked,
        ShiftVarianceNoteThreshold = _varianceThreshold.ValueOrZero,
    };

    void ISettingsView.ShowState(SettingsFormState state)
    {
        foreach (var (property, box) in _fieldBoxes)
        {
            if (state.FieldErrors.TryGetValue(property, out var message))
            {
                box.SetError(message);
            }
            else
            {
                box.ClearValidation();
            }
        }

        _footerInfo.Text = state.FooterInfo;
        _footerInfo.Tone = state.FooterTone.ToLabelTone();
        _loyaltySummary.Text = state.LoyaltySummary.Replace("\n", Environment.NewLine);
        _warnings.Text = string.Join(Environment.NewLine, state.Warnings.Select(w => "⚠ " + w));
        _receiptPreview.Text = state.ReceiptPreview.Replace("\n", Environment.NewLine);
        _status.Set(state.StatusText, state.StatusTone.ToBadgeKind());
        _save.Enabled = state.CanSave;
        _undo.Enabled = state.CanUndo;
    }

    void ISettingsView.SetEditable(bool editable)
    {
        foreach (var box in _fieldBoxes.Values)
        {
            box.Enabled = editable;
        }

        _footer.Enabled = editable;
        _paper58.Enabled = _paper80.Enabled = _previewAfterCheckout.Enabled = editable;
        _requireOpenShift.Enabled = _allowNegativeStock.Enabled = editable;
        _defaults.Enabled = editable;
    }

    void ISettingsView.FocusField(string property)
    {
        Control? field = property switch
        {
            nameof(StoreSettings.ReceiptFooter) => _footer,
            nameof(StoreSettings.ReceiptPaperWidthMm) => _paper80,
            _ => _fieldBoxes.GetValueOrDefault(property),
        };
        if (field is null)
        {
            return;
        }

        for (var parent = field.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ModernTabPage page)
            {
                _tabs.SelectedTab = page;
                break;
            }
        }

        field.Focus();
    }

    void ISettingsView.ShowReceiptPrintPreview(ReceiptData receipt, int paperWidthMm)
    {
        using var preview = new ReceiptPreviewForm(receipt, paperWidthMm);
        preview.ShowDialog(FindForm());
    }

    void ISettingsView.ToastInfo(string message, string? title) => ToastInfo(message, title);

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) =>
        Confirm(message, title, confirmText, danger ? "Hủy" : "Xem lại", danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    private void OnEdited()
    {
        if (!_populating)
        {
            _presenter.Edited();
        }
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        if (!IsDisposed)
        {
            SyncThemeRadios();
        }
    }

    private void SyncThemeRadios()
    {
        _themeLight.Checked = ThemeManager.Mode == ThemeMode.Light;
        _themeDark.Checked = ThemeManager.Mode == ThemeMode.Dark;
    }

    // ---- Layout helpers -----------------------------------------------------------------------------

    private static ModernTabPage Page(string text, string glyph, out Panel content)
    {
        var page = new ModernTabPage(text) { IconGlyph = glyph, Padding = new Padding(8, 16, 8, 8) };
        content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        page.Controls.Add(content);
        return page;
    }

    private static void Caption(Control parent, string text, int x, int y) =>
        parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });

    private static void Hint(Control parent, string text, int x, int y, int width) =>
        parent.Controls.Add(new ToneLabel
        {
            Text = text,
            AutoSize = false,
            Location = new Point(x, y),
            Size = new Size(width, 44),
            Tone = LabelTone.Muted,
        });

    private static ModernTextBox Box(Control parent, string caption, int x, int y, int width, string placeholder)
    {
        Caption(parent, caption, x, y);
        var box = new ModernTextBox { Location = new Point(x, y + 22), Width = width, PlaceholderText = placeholder };
        parent.Controls.Add(box);
        return box;
    }

    private static ModernCurrencyBox Money(Control parent, string caption, int x, int y, decimal maximum)
    {
        Caption(parent, caption, x, y);
        var box = new ModernCurrencyBox { Location = new Point(x, y + 22), Width = HalfWidth, Minimum = 0, Maximum = maximum };
        parent.Controls.Add(box);
        return box;
    }

    private static ModernButton HeaderButton(string text, string glyph, ButtonVariant variant, int width) => new()
    {
        Text = text,
        IconGlyph = glyph,
        Variant = variant,
        Size = new Size(width, 40),
        Margin = new Padding(8, 0, 0, 0),
    };
}
