using System.ComponentModel;
using System.Globalization;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Drawing;
using GroceryPOS.UI.Controls.Grids.Core;
using GroceryPOS.UI.Controls.Icons;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;

namespace GroceryPOS.UI.Controls.Grids;

/// <summary>
/// List grid for every list screen: a search box with a toolbar slot on top, a themed grid in the middle and a
/// pager (summary, page size, page links) at the bottom.
/// <para>
/// Two data modes: <see cref="SetItems{T}"/> keeps all rows in memory and searches/sorts/pages them locally
/// (accent-insensitive search over the searchable columns, Vietnamese collation); or set
/// <see cref="DataProvider"/> to load each page from the application layer (audit log, invoices…).
/// </para>
/// Keyboard: Ctrl+F search, Esc clears the search, Enter or double-click raises <see cref="RowActivated"/>,
/// Ctrl+PageUp/PageDown change page, typing a letter in the grid starts a search.
/// </summary>
[ToolboxItem(true)]
[DefaultEvent(nameof(RowActivated))]
public class ModernDataGrid : UserControl, IThemeable
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private readonly GridTable _grid;
    private readonly Panel _toolbar;
    private readonly ModernTextBox _search;
    private readonly FlowLayoutPanel _toolbarItems;
    private readonly Panel _footer;
    private readonly Label _summary;
    private readonly FlowLayoutPanel _pager;
    private readonly ModernComboBox _pageSize;
    private readonly System.Windows.Forms.Timer _searchDelay;
    private readonly HashSet<string> _nonSearchable = new(StringComparer.Ordinal);

    private GridQuery _query = new();
    private GridDataView<object>? _view;
    private Func<GridQuery, CancellationToken, Task<GridPage<object>>>? _dataProvider;
    private GridPage<object> _page = GridPage<object>.Empty();
    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;
    private bool _isLoading;
    private bool _syncingPageSize;
    private int _cornerRadius = -1;

    // Control.Visible reads false until the parent is shown, so the requested state is kept here.
    private bool _showSearch = true;
    private bool _showPager = true;

    public ModernDataGrid()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _grid = new GridTable { Dock = DockStyle.Fill };
        _grid.ColumnHeaderMouseClick += OnColumnHeaderMouseClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                RaiseRowActivated(e.RowIndex);
            }
        };
        _grid.KeyDown += OnGridKeyDown;
        _grid.KeyPress += OnGridKeyPress;
        _grid.SelectionChanged += (_, e) => OnSelectionChanged(e);

        _search = new ModernTextBox
        {
            IconGlyph = Glyphs.Search,
            PlaceholderText = "Tìm kiếm…",
            ReserveMessageSpace = false,
            Width = 320,
            Dock = DockStyle.Left,
            TabIndex = 0,
        };
        _search.TextChanged += (_, _) =>
        {
            _search.TrailingGlyph = _search.Text.Length > 0 ? Glyphs.Clear : null;
            _searchDelay!.Stop();
            _searchDelay.Start();
        };
        _search.TrailingIconClick += (_, _) => ClearSearch();
        _search.EnterPressed += (_, _) => ApplySearchNow();
        _search.KeyDown += OnSearchKeyDown;

        _toolbarItems = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            TabIndex = 1,
        };
        _toolbarItems.ControlAdded += (_, e) =>
        {
            ThemeManager.Apply(e.Control!);
            UpdateToolbarVisibility();
        };
        _toolbarItems.ControlRemoved += (_, _) => UpdateToolbarVisibility();

        _toolbar = new Panel { Dock = DockStyle.Top, Padding = new Padding(12, 12, 12, 8), BackColor = Color.Transparent };
        _toolbar.Controls.Add(_toolbarItems);
        _toolbar.Controls.Add(_search);

        _summary = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BackColor = Color.Transparent,
            Text = _page.Summary,
        };

        _pageSize = new ModernComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            ReserveMessageSpace = false,
            Width = 120,
            Margin = new Padding(8, 0, 0, 0),
        };
        foreach (var size in GridQuery.PageSizeOptions)
        {
            _pageSize.Items.Add(new PageSizeItem(size));
        }

        _pageSize.SelectedIndexChanged += OnPageSizeChanged;

        _pager = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = Padding.Empty,
        };

        _footer = new Panel { Dock = DockStyle.Bottom, Padding = new Padding(12, 6, 12, 6), BackColor = Color.Transparent };
        _footer.Controls.Add(_summary);
        _footer.Controls.Add(_pager);

        // Docking runs from the last added control: toolbar and footer first, the grid fills the rest.
        Controls.Add(_grid);
        Controls.Add(_toolbar);
        Controls.Add(_footer);

        _searchDelay = new System.Windows.Forms.Timer { Interval = 300 };
        _searchDelay.Tick += (_, _) => ApplySearchNow();

        Size = new Size(640, 400);
        Padding = new Padding(1);
        SyncPageSizeCombo();
        ApplyTheme(ThemeManager.Current);
        RebuildPager();
    }

    /// <summary>Selected row changed (also after a page change or reload).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Double-click or Enter on a row – usually opens the edit dialog.</summary>
    public event EventHandler<GridRowEventArgs>? RowActivated;

    /// <summary>Search, sort, page or page size changed (before the data is loaded).</summary>
    public event EventHandler<GridQueryEventArgs>? QueryChanged;

    /// <summary>A page shown (after any search/sort/page change or reload).</summary>
    public event EventHandler? PageLoaded;

    /// <summary>
    /// <see cref="DataProvider"/> threw. When nobody handles this event the exception is rethrown on the UI
    /// thread, so the global exception handler logs it.
    /// </summary>
    public event EventHandler<ThreadExceptionEventArgs>? LoadFailed;

    /// <summary>The grid's columns. Prefer the <c>Add…Column</c> helpers, which set formats and sorting.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridViewColumnCollection Columns => _grid.Columns;

    /// <summary>The inner grid, for things the wrapper does not expose (context menus, cell styles).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DataGridView InnerGrid => _grid;

    /// <summary>Right side of the toolbar for screen buttons (Thêm, Sửa, Xuất Excel…); laid out right to left.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowLayoutPanel ToolbarItems => _toolbarItems;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GridQuery Query => _query;

    /// <summary>The page currently shown.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GridPage<object> CurrentPage => _page;

    [Category("Appearance")]
    [DefaultValue("Tìm kiếm…")]
    public string SearchPlaceholder
    {
        get => _search.PlaceholderText;
        set => _search.PlaceholderText = value;
    }

    [Category("Appearance")]
    [DefaultValue(true)]
    public bool ShowSearch
    {
        get => _showSearch;
        set
        {
            _showSearch = value;
            _search.Visible = value;
            UpdateToolbarVisibility();
        }
    }

    [Category("Appearance")]
    [DefaultValue(true)]
    public bool ShowPager
    {
        get => _showPager;
        set
        {
            _showPager = value;
            _footer.Visible = value;
        }
    }

    [Category("Appearance")]
    [DefaultValue("Không có dữ liệu")]
    public string EmptyText
    {
        get => _grid.EmptyText;
        set
        {
            _grid.EmptyText = value;
            _grid.Invalidate();
        }
    }

    /// <summary>Rows per page; when <see cref="ShowPager"/> is off, set it high enough for all rows.</summary>
    [Category("Behavior")]
    [DefaultValue(GridQuery.DefaultPageSize)]
    public int PageSize
    {
        get => _query.PageSize;
        set
        {
            if (value != _query.PageSize)
            {
                ChangeQuery(_query.WithPageSize(value));
                SyncPageSizeCombo();
            }
        }
    }

    /// <summary>Corner radius of the outer border in pixels at 96 DPI; -1 uses the theme's large radius.</summary>
    [Category("Appearance")]
    [DefaultValue(-1)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(-1, value);
            Invalidate();
        }
    }

    /// <summary>
    /// Loads pages from outside (server-side search/sort/paging). Setting it switches off the in-memory mode
    /// and loads the first page. The provider must honour <see cref="GridQuery.PageIndex"/> clamping itself
    /// or return the page it actually served.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<GridQuery, CancellationToken, Task<GridPage<object>>>? DataProvider
    {
        get => _dataProvider;
        set
        {
            _dataProvider = value;
            if (value is not null)
            {
                _view = null;
                _query = _query.WithPage(0);
                Reload();
            }
        }
    }

    [Browsable(false)]
    public bool IsLoading => _isLoading;

    /// <summary>The item behind the selected row, or null.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem => _grid.CurrentRow?.Tag;

    public T? GetSelected<T>()
        where T : class => SelectedItem as T;

    void IThemeable.ApplyTheme(Theme theme) => ApplyTheme(theme);

    bool IThemeable.ThemesChildren => true;

    // ---- Columns -----------------------------------------------------------------------------------------

    /// <summary>Text column bound to <paramref name="property"/> (dotted paths allowed). Width 0 fills the remaining space.</summary>
    public DataGridViewTextBoxColumn AddTextColumn(string property, string header, int width = 0, bool searchable = true)
    {
        var column = CreateColumn(property, header, width, searchable);
        _grid.Columns.Add(column);
        return column;
    }

    /// <summary>VND column: "1.250.000 ₫", right-aligned.</summary>
    public DataGridViewTextBoxColumn AddMoneyColumn(string property, string header, int width = 130, bool searchable = false)
    {
        var column = CreateColumn(property, header, width, searchable);
        column.DefaultCellStyle.Format = "#,##0 ₫";
        column.DefaultCellStyle.FormatProvider = Vietnamese;
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns.Add(column);
        return column;
    }

    /// <summary>Number column with vi-VN grouping (quantities, stock, points), right-aligned.</summary>
    public DataGridViewTextBoxColumn AddNumberColumn(string property, string header, int width = 100, int decimals = 0, bool searchable = false)
    {
        var column = CreateColumn(property, header, width, searchable);
        column.DefaultCellStyle.Format = decimals > 0 ? "#,##0." + new string('#', decimals) : "#,##0";
        column.DefaultCellStyle.FormatProvider = Vietnamese;
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns.Add(column);
        return column;
    }

    /// <summary>Date column, dd/MM/yyyy or dd/MM/yyyy HH:mm.</summary>
    public DataGridViewTextBoxColumn AddDateColumn(string property, string header, int width = 0, bool withTime = false, bool searchable = true)
    {
        var column = CreateColumn(property, header, width == 0 ? (withTime ? 140 : 110) : width, searchable);
        column.DefaultCellStyle.Format = withTime ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy";
        column.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns.Add(column);
        return column;
    }

    /// <summary>Read-only check box column (Đang bán, Đã khóa…).</summary>
    public DataGridViewCheckBoxColumn AddCheckColumn(string property, string header, int width = 90)
    {
        var column = new DataGridViewCheckBoxColumn
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            Width = this.ScaleDpi(width),
            SortMode = DataGridViewColumnSortMode.Programmatic,
        };
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _nonSearchable.Add(property);
        _grid.Columns.Add(column);
        return column;
    }

    // ---- Data --------------------------------------------------------------------------------------------

    /// <summary>
    /// In-memory mode: replaces all rows and shows the page for the current query (search, sort and page are
    /// kept, so a reload after an edit stays where the user was).
    /// </summary>
    public void SetItems<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _dataProvider = null;
        _view ??= new GridDataView<object>(BuildFields());
        _view.SetItems(items.Cast<object>());
        RunQuery();
    }

    /// <summary>Shows the current query again (in-memory: re-filters; provider mode: loads the page again).</summary>
    public void Reload() => RunQuery();

    /// <summary>Call after adding or removing columns in in-memory mode so search and sorting see them.</summary>
    public void RefreshFields()
    {
        if (_view is not null)
        {
            var items = _view.Items;
            _view = new GridDataView<object>(BuildFields());
            _view.SetItems(items);
            RunQuery();
        }
    }

    public void GoToPage(int pageIndex) => ChangeQuery(_query.WithPage(pageIndex));

    public void ClearSearch()
    {
        _searchDelay.Stop();
        _search.Text = string.Empty;
        ApplySearchNow();
    }

    public void FocusSearch()
    {
        if (_showSearch)
        {
            _search.Focus();
            _search.SelectAll();
        }
    }

    /// <summary>Selects the row whose item equals <paramref name="item"/> on the current page.</summary>
    public bool SelectItem(object? item)
    {
        if (item is null)
        {
            return false;
        }

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (Equals(row.Tag, item))
            {
                SelectRow(row.Index);
                return true;
            }
        }

        return false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F when _showSearch:
                FocusSearch();
                return true;
            case Keys.Control | Keys.PageDown when _page.HasNext:
                GoToPage(_page.PageIndex + 1);
                return true;
            case Keys.Control | Keys.PageUp when _page.HasPrevious:
                GoToPage(_page.PageIndex - 1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected virtual void OnSelectionChanged(EventArgs e) => SelectionChanged?.Invoke(this, e);

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = ThemeManager.Current;
        var radius = _cornerRadius < 0 ? theme.Spacing.RadiusLg : _cornerRadius;
        e.Graphics.Clear(Parent?.BackColor ?? theme.Colors.Background);
        e.Graphics.UseHighQuality();
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        e.Graphics.FillRoundedRectangle(theme.Colors.Surface, bounds, this.ScaleDpi(radius));
        e.Graphics.DrawRoundedRectangle(theme.Colors.Border, bounds, this.ScaleDpi(radius));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyMetrics();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        base.OnHandleDestroyed(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyMetrics();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchDelay.Dispose();
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- Internals ---------------------------------------------------------------------------------------

    private DataGridViewTextBoxColumn CreateColumn(string property, string header, int width, bool searchable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        var column = new DataGridViewTextBoxColumn
        {
            Name = property,
            DataPropertyName = property,
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.Programmatic,
        };
        if (width <= 0)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.MinimumWidth = this.ScaleDpi(120);
        }
        else
        {
            column.Width = this.ScaleDpi(width);
        }

        if (searchable)
        {
            _nonSearchable.Remove(property);
        }
        else
        {
            _nonSearchable.Add(property);
        }

        return column;
    }

    private List<GridField<object>> BuildFields()
    {
        var fields = new List<GridField<object>>();
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            var path = column.DataPropertyName;
            if (string.IsNullOrEmpty(path) || fields.Any(f => f.Key == path))
            {
                continue;
            }

            var style = column.DefaultCellStyle;
            fields.Add(new GridField<object>(
                path,
                item => PropertyPath.GetValue(item, path),
                Searchable: !_nonSearchable.Contains(path),
                SearchText: item => FormatForSearch(PropertyPath.GetValue(item, path), style)));
        }

        return fields;
    }

    /// <summary>Searches what the user sees: "32.000 ₫", "19/10/2026".</summary>
    private static string? FormatForSearch(object? value, DataGridViewCellStyle style) =>
        value is IFormattable formattable && !string.IsNullOrEmpty(style.Format)
            ? formattable.ToString(style.Format, style.FormatProvider ?? CultureInfo.CurrentCulture)
            : value?.ToString();

    private void ApplySearchNow()
    {
        _searchDelay.Stop();
        var search = _search.Text.Trim();
        if (search != _query.Search)
        {
            ChangeQuery(_query.WithSearch(search));
        }
    }

    private void ChangeQuery(GridQuery query)
    {
        if (query == _query)
        {
            return;
        }

        _query = query;
        QueryChanged?.Invoke(this, new GridQueryEventArgs(query));
        RunQuery();
    }

    // async void on purpose: an unhandled provider failure reaches Application.ThreadException.
    private async void RunQuery()
    {
        var version = ++_loadVersion;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;

        if (_view is not null)
        {
            ShowPage(_view.Execute(_query));
            return;
        }

        if (_dataProvider is null)
        {
            ShowPage(GridPage<object>.Empty(_query.PageSize));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        SetLoading(true);
        try
        {
            var page = await _dataProvider(_query, cancellation.Token);
            if (version == _loadVersion && !IsDisposed)
            {
                ShowPage(page);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer query replaced this one.
        }
        catch (Exception ex) when (version == _loadVersion)
        {
            _summary.Text = "Không tải được dữ liệu";
            if (LoadFailed is null)
            {
                throw;
            }

            LoadFailed(this, new ThreadExceptionEventArgs(ex));
        }
        finally
        {
            if (version == _loadVersion && !IsDisposed)
            {
                SetLoading(false);
            }
        }
    }

    private void ShowPage(GridPage<object> page)
    {
        var selected = SelectedItem;
        _page = page;
        if (page.PageIndex != _query.PageIndex || page.PageSize != _query.PageSize)
        {
            _query = _query with { PageIndex = page.PageIndex, PageSize = page.PageSize };
        }

        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            if (page.Items.Count > 0)
            {
                var columns = _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.Index).ToArray();
                var rows = new DataGridViewRow[page.Items.Count];
                for (var i = 0; i < rows.Length; i++)
                {
                    var item = page.Items[i];
                    var row = new DataGridViewRow { Tag = item };
                    row.CreateCells(_grid, columns.Select(c => PropertyPath.GetValue(item, c.DataPropertyName)).ToArray());
                    rows[i] = row;
                }

                _grid.Rows.AddRange(rows);
            }
        }
        finally
        {
            _grid.ResumeLayout();
        }

        UpdateSortGlyphs();
        if (!SelectItem(selected) && _grid.RowCount > 0)
        {
            SelectRow(0);
        }

        _summary.Text = page.Summary;
        RebuildPager();
        _grid.Invalidate();
        OnSelectionChanged(EventArgs.Empty);
        PageLoaded?.Invoke(this, EventArgs.Empty);
    }

    private void SelectRow(int index)
    {
        _grid.ClearSelection();
        var firstVisible = _grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
        if (firstVisible is not null)
        {
            _grid.CurrentCell = _grid.Rows[index].Cells[firstVisible.Index];
        }

        _grid.Rows[index].Selected = true;
    }

    private void SetLoading(bool loading)
    {
        _isLoading = loading;
        UseWaitCursor = loading;
        if (loading)
        {
            _summary.Text = "Đang tải…";
        }
    }

    private void UpdateSortGlyphs()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.SortMode == DataGridViewColumnSortMode.NotSortable)
            {
                continue;
            }

            column.HeaderCell.SortGlyphDirection = _query.IsSorted && column.DataPropertyName == _query.SortKey
                ? (_query.SortDirection == SortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending)
                : SortOrder.None;
        }
    }

    private void OnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (column.SortMode == DataGridViewColumnSortMode.NotSortable || string.IsNullOrEmpty(column.DataPropertyName))
        {
            return;
        }

        ChangeQuery(_query.ToggleSort(column.DataPropertyName));
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None && _grid.CurrentRow is not null)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            RaiseRowActivated(_grid.CurrentRow.Index);
        }
    }

    private void OnGridKeyPress(object? sender, KeyPressEventArgs e)
    {
        // Typing in the grid starts a search, like in Explorer.
        if (_showSearch && !char.IsControl(e.KeyChar) && !char.IsWhiteSpace(e.KeyChar))
        {
            e.Handled = true;
            _search.Focus();
            _search.Text += e.KeyChar;
            _search.InnerTextBox.SelectionStart = _search.Text.Length;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape when _search.Text.Length > 0:
                e.Handled = true;
                e.SuppressKeyPress = true;
                ClearSearch();
                break;
            case Keys.Down when _grid.RowCount > 0:
                e.Handled = true;
                ApplySearchNow();
                _grid.Focus();
                break;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (!_syncingPageSize && _pageSize.SelectedItem is PageSizeItem item)
        {
            ChangeQuery(_query.WithPageSize(item.Size));
        }
    }

    private void SyncPageSizeCombo()
    {
        _syncingPageSize = true;
        try
        {
            var items = _pageSize.Items.Cast<PageSizeItem>().ToList();
            if (items.All(i => i.Size != _query.PageSize))
            {
                _pageSize.Items.Add(new PageSizeItem(_query.PageSize));
                items = _pageSize.Items.Cast<PageSizeItem>().ToList();
            }

            _pageSize.SelectedIndex = items.FindIndex(i => i.Size == _query.PageSize);
        }
        finally
        {
            _syncingPageSize = false;
        }
    }

    private void RaiseRowActivated(int rowIndex)
    {
        if (rowIndex >= 0 && rowIndex < _grid.RowCount && _grid.Rows[rowIndex].Tag is { } item)
        {
            RowActivated?.Invoke(this, new GridRowEventArgs(item, rowIndex));
        }
    }

    private void RebuildPager()
    {
        _pager.SuspendLayout();
        try
        {
            foreach (Control control in _pager.Controls.Cast<Control>().ToList())
            {
                _pager.Controls.Remove(control);
                if (!ReferenceEquals(control, _pageSize))
                {
                    control.Dispose();
                }
            }

            var page = _page;
            _pager.Controls.Add(PagerButton(Glyphs.First, "Trang đầu", 0, page.HasPrevious));
            _pager.Controls.Add(PagerButton(Glyphs.ChevronLeft, "Trang trước", page.PageIndex - 1, page.HasPrevious));
            foreach (var link in PageLinks.Build(page.PageIndex, page.PageCount))
            {
                if (link is null)
                {
                    _pager.Controls.Add(new Label
                    {
                        Text = "…",
                        AutoSize = false,
                        Size = new Size(this.ScaleDpi(20), this.ScaleDpi(32)),
                        TextAlign = ContentAlignment.MiddleCenter,
                        ForeColor = ThemeManager.Current.Colors.TextMuted,
                        BackColor = Color.Transparent,
                        Margin = new Padding(0),
                    });
                    continue;
                }

                var index = link.Value;
                var button = PagerButton(null, $"Trang {index + 1}", index, enabled: true);
                button.Text = (index + 1).ToString(Vietnamese);
                button.Width = this.ScaleDpi(Math.Max(32, 14 + (button.Text.Length * 8)));
                button.Variant = index == page.PageIndex ? ButtonVariant.Primary : ButtonVariant.Ghost;
                _pager.Controls.Add(button);
            }

            _pager.Controls.Add(PagerButton(Glyphs.ChevronRight, "Trang sau", page.PageIndex + 1, page.HasNext));
            _pager.Controls.Add(PagerButton(Glyphs.Last, "Trang cuối", page.PageCount - 1, page.HasNext));
            _pager.Controls.Add(_pageSize);
        }
        finally
        {
            _pager.ResumeLayout(true);
        }
    }

    private ModernButton PagerButton(string? glyph, string tooltip, int pageIndex, bool enabled)
    {
        var size = this.ScaleDpi(32);
        var button = new ModernButton
        {
            Variant = ButtonVariant.Ghost,
            IconGlyph = glyph,
            Text = string.Empty,
            Size = new Size(size, size),
            Margin = new Padding(this.ScaleDpi(2), 0, 0, 0),
            Enabled = enabled,
            TabStop = false,
            AccessibleName = tooltip,
        };
        button.Click += (_, _) => GoToPage(pageIndex);
        return button;
    }

    private void ApplyTheme(Theme theme)
    {
        BackColor = theme.Colors.Surface;
        ForeColor = theme.Colors.TextPrimary;
        _summary.ForeColor = theme.Colors.TextSecondary;
        _summary.Font = ThemeFonts.Get(FontRole.Body);
        _grid.ApplyColors(theme);
        foreach (var child in new Control[] { _search, _pageSize })
        {
            ((IThemeable)child).ApplyTheme(theme);
        }

        foreach (Control control in _toolbarItems.Controls)
        {
            ThemeManager.Apply(control);
        }

        foreach (Control control in _pager.Controls)
        {
            switch (control)
            {
                case IThemeable themeable:
                    themeable.ApplyTheme(theme);
                    break;
                case Label label:
                    label.ForeColor = theme.Colors.TextMuted;
                    break;
            }
        }

        Invalidate(true);
    }

    private void ApplyMetrics()
    {
        _toolbar.Height = this.ScaleDpi(ThemeManager.Current.Spacing.ControlHeight + 20);
        _footer.Height = this.ScaleDpi(44);
        _search.Width = this.ScaleDpi(320);
        UpdateToolbarVisibility();
    }

    private void UpdateToolbarVisibility() =>
        _toolbar.Visible = _showSearch || _toolbarItems.Controls.Count > 0;

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        if (!IsDisposed)
        {
            ApplyTheme(e.Current);
        }
    }

    private sealed record PageSizeItem(int Size)
    {
        public override string ToString() => $"{Size} / trang";
    }
}

public sealed class GridRowEventArgs(object item, int rowIndex) : EventArgs
{
    public object Item { get; } = item;

    public int RowIndex { get; } = rowIndex;
}

public sealed class GridQueryEventArgs(GridQuery query) : EventArgs
{
    public GridQuery Query { get; } = query;
}
