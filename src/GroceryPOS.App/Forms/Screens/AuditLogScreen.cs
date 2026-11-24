using GroceryPOS.App.Forms.Pos;
using GroceryPOS.App.Forms.Shared;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Reports;
using GroceryPOS.Presentation.Auditing;
using GroceryPOS.Presentation.Mvp;
using GroceryPOS.UI.Controls.Forms;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
using GroceryPOS.UI.Controls.Feedback;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Grids.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Inputs.Core;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Screens;

/// <summary>
/// Nhật ký hệ thống (<c>auditlog.view</c>): period, group, severity and user filters over a server-paged grid
/// (search box = username / object / details), KPI cards for the period (click one to filter exactly what it counts), a
/// detail card for the selected entry with "same user" / "same object" shortcuts, and Excel export of everything that
/// matches (audited). F5 reloads, Ctrl+E exports. Passive MVP view – the logic lives in <see cref="AuditLogPresenter"/>.
/// </summary>
internal sealed class AuditLogScreen : BaseUserControl, IAuditLogView
{
    private readonly AuditLogPresenter _presenter;
    private readonly ModernDateRangePicker _period;
    private readonly ModernComboBox _category;
    private readonly ModernComboBox _severity;
    private readonly ModernComboBox _user;
    private readonly ToneLabel _status;

    private readonly StatCard _total;
    private readonly StatCard _security;
    private readonly StatCard _salesExceptions;
    private readonly StatCard _stockAndPrice;

    private readonly ModernDataGrid _grid;

    private readonly ToneLabel _detailAction;
    private readonly Label _detailTime;
    private readonly Label _detailUser;
    private readonly Label _detailCategory;
    private readonly Label _detailSeverity;
    private readonly Label _detailEntity;
    private readonly TextBox _detailText;
    private readonly ModernButton _sameUser;
    private readonly ModernButton _sameEntity;

    private bool _suppressFilterEvents;

    public AuditLogScreen(IServiceProvider services)
    {
        _presenter = services.GetRequiredService<AuditLogPresenter>();

        // ---- Header: filters + actions -------------------------------------------------------------------
        _period = new ModernDateRangePicker();
        _period.Initialize(DatePreset.Last7Days);
        _period.RangeChanged += async (_, _) =>
        {
            if (!_suppressFilterEvents)
            {
                await _presenter.SetPeriodAsync(CurrentPeriod, _period.Describe());
            }
        };

        _category = Filter(210, AuditLogPresenter.CategoryOptions);
        _severity = Filter(170, AuditLogPresenter.SeverityOptions);
        _user = Filter(170, UserOptions([]));
        _category.SelectedIndexChanged += (_, _) => { if (!_suppressFilterEvents) _presenter.SetCategory(_category.SelectedValue as AuditCategory?); };
        _severity.SelectedIndexChanged += (_, _) => { if (!_suppressFilterEvents) _presenter.SetSeverity(_severity.SelectedValue as AuditSeverity?); };
        _user.SelectedIndexChanged += (_, _) => { if (!_suppressFilterEvents) _presenter.SetUser(_user.SelectedValue as string); };

        var refresh = Action("Làm mới (F5)", Glyphs.Refresh, ButtonVariant.Ghost, RefreshDataAsync);
        var export = Action("Xuất Excel (Ctrl+E)", Glyphs.Document, ButtonVariant.Primary, _presenter.ExportAsync);
        var clear = Action("Bỏ lọc", Glyphs.Clear, ButtonVariant.Secondary, () => { _presenter.ClearFilters(); return Task.CompletedTask; });
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        actions.Controls.AddRange([clear, refresh, export]);

        // Filters and actions wrap onto more lines on narrow windows instead of being hidden behind each other.
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(24, 16, 24, 0) };
        header.Controls.AddRange([_period, _category, _severity, _user, actions]);

        // ---- KPI row (click = filter exactly what the card counts) ------------------------------------------
        _total = Stat("Tổng sự kiện", Glyphs.History, AuditQuickFilter.None);
        _security = Stat("Đăng nhập thất bại", Glyphs.Lock, AuditQuickFilter.Security);
        _salesExceptions = Stat("Hủy HĐ · trả hàng · in lại", Glyphs.Return, AuditQuickFilter.SalesExceptions);
        _stockAndPrice = Stat("Đổi giá · chỉnh kho", Glyphs.Package, AuditQuickFilter.StockAndPrice);
        var stats = new StatCardRow { CardHeight = 120, MinCardWidth = 180, Padding = new Padding(24, 4, 24, 12) };
        stats.AddCards(_total, _security, _salesExceptions, _stockAndPrice);

        // ---- Grid (server-paged) --------------------------------------------------------------------------
        _grid = new ModernDataGrid
        {
            Dock = DockStyle.Fill,
            EmptyText = "Không có sự kiện nào khớp bộ lọc.",
            SearchPlaceholder = "Tìm người dùng, mã chứng từ, nội dung…",
            PageSize = AuditLogQuery.DefaultPageSize,
        };
        _grid.AddDateColumn(nameof(AuditLogEntryDto.Time), "Thời gian", width: 140, withTime: true, searchable: false);
        NotSortable(_grid.AddTextColumn(nameof(AuditLogEntryDto.Username), "Người dùng", width: 120));
        NotSortable(_grid.AddTextColumn(nameof(AuditLogEntryDto.ActionName), "Hành động", width: 200));
        NotSortable(_grid.AddTextColumn(nameof(AuditLogEntryDto.Entity), "Đối tượng", width: 200));
        NotSortable(_grid.AddTextColumn(nameof(AuditLogEntryDto.Details), "Chi tiết"));
        NotSortable(_grid.AddTextColumn(nameof(AuditLogEntryDto.SeverityName), "Mức độ", width: 100));
        _grid.InnerGrid.CellFormatting += ToneSeverityCells;
        _grid.SelectionChanged += (_, _) => _presenter.Select(_grid.GetSelected<AuditLogEntryDto>());
        _grid.LoadFailed += (_, e) => ShowStatusText($"Không tải được nhật ký: {e.Exception?.Message}", LabelTone.Danger);

        // Status under the list: period, match count, active filters (two lines when filtered).
        _status = new ToneLabel { Dock = DockStyle.Bottom, Height = 44, AutoSize = false, Tone = LabelTone.Muted, Font = ThemeFonts.Get(FontRole.Caption), Padding = new Padding(0, 6, 0, 0), AutoEllipsis = true };

        var gridCard = new ModernCard { Title = "Sự kiện", HeaderGlyph = Glyphs.List, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
        gridCard.Controls.Add(_grid);
        gridCard.Controls.Add(_status);

        // ---- Detail card ----------------------------------------------------------------------------------
        _detailAction = new ToneLabel { AutoSize = false, Dock = DockStyle.Top, Height = 32, Font = ThemeFonts.Get(FontRole.Subtitle), AutoEllipsis = true };
        _detailTime = DetailValue();
        _detailUser = DetailValue();
        _detailCategory = DetailValue();
        _detailSeverity = DetailValue();
        _detailEntity = DetailValue();

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(0, 4, 0, 8) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, "Thời gian", _detailTime);
        AddField(fields, "Người dùng", _detailUser);
        AddField(fields, "Nhóm", _detailCategory);
        AddField(fields, "Mức độ", _detailSeverity);
        AddField(fields, "Đối tượng", _detailEntity);

        var detailsCaption = new Label { Text = "Nội dung", Dock = DockStyle.Top, Height = 24, Font = ThemeFonts.Get(FontRole.Caption) };
        _detailText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = ThemeFonts.Get(FontRole.Body),
        };

        _grid.RowActivated += (_, _) => _detailText.Focus();

        _sameUser = Action("Cùng người dùng", Glyphs.People, ButtonVariant.Secondary, () => { _presenter.FilterBySelectedUser(); return Task.CompletedTask; });
        _sameEntity = Action("Cùng đối tượng", Glyphs.Filter, ButtonVariant.Secondary, () => { _presenter.FilterBySelectedEntity(); return Task.CompletedTask; });
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        detailActions.Controls.AddRange([_sameUser, _sameEntity]);

        var detailCard = new ModernCard { Title = "Chi tiết sự kiện", HeaderGlyph = Glyphs.Info, Dock = DockStyle.Fill };
        detailCard.Controls.Add(_detailText);
        detailCard.Controls.Add(detailsCaption);
        detailCard.Controls.Add(fields);
        detailCard.Controls.Add(_detailAction);
        detailCard.Controls.Add(detailActions);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(24, 0, 24, 24) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(gridCard, 0, 0);
        body.Controls.Add(detailCard, 1, 0);

        Controls.Add(body);
        Controls.Add(stats);
        Controls.Add(header);

        _presenter.Attach(this);
    }

    /// <summary>First show and F5: today may have moved on (presets are relative), then everything reloads.</summary>
    public override Task RefreshDataAsync()
    {
        _suppressFilterEvents = true;
        try
        {
            _period.RefreshToday();
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        return _presenter.LoadAsync(CurrentPeriod, _period.Describe());
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.E) && !IsBusy)
        {
            _ = _presenter.ExportAsync();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private ReportPeriod CurrentPeriod => new(_period.Range.From, _period.Range.To);

    private async Task<GridPage<object>> LoadPageAsync(GridQuery query, CancellationToken cancellationToken)
    {
        var request = ToRequest(query);
        var page = await _presenter.LoadPageAsync(request, cancellationToken);
        return page is null
            ? GridPage<object>.Empty(query.PageSize)
            : new GridPage<object>(page.Items.Cast<object>().ToList(), page.TotalCount, page.PageIndex, page.PageSize);
    }

    private static AuditGridRequest ToRequest(GridQuery query) => new(
        query.Search,
        OldestFirst: query.IsSorted && query.SortKey == nameof(AuditLogEntryDto.Time) && query.SortDirection == SortDirection.Ascending,
        query.PageIndex,
        query.PageSize);

    private void ToneSeverityCells(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.InnerGrid.Rows[e.RowIndex].Tag is not AuditLogEntryDto entry)
        {
            return;
        }

        var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (property is not (nameof(AuditLogEntryDto.ActionName) or nameof(AuditLogEntryDto.SeverityName)))
        {
            return;
        }

        var palette = ThemeManager.Current.Colors;
        e.CellStyle!.ForeColor = AuditLogDetail.ToneOf(entry.Severity) switch
        {
            Tone.Danger => palette.Danger,
            Tone.Warning => palette.Warning,
            _ => property == nameof(AuditLogEntryDto.SeverityName) ? palette.TextMuted : e.CellStyle.ForeColor,
        };
    }

    private void ShowStatusText(string text, LabelTone tone)
    {
        if (IsAlive)
        {
            _status.Text = text;
            _status.Tone = tone;
        }
    }

    // ---- IAuditLogView -----------------------------------------------------------------------------------

    bool IView.IsAlive => IsAlive;

    void IAuditLogView.ShowUsers(IReadOnlyList<string> usernames)
    {
        _suppressFilterEvents = true;
        try
        {
            _user.DataSource = UserOptions(usernames);
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    void IAuditLogView.ShowFilters(AuditLogFilters filters)
    {
        _suppressFilterEvents = true;
        try
        {
            Select(_category, filters.Category);
            Select(_severity, filters.MinSeverity);
            Select(_user, filters.Username);
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    void IAuditLogView.ShowCards(AuditLogCards cards)
    {
        Show(_total, cards.Total);
        Show(_security, cards.Security);
        Show(_salesExceptions, cards.SalesExceptions);
        Show(_stockAndPrice, cards.StockAndPrice);
    }

    void IAuditLogView.ShowStatus(string text, Tone tone) => ShowStatusText(text, tone.ToLabelTone());

    void IAuditLogView.ShowDetail(AuditLogDetail detail)
    {
        _detailAction.Text = detail.Title;
        _detailAction.Tone = detail.TitleTone.ToLabelTone();
        _detailTime.Text = detail.Time;
        _detailUser.Text = detail.User;
        _detailCategory.Text = detail.Category;
        _detailSeverity.Text = detail.Severity;
        _detailEntity.Text = detail.Entity;
        _detailText.Text = string.Join(Environment.NewLine, detail.Lines);
        _sameUser.Enabled = detail.CanFilterUser;
        _sameEntity.Enabled = detail.CanFilterEntity;
    }

    void IAuditLogView.ReloadEntries(bool firstPage)
    {
        if (_grid.DataProvider is null)
        {
            _grid.DataProvider = LoadPageAsync; // loads page 1
        }
        else if (firstPage && _grid.Query.PageIndex > 0)
        {
            _grid.GoToPage(0); // reloads
        }
        else
        {
            _grid.Reload();
        }
    }

    void IAuditLogView.SetSearch(string text) => _grid.SetSearch(text);

    void IAuditLogView.ClearSearch() => _grid.ClearSearch();

    AuditGridRequest IAuditLogView.CurrentGrid => ToRequest(_grid.Query);

    Task<T> IAuditLogView.RunBusyAsync<T>(Func<Task<T>> work, string message) => RunBusyAsync(work, message);

    Task<string?> IAuditLogView.SaveExcelAsync(string suggestedFileName, byte[] content) =>
        ExcelFiles.SaveAsync(this, AuditLogPresenter.Title, "Lưu nhật ký hệ thống", suggestedFileName, content);

    void IAuditLogView.ToastWarning(string message, string? title) => ToastWarning(message, title);

    void IView.ShowWarning(string message, string title) => ShowWarning(message, title);

    void IView.ShowInfo(string message, string title) => ShowInfo(message, title);

    bool IView.Confirm(string message, string title, string confirmText, bool danger) => Confirm(message, title, confirmText, danger: danger);

    void IView.ToastSuccess(string message, string? title) => ToastSuccess(message, title);

    void IView.ToastError(string message, string? title) => ToastError(message, title);

    // ---- Building blocks -------------------------------------------------------------------------------

    private static void NotSortable(DataGridViewColumn column) => column.SortMode = DataGridViewColumnSortMode.NotSortable;

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private StatCard Stat(string caption, string glyph, AuditQuickFilter filter)
    {
        void OnClick() => _presenter.ApplyQuickFilter(filter);
        var card = new StatCard
        {
            Caption = caption,
            Glyph = glyph,
            Value = "–",
            Size = new Size(250, 120),
            Margin = new Padding(0, 0, 16, 0),
            Cursor = Cursors.Hand,
        };
        card.Click += (_, _) => OnClick();
        foreach (Control child in card.Controls)
        {
            child.Click += (_, _) => OnClick();
        }

        return card;
    }

    private static void Show(StatCard card, AuditStatCard state)
    {
        card.Value = state.Value;
        card.Note = state.Note;
        card.Accent = state.Accent.ToBadgeKind();
    }

    private static List<FilterOption<string?>> UserOptions(IEnumerable<string> usernames) =>
        [new(null, "Mọi người dùng"), .. usernames.Select(u => new FilterOption<string?>(u, u))];

    /// <summary>Selects <paramref name="value"/>, or the first ("Tất cả…") entry when it is null or not listed.</summary>
    private static void Select<T>(ModernComboBox combo, T? value)
    {
        if (value is null)
        {
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
            return;
        }

        combo.SelectedValue = value;
        if (!Equals(combo.SelectedValue, value))
        {
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }
    }

    private static ModernComboBox Filter<T>(int width, IReadOnlyList<FilterOption<T>> options) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        ReserveMessageSpace = false,
        DisplayMember = nameof(FilterOption<T>.Name),
        ValueMember = nameof(FilterOption<T>.Value),
        DataSource = options,
        Margin = new Padding(8, 0, 0, 0),
    };

    private static Label DetailValue() => new() { AutoSize = true, MaximumSize = new Size(260, 0), Margin = new Padding(0, 4, 0, 4) };

    private static void AddField(TableLayoutPanel table, string caption, Label value)
    {
        var label = new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 4, 8, 4), Font = ThemeFonts.Get(FontRole.Caption) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, table.RowCount - 1);
        table.Controls.Add(value, 1, table.RowCount - 1);
    }
}
