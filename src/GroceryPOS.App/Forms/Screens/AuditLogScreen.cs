using GroceryPOS.App.Forms.Pos;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Reports;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Display;
using GroceryPOS.UI.Controls.Display.Core;
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
/// (search box = username / object / details), KPI cards for the period (click one to filter), a detail card for the
/// selected entry with "same user" / "same object" shortcuts, and Excel export of everything that matches (audited).
/// F5 reloads, Ctrl+E exports.
/// </summary>
internal sealed class AuditLogScreen : UserControl
{
    private const string Caption = "Nhật ký hệ thống";

    private readonly IServiceScopeFactory _scopeFactory;
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

    private readonly ModernCard _detailCard;
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

    public AuditLogScreen(IServiceScopeFactory scopeFactory, ICurrentUser currentUser)
    {
        _scopeFactory = scopeFactory;
        _ = currentUser; // the menu entry is gated by auditlog.view; the service re-checks every call.
        Dock = DockStyle.Fill;

        // ---- Header: filters + actions -------------------------------------------------------------------
        _period = new ModernDateRangePicker();
        _period.Initialize(DatePreset.Last7Days);
        _period.RangeChanged += (_, _) => OnFiltersChanged(reloadSummary: true);

        var categories = new List<Option<AuditCategory?>> { new(null, "Tất cả nhóm") };
        categories.AddRange(AuditCatalog.Categories.Select(c => new Option<AuditCategory?>(c, AuditCatalog.CategoryName(c))));
        _category = Filter(210, categories);

        _severity = Filter(170, new List<Option<AuditSeverity?>>
        {
            new(null, "Mọi mức độ"),
            new(AuditSeverity.Warning, "Từ cần chú ý"),
            new(AuditSeverity.Critical, "Chỉ quan trọng"),
        });

        _user = Filter(170, new List<Option<string?>> { new(null, "Mọi người dùng") });

        foreach (var filter in new[] { _category, _severity, _user })
        {
            filter.SelectedIndexChanged += (_, _) => OnFiltersChanged(reloadSummary: false);
        }

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        filters.Controls.AddRange([_period, _category, _severity, _user]);

        var refresh = Action("Làm mới (F5)", Glyphs.Refresh, ButtonVariant.Ghost, ReloadAllAsync);
        var export = Action("Xuất Excel (Ctrl+E)", Glyphs.Document, ButtonVariant.Primary, ExportAsync);
        var clear = Action("Bỏ lọc", Glyphs.Clear, ButtonVariant.Secondary, () => { ClearFilters(); return Task.CompletedTask; });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([clear, refresh, export]);

        var header = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(24, 16, 24, 0) };
        header.Controls.Add(filters);
        header.Controls.Add(actions);

        // ---- KPI row (click = filter) ----------------------------------------------------------------------
        _total = Stat("Tổng sự kiện", Glyphs.History, BadgeKind.Primary, () => ApplyQuickFilter(null, null));
        _security = Stat("Đăng nhập thất bại", Glyphs.Lock, BadgeKind.Warning, () => ApplyQuickFilter(AuditCategory.Authentication, AuditSeverity.Warning));
        _salesExceptions = Stat("Hủy HĐ · trả hàng · in lại", Glyphs.Return, BadgeKind.Danger, () => ApplyQuickFilter(AuditCategory.Sales, AuditSeverity.Warning));
        _stockAndPrice = Stat("Đổi giá · chỉnh kho", Glyphs.Package, BadgeKind.Info, () => ApplyQuickFilter(null, AuditSeverity.Warning));
        _status = new ToneLabel { AutoSize = true, Tone = LabelTone.Muted, Margin = new Padding(0, 100, 0, 0), Font = ThemeFonts.Get(FontRole.Caption) };
        var stats = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 136, WrapContents = false, Padding = new Padding(24, 4, 24, 0) };
        stats.Controls.AddRange([_total, _security, _salesExceptions, _stockAndPrice, _status]);

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
        _grid.SelectionChanged += (_, _) => ShowDetail(_grid.GetSelected<AuditLogEntryDto>());
        _grid.LoadFailed += (_, e) => SetStatus($"Không tải được nhật ký: {e.Exception?.Message}", LabelTone.Danger);

        var gridCard = new ModernCard { Title = "Sự kiện", HeaderGlyph = Glyphs.List, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
        gridCard.Controls.Add(_grid);

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

        _sameUser = Action("Cùng người dùng", Glyphs.People, ButtonVariant.Secondary, () => { FilterBySelectedUser(); return Task.CompletedTask; });
        _sameEntity = Action("Cùng đối tượng", Glyphs.Filter, ButtonVariant.Secondary, () => { FilterBySelectedEntity(); return Task.CompletedTask; });
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        detailActions.Controls.AddRange([_sameUser, _sameEntity]);

        _detailCard = new ModernCard { Title = "Chi tiết sự kiện", HeaderGlyph = Glyphs.Info, Dock = DockStyle.Fill };
        _detailCard.Controls.Add(_detailText);
        _detailCard.Controls.Add(detailsCaption);
        _detailCard.Controls.Add(fields);
        _detailCard.Controls.Add(_detailAction);
        _detailCard.Controls.Add(detailActions);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(24, 0, 24, 24) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(gridCard, 0, 0);
        body.Controls.Add(_detailCard, 1, 0);

        Controls.Add(body);
        Controls.Add(stats);
        Controls.Add(header);

        ShowDetail(null);
        Load += async (_, _) =>
        {
            await LoadUsersAsync();
            await LoadSummaryAsync();
            _grid.DataProvider = LoadPageAsync; // first page loads here
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                _ = ReloadAllAsync();
                return true;
            case Keys.Control | Keys.E:
                _ = ExportAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---- Query building ------------------------------------------------------------------------------------

    private ReportPeriod CurrentPeriod => new(_period.Range.From, _period.Range.To);

    private AuditLogQuery BuildQuery(GridQuery? grid = null)
    {
        var gridQuery = grid ?? _grid.Query;
        return new AuditLogQuery(
            CurrentPeriod,
            Category: _category.SelectedValue as AuditCategory?,
            MinSeverity: _severity.SelectedValue as AuditSeverity?,
            Username: _user.SelectedValue as string,
            Search: gridQuery.Search,
            OldestFirst: gridQuery.IsSorted && gridQuery.SortKey == nameof(AuditLogEntryDto.Time) && gridQuery.SortDirection == SortDirection.Ascending,
            PageIndex: gridQuery.PageIndex,
            PageSize: gridQuery.PageSize);
    }

    private async Task<GridPage<object>> LoadPageAsync(GridQuery gridQuery, CancellationToken cancellationToken)
    {
        var query = BuildQuery(gridQuery);
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuditLogService>().SearchAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            SetStatus(result.Error.Message, LabelTone.Danger);
            return GridPage<object>.Empty(gridQuery.PageSize);
        }

        var page = result.Value;
        SetStatus($"{_period.Describe()} · {Number(page.TotalCount)} sự kiện khớp bộ lọc · cập nhật lúc {DateTime.Now:HH:mm}");
        return new GridPage<object>(page.Items.Cast<object>().ToList(), page.TotalCount, page.PageIndex, page.PageSize);
    }

    private async Task LoadSummaryAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuditLogService>().GetSummaryAsync(CurrentPeriod);
        if (IsDisposed)
        {
            return;
        }

        if (result.IsFailure)
        {
            SetStatus(result.Error.Message, LabelTone.Danger);
            return;
        }

        var s = result.Value;
        _total.Value = Number(s.Total);
        _total.Note = $"{Number(s.Critical)} quan trọng · {Number(s.Warning)} cần chú ý";
        _security.Value = Number(s.FailedLogins + s.AccountsLocked);
        _security.Accent = s.AccountsLocked > 0 ? BadgeKind.Danger : BadgeKind.Warning;
        _security.Note = s.AccountsLocked > 0 ? $"{Number(s.AccountsLocked)} lần khóa tài khoản" : "Không có tài khoản bị khóa";
        _salesExceptions.Value = Number(s.SalesExceptions);
        _salesExceptions.Note = $"Hủy {Number(s.SalesVoided)} · trả {Number(s.Returns)} · in lại {Number(s.Reprints)}";
        _stockAndPrice.Value = Number(s.StockAndPrice);
        _stockAndPrice.Note = $"Đổi giá {Number(s.PriceChanges)} · kho {Number(s.StockAdjustments)}";
    }

    private async Task LoadUsersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuditLogService>().ListUsernamesAsync();
        if (IsDisposed || result.IsFailure)
        {
            return;
        }

        var current = _user.SelectedValue as string;
        var options = new List<Option<string?>> { new(null, "Mọi người dùng") };
        options.AddRange(result.Value.Select(u => new Option<string?>(u, u)));
        _suppressFilterEvents = true;
        try
        {
            _user.DataSource = options;
            SelectUser(current);
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void OnFiltersChanged(bool reloadSummary)
    {
        if (_suppressFilterEvents || _grid.DataProvider is null)
        {
            return;
        }

        if (_grid.Query.PageIndex > 0)
        {
            _grid.GoToPage(0); // reloads
        }
        else
        {
            _grid.Reload();
        }
        if (reloadSummary)
        {
            _ = LoadSummaryAsync();
        }
    }

    private async Task ReloadAllAsync()
    {
        _period.RefreshToday();
        await LoadUsersAsync();
        await LoadSummaryAsync();
        _grid.Reload();
    }

    private void ApplyQuickFilter(AuditCategory? category, AuditSeverity? severity)
    {
        _suppressFilterEvents = true;
        try
        {
            _category.SelectedValue = category!;
            _severity.SelectedValue = severity!;
            if (category is null)
            {
                _category.SelectedIndex = 0;
            }

            if (severity is null)
            {
                _severity.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        OnFiltersChanged(reloadSummary: false);
    }

    private void ClearFilters()
    {
        _suppressFilterEvents = true;
        try
        {
            _category.SelectedIndex = 0;
            _severity.SelectedIndex = 0;
            _user.SelectedIndex = 0;
            _grid.ClearSearch();
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        OnFiltersChanged(reloadSummary: false);
    }

    private void FilterBySelectedUser()
    {
        if (_grid.GetSelected<AuditLogEntryDto>() is not { } entry)
        {
            return;
        }

        _suppressFilterEvents = true;
        try
        {
            SelectUser(entry.Username);
        }
        finally
        {
            _suppressFilterEvents = false;
        }

        OnFiltersChanged(reloadSummary: false);
    }

    private void FilterBySelectedEntity()
    {
        if (_grid.GetSelected<AuditLogEntryDto>() is { EntityId: { Length: > 0 } id })
        {
            _grid.SetSearch(id);
        }
    }

    private void SelectUser(string? username)
    {
        if (username is null)
        {
            _user.SelectedIndex = 0;
            return;
        }

        _user.SelectedValue = username;
        if (!Equals(_user.SelectedValue, username))
        {
            _user.SelectedIndex = 0;
        }
    }

    // ---- Detail ----------------------------------------------------------------------------------------

    private void ShowDetail(AuditLogEntryDto? entry)
    {
        if (entry is null)
        {
            _detailAction.Text = "Chọn một sự kiện";
            _detailAction.Tone = LabelTone.Muted;
            foreach (var label in new[] { _detailTime, _detailUser, _detailCategory, _detailSeverity, _detailEntity })
            {
                label.Text = "–";
            }

            _detailText.Text = string.Empty;
            _sameUser.Enabled = false;
            _sameEntity.Enabled = false;
            return;
        }

        _detailAction.Text = entry.ActionName;
        _detailAction.Tone = Tone(entry.Severity);
        _detailTime.Text = entry.Time.ToString("dd/MM/yyyy HH:mm:ss");
        _detailUser.Text = entry.UserId is { } id ? $"{entry.Username} (#{id})" : entry.Username;
        _detailCategory.Text = entry.CategoryName;
        _detailSeverity.Text = entry.SeverityName;
        _detailEntity.Text = entry.Entity ?? "–";
        _detailText.Text = string.IsNullOrWhiteSpace(entry.Details)
            ? "(không có nội dung)"
            : entry.Details.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        _sameUser.Enabled = true;
        _sameEntity.Enabled = !string.IsNullOrEmpty(entry.EntityId);
    }

    private static LabelTone Tone(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => LabelTone.Danger,
        AuditSeverity.Warning => LabelTone.Warning,
        _ => LabelTone.Primary,
    };

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
        e.CellStyle!.ForeColor = entry.Severity switch
        {
            AuditSeverity.Critical => palette.Danger,
            AuditSeverity.Warning => palette.Warning,
            _ => property == nameof(AuditLogEntryDto.SeverityName) ? palette.TextMuted : e.CellStyle.ForeColor,
        };
    }

    // ---- Export ----------------------------------------------------------------------------------------

    private async Task ExportAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        UseWaitCursor = true;
        var result = await service.ExportAsync(BuildQuery());
        if (IsDisposed)
        {
            return;
        }

        UseWaitCursor = false;
        if (result.IsFailure)
        {
            MessageBox.Show(this, result.Error.Message, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Lưu nhật ký hệ thống",
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = result.Value.FileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, result.Value.Content);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Không lưu được file (có thể file đang mở trong Excel).\n{ex.Message}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetStatus($"Đã xuất {Path.GetFileName(dialog.FileName)}", LabelTone.Success);
        _grid.Reload(); // the export itself is now in the log
        _ = LoadSummaryAsync();
    }

    // ---- Building blocks -------------------------------------------------------------------------------

    private void SetStatus(string text, LabelTone tone = LabelTone.Muted)
    {
        if (IsDisposed)
        {
            return;
        }

        _status.Text = text;
        _status.Tone = tone;
    }

    private static string Number(int value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));

    private static void NotSortable(DataGridViewColumn column) => column.SortMode = DataGridViewColumnSortMode.NotSortable;

    private static ModernButton Action(string text, string glyph, ButtonVariant variant, Func<Task> onClick)
    {
        var button = new ModernButton { Text = text, IconGlyph = glyph, Variant = variant, AutoSize = true, Height = 40, Margin = new Padding(8, 0, 0, 0) };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private static StatCard Stat(string caption, string glyph, BadgeKind accent, System.Action onClick)
    {
        var card = new StatCard
        {
            Caption = caption,
            Glyph = glyph,
            Accent = accent,
            Value = "–",
            Size = new Size(250, 120),
            Margin = new Padding(0, 0, 16, 0),
            Cursor = Cursors.Hand,
        };
        card.Click += (_, _) => onClick();
        foreach (Control child in card.Controls)
        {
            child.Click += (_, _) => onClick();
        }

        return card;
    }

    private static ModernComboBox Filter<T>(int width, IList<Option<T>> options) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        ReserveMessageSpace = false,
        DisplayMember = nameof(Option<T>.Name),
        ValueMember = nameof(Option<T>.Value),
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

    private sealed record Option<T>(T Value, string Name);
}
