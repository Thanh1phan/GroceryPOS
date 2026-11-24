using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Auditing;

/// <summary>The Nhật ký hệ thống screen as seen by its presenter.</summary>
public interface IAuditLogView : IView
{
    /// <summary>Fills the user filter ("Mọi người dùng" + <paramref name="usernames"/>).</summary>
    void ShowUsers(IReadOnlyList<string> usernames);

    /// <summary>Puts the filter drop-downs on these values without reporting them back as user changes.</summary>
    void ShowFilters(AuditLogFilters filters);

    void ShowCards(AuditLogCards cards);

    void ShowStatus(string text, Tone tone);

    void ShowDetail(AuditLogDetail detail);

    /// <summary>
    /// (Re)loads the grid through <see cref="AuditLogPresenter.LoadPageAsync"/>; <paramref name="firstPage"/> goes back to
    /// page 1 (after a filter change). The first call starts the grid.
    /// </summary>
    void ReloadEntries(bool firstPage);

    /// <summary>Puts <paramref name="text"/> in the grid's search box (which reloads the grid).</summary>
    void SetSearch(string text);

    /// <summary>Clears the grid's search box (the grid reloads page 1 when it had text).</summary>
    void ClearSearch();

    /// <summary>The grid's current search, sort and page.</summary>
    AuditGridRequest CurrentGrid { get; }

    Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message);

    /// <summary>Asks where to save the workbook and writes it; the saved path, or null when cancelled or failed.</summary>
    Task<string?> SaveExcelAsync(string suggestedFileName, byte[] content);

    void ToastWarning(string message, string? title = null);
}

/// <summary>
/// Nhật ký hệ thống logic (<c>auditlog.view</c>): period / group / severity / user filters and KPI-card shortcuts over a
/// server-paged grid, "latest wins" KPI loading, the detail card with "Cùng người dùng" / "Cùng đối tượng", and Excel
/// export of everything that matches (one export at a time; the export is itself audited, so the list reloads).
/// </summary>
public sealed class AuditLogPresenter : Presenter<IAuditLogView>
{
    public const string Title = "Nhật ký hệ thống";

    public static readonly IReadOnlyList<FilterOption<AuditCategory?>> CategoryOptions =
    [
        new(null, "Tất cả nhóm"),
        .. AuditCatalog.Categories.Select(c => new FilterOption<AuditCategory?>(c, AuditCatalog.CategoryName(c))),
    ];

    public static readonly IReadOnlyList<FilterOption<AuditSeverity?>> SeverityOptions =
    [
        new(null, "Mọi mức độ"),
        new(AuditSeverity.Warning, "Từ cần chú ý"),
        new(AuditSeverity.Critical, "Chỉ quan trọng"),
    ];

    private readonly TimeProvider _clock;
    private IReadOnlyList<string> _usernames = [];
    private ReportPeriod? _period;
    private string _periodText = string.Empty;
    private AuditLogEntryDto? _selected;
    private long _summaryVersion;
    private bool _exporting;

    public AuditLogPresenter(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, TimeProvider clock)
        : base(scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        CanView = currentUser.HasPermission(Permissions.AuditLogView);
    }

    public bool CanView { get; }

    public AuditLogFilters Filters { get; private set; } = AuditLogFilters.None;

    public ReportPeriod? Period => _period;

    public IReadOnlyList<string> Usernames => _usernames;

    public AuditLogEntryDto? Selected => _selected;

    public bool IsExporting => _exporting;

    protected override void OnAttached()
    {
        View.ShowCards(AuditLogCards.Empty);
        View.ShowDetail(AuditLogDetail.None);
        View.ShowFilters(Filters);
        if (!CanView)
        {
            View.ShowStatus(AuditLogErrors.Denied.Message, Tone.Danger);
        }
    }

    /// <summary>First load and F5: the user list, the KPI cards and the grid for <paramref name="period"/>.</summary>
    public async Task LoadAsync(ReportPeriod period, string periodText)
    {
        ArgumentNullException.ThrowIfNull(period);
        _period = period;
        _periodText = periodText ?? string.Empty;
        if (!CanView)
        {
            return;
        }

        await LoadUsersAsync();
        if (!ViewAlive)
        {
            return;
        }

        var summary = LoadSummaryAsync();
        View.ReloadEntries(firstPage: false);
        await summary;
    }

    /// <summary>The period picker changed: cards and grid (back to page 1).</summary>
    public Task SetPeriodAsync(ReportPeriod period, string periodText)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period == _period)
        {
            return Task.CompletedTask;
        }

        _period = period;
        _periodText = periodText ?? string.Empty;
        if (!CanView)
        {
            return Task.CompletedTask;
        }

        View.ReloadEntries(firstPage: true);
        return LoadSummaryAsync();
    }

    /// <summary>A drop-down changed. Choosing a group or severity ends a KPI-card shortcut.</summary>
    public void SetCategory(AuditCategory? category) =>
        Apply(Filters with { Category = category, QuickFilter = AuditQuickFilter.None }, updateDropDowns: false);

    public void SetSeverity(AuditSeverity? severity) =>
        Apply(Filters with { MinSeverity = severity, QuickFilter = AuditQuickFilter.None }, updateDropDowns: false);

    public void SetUser(string? username) =>
        Apply(Filters with { Username = string.IsNullOrWhiteSpace(username) ? null : username }, updateDropDowns: false);

    /// <summary>A KPI card was clicked: exactly the actions that card counts (the "Tổng sự kiện" card clears them).</summary>
    public void ApplyQuickFilter(AuditQuickFilter filter) =>
        Apply(Filters with { Category = null, MinSeverity = null, QuickFilter = filter }, updateDropDowns: true);

    /// <summary>Bỏ lọc: every filter and the search box.</summary>
    public void ClearFilters()
    {
        var hadSearch = !string.IsNullOrWhiteSpace(View.CurrentGrid.Search);
        Filters = AuditLogFilters.None;
        View.ShowFilters(Filters);
        if (hadSearch)
        {
            View.ClearSearch(); // reloads with the cleared filters
        }
        else
        {
            Reload();
        }
    }

    /// <summary>Cùng người dùng: only the selected entry's user.</summary>
    public void FilterBySelectedUser()
    {
        if (_selected is { } entry && _usernames.Contains(entry.Username, StringComparer.Ordinal))
        {
            Apply(Filters with { Username = entry.Username }, updateDropDowns: true);
        }
    }

    /// <summary>Cùng đối tượng: searches the selected entry's object id (invoice number, product code…).</summary>
    public void FilterBySelectedEntity()
    {
        if (_selected is { EntityId: { Length: > 0 } id })
        {
            View.SetSearch(id);
        }
    }

    public void Select(AuditLogEntryDto? entry)
    {
        _selected = entry;
        View.ShowDetail(entry is null
            ? AuditLogDetail.None
            : AuditLogDetail.Of(entry, _usernames.Contains(entry.Username, StringComparer.Ordinal)));
    }

    /// <summary>The grid's data provider. Null when the page could not be loaded (the reason is in the status line).</summary>
    public async Task<AuditLogPageDto?> LoadPageAsync(AuditGridRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanView || _period is null)
        {
            return null;
        }

        var query = BuildQuery(request);
        var result = await UseAsync<IAuditLogService, Result<AuditLogPageDto>>(s => s.SearchAsync(query, cancellationToken));
        if (!ViewAlive || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (result.IsFailure)
        {
            View.ShowStatus(result.Error.Message, Tone.Danger);
            return null;
        }

        View.ShowStatus(StatusText(result.Value.TotalCount, query), Tone.Muted);
        return result.Value;
    }

    /// <summary>
    /// Xuất Excel (Ctrl+E): everything matching the filters and search. The export is audited, so afterwards the list
    /// and cards reload (also when saving was cancelled – the entry exists either way).
    /// </summary>
    public async Task ExportAsync()
    {
        if (!CanView)
        {
            View.ShowWarning(AuditLogErrors.Denied.Message, Title);
            return;
        }

        if (_exporting || _period is null)
        {
            return;
        }

        _exporting = true;
        try
        {
            var query = BuildQuery(View.CurrentGrid);
            var result = await View.RunBusyAsync(
                () => UseAsync<IAuditLogService, Result<ReportFile>>(s => s.ExportAsync(query)),
                "Đang xuất nhật ký…");
            if (!ViewAlive)
            {
                return;
            }

            if (result.IsFailure)
            {
                View.ShowWarning(result.Error.Message, Title);
                return;
            }

            var path = await View.SaveExcelAsync(result.Value.FileName, result.Value.Content);
            if (!ViewAlive)
            {
                return;
            }

            if (path is not null)
            {
                View.ToastSuccess($"Đã lưu {Path.GetFileName(path)}.", "Xuất nhật ký");
            }

            View.ReloadEntries(firstPage: false);
            await LoadSummaryAsync();
        }
        finally
        {
            _exporting = false;
        }
    }

    /// <summary>The query the grid and the export run (paging from <paramref name="grid"/>).</summary>
    public AuditLogQuery BuildQuery(AuditGridRequest grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var period = _period ?? throw new InvalidOperationException("No period yet – call LoadAsync first.");
        return new AuditLogQuery(
            period,
            Category: Filters.Category,
            MinSeverity: Filters.MinSeverity,
            Username: Filters.Username,
            Search: string.IsNullOrWhiteSpace(grid.Search) ? null : grid.Search.Trim(),
            OldestFirst: grid.OldestFirst,
            PageIndex: grid.PageIndex,
            PageSize: grid.PageSize,
            Actions: AuditLogFilters.ActionsOf(Filters.QuickFilter));
    }

    private string StatusText(int total, AuditLogQuery query)
    {
        var text = $"{_periodText} · {AuditLogCards.N(total)} sự kiện khớp bộ lọc · cập nhật lúc {_clock.GetLocalNow():HH:mm}";
        return AuditLogService.DescribeFilters(query) is { } filters ? $"{text}{Environment.NewLine}Bộ lọc: {filters}" : text;
    }

    private void Apply(AuditLogFilters filters, bool updateDropDowns)
    {
        if (filters == Filters)
        {
            return;
        }

        Filters = filters;
        if (updateDropDowns)
        {
            View.ShowFilters(filters);
        }

        Reload();
    }

    private void Reload()
    {
        if (CanView && _period is not null)
        {
            View.ReloadEntries(firstPage: true);
        }
    }

    private async Task LoadUsersAsync()
    {
        var result = await UseAsync<IAuditLogService, Result<IReadOnlyList<string>>>(s => s.ListUsernamesAsync());
        if (!ViewAlive)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ToastError(result.Error.Message, "Không tải được danh sách người dùng");
            return;
        }

        _usernames = result.Value;
        View.ShowUsers(_usernames);

        // A user filter that no longer exists (renamed, log cleaned up) falls back to everyone.
        if (Filters.Username is { } user && !_usernames.Contains(user, StringComparer.Ordinal))
        {
            Filters = Filters with { Username = null };
        }

        View.ShowFilters(Filters);
    }

    private async Task LoadSummaryAsync()
    {
        if (_period is not { } period)
        {
            return;
        }

        var version = ++_summaryVersion;
        var result = await UseAsync<IAuditLogService, Result<AuditLogSummaryDto>>(s => s.GetSummaryAsync(period));
        if (!ViewAlive || version != _summaryVersion)
        {
            return;
        }

        if (result.IsFailure)
        {
            View.ShowCards(AuditLogCards.Empty);
            View.ShowStatus(result.Error.Message, Tone.Danger);
            return;
        }

        View.ShowCards(AuditLogCards.From(result.Value));
    }
}
