using System.Reflection;
using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Reports;
using GroceryPOS.Application.Reports.Export;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public sealed class AuditCatalogTests
{
    private static IEnumerable<string> AllActionKeys() =>
        typeof(AuditActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Every_audit_action_has_a_vietnamese_name_and_a_category()
    {
        foreach (var key in AllActionKeys())
        {
            var info = AuditCatalog.Describe(key);
            Assert.True(AuditCatalog.IsKnown(key), $"Missing catalog entry for {key}");
            Assert.NotEqual(AuditCategory.Other, info.Category);
            Assert.NotEqual(key, info.Name);
        }

        Assert.Equal(AllActionKeys().Count(), AuditCatalog.Actions.Select(a => a.Key).Distinct().Count());
    }

    [Fact]
    public void Every_category_in_the_filter_has_actions_and_a_name()
    {
        foreach (var category in AuditCatalog.Categories)
        {
            Assert.NotEmpty(AuditCatalog.ActionsIn(category));
            Assert.NotEqual("Khác", AuditCatalog.CategoryName(category));
        }
    }

    [Fact]
    public void Unknown_actions_are_shown_as_is_under_other()
    {
        var info = AuditCatalog.Describe("plugin.something");

        Assert.Equal("plugin.something", info.Name);
        Assert.Equal(AuditCategory.Other, info.Category);
        Assert.Equal(AuditSeverity.Info, info.Severity);
    }

    [Fact]
    public void Security_and_money_sensitive_actions_are_critical()
    {
        var critical = AuditCatalog.ActionsAtLeast(AuditSeverity.Critical);

        Assert.Contains(AuditActions.SaleVoided, critical);
        Assert.Contains(AuditActions.AccountLocked, critical);
        Assert.Contains(AuditActions.RoleUpdated, critical);
        Assert.DoesNotContain(AuditActions.SaleCompleted, critical);
        Assert.Contains(AuditActions.PriceChanged, AuditCatalog.ActionsAtLeast(AuditSeverity.Warning));
    }

    [Theory]
    [InlineData("Sale", "HD261102-0001", "Hóa đơn HD261102-0001")]
    [InlineData("Product", "SUA01", "Sản phẩm SUA01")]
    [InlineData("Product", null, "Sản phẩm")]
    [InlineData("Widget", "7", "Widget 7")]
    [InlineData(null, "7", "7")]
    [InlineData(null, null, null)]
    public void Entities_are_described_in_vietnamese(string? entityName, string? entityId, string? expected) =>
        Assert.Equal(expected, AuditCatalog.DescribeEntity(entityName, entityId));
}

public sealed class AuditLogServiceTests
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private static readonly ReportPeriod Nov2 = ReportPeriod.SingleDay(new DateOnly(2026, 11, 2));

    private static AuditLog Log(int id, int day, int hour, string action, string? user = "thungan1",
        string? entity = null, string? entityId = null, string? details = null)
    {
        var log = new AuditLog(new DateTimeOffset(2026, 11, day, hour, 0, 0, Vietnam), action, 2, user, entity, entityId, details);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(log, id);
        return log;
    }

    private static List<AuditLog> SampleLogs() =>
    [
        Log(1, 1, 23, AuditActions.LoginSucceeded),                       // previous day (store time)
        Log(2, 2, 0, AuditActions.LoginSucceeded),                        // first minute of Nov 2
        Log(3, 2, 8, AuditActions.SaleCompleted, entity: "Sale", entityId: "HD261102-0001", details: "3 mặt hàng – 87.000 ₫"),
        Log(4, 2, 9, AuditActions.SaleVoided, "quanly", "Sale", "HD261102-0001", "Lý do: khách đổi ý"),
        Log(5, 2, 10, AuditActions.LoginFailed, "khach", details: "Tài khoản không tồn tại"),
        Log(6, 2, 10, AuditActions.AccountLocked, "thungan2"),
        Log(7, 2, 11, AuditActions.PriceChanged, "quanly", "Product", "SUA01", "25.000 ₫ → 27.000 ₫"),
        Log(8, 2, 12, AuditActions.StockAdjusted, "thukho", "Product", "MI01", "Hư hỏng −2"),
        Log(9, 2, 23, AuditActions.Logout),                               // last hour of Nov 2
        Log(10, 3, 0, AuditActions.LoginSucceeded),                       // next day
    ];

    [Fact]
    public async Task Search_covers_the_store_day_and_lists_newest_first_with_vietnamese_names()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var page = (await sut.SearchAsync(new AuditLogQuery(Nov2))).Value;

        Assert.Equal(8, page.TotalCount);
        Assert.Equal([9, 8, 7, 6, 5, 4, 3, 2], page.Items.Select(i => i.Id));
        var voided = page.Items.Single(i => i.Id == 4);
        Assert.Equal("Hủy hóa đơn", voided.ActionName);
        Assert.Equal(AuditSeverity.Critical, voided.Severity);
        Assert.Equal("Bán hàng & trả hàng", voided.CategoryName);
        Assert.Equal("Hóa đơn HD261102-0001", voided.Entity);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), voided.Time);
    }

    [Fact]
    public async Task Oldest_first_reverses_the_order()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var page = (await sut.SearchAsync(new AuditLogQuery(Nov2, OldestFirst: true, PageSize: 3))).Value;

        Assert.Equal([2, 3, 4], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Category_severity_user_and_search_filters_combine()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var auth = (await sut.SearchAsync(new AuditLogQuery(Nov2, Category: AuditCategory.Authentication))).Value;
        Assert.Equal([9, 6, 5, 2], auth.Items.Select(i => i.Id));

        var authWarnings = (await sut.SearchAsync(new AuditLogQuery(Nov2, Category: AuditCategory.Authentication, MinSeverity: AuditSeverity.Warning))).Value;
        Assert.Equal([6, 5], authWarnings.Items.Select(i => i.Id));

        var critical = (await sut.SearchAsync(new AuditLogQuery(Nov2, MinSeverity: AuditSeverity.Critical))).Value;
        Assert.Equal([6, 4], critical.Items.Select(i => i.Id));

        var manager = (await sut.SearchAsync(new AuditLogQuery(Nov2, Username: " quanly "))).Value;
        Assert.Equal([7, 4], manager.Items.Select(i => i.Id));

        var invoice = (await sut.SearchAsync(new AuditLogQuery(Nov2, Search: "hd261102-0001"))).Value;
        Assert.Equal([4, 3], invoice.Items.Select(i => i.Id));

        var mismatch = (await sut.SearchAsync(new AuditLogQuery(Nov2, Category: AuditCategory.Sales, Action: AuditActions.PriceChanged))).Value;
        Assert.Equal(0, mismatch.TotalCount);
    }

    [Fact]
    public void Resolve_actions_returns_null_without_action_filters()
    {
        Assert.Null(AuditLogService.ResolveActions(new AuditLogQuery(Nov2, Username: "a", Search: "b")));
        Assert.Null(AuditLogService.ResolveActions(new AuditLogQuery(Nov2, Category: AuditCategory.Other, MinSeverity: AuditSeverity.Info)));
        Assert.Equal([AuditActions.SaleVoided], AuditLogService.ResolveActions(new AuditLogQuery(Nov2, Category: AuditCategory.Sales, MinSeverity: AuditSeverity.Critical)));
    }

    [Fact]
    public async Task Page_past_the_end_falls_back_to_the_last_page_and_size_is_clamped()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var page = (await sut.SearchAsync(new AuditLogQuery(Nov2, PageIndex: 9, PageSize: 3))).Value;
        Assert.Equal(2, page.PageIndex);
        Assert.Equal([3, 2], page.Items.Select(i => i.Id));

        var huge = (await sut.SearchAsync(new AuditLogQuery(Nov2, PageSize: 10_000))).Value;
        Assert.Equal(AuditLogQuery.MaxPageSize, huge.PageSize);
    }

    [Fact]
    public async Task Summary_counts_the_period_by_severity_and_kind()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var summary = (await sut.GetSummaryAsync(Nov2)).Value;

        Assert.Equal(8, summary.Total);
        Assert.Equal(2, summary.Critical);   // void + locked
        Assert.Equal(3, summary.Warning);    // failed login, price change, stock adjustment
        Assert.Equal(1, summary.FailedLogins);
        Assert.Equal(1, summary.AccountsLocked);
        Assert.Equal(1, summary.SalesExceptions);
        Assert.Equal(2, summary.StockAndPrice);
    }

    [Fact]
    public async Task Requires_sign_in_and_the_audit_log_permission_and_a_valid_period()
    {
        var (denied, _, _, _) = Create(SampleLogs(), Permissions.ReportsView);
        Assert.Equal(AuditLogErrors.Denied, (await denied.SearchAsync(new AuditLogQuery(Nov2))).Error);
        Assert.Equal(AuditLogErrors.Denied, (await denied.GetSummaryAsync(Nov2)).Error);
        Assert.Equal(AuditLogErrors.Denied, (await denied.ListUsernamesAsync()).Error);
        Assert.Equal(AuditLogErrors.Denied, (await denied.ExportAsync(new AuditLogQuery(Nov2))).Error);

        var anonymous = new AuditLogService(new InMemoryAuditLogRepository(SampleLogs()), new UserSessionStore(),
            new CapturingWriter(), new RecordingAuditService(), new FakeUnitOfWork(), new FakeClock());
        Assert.Equal(AuthErrors.NotAuthenticated, (await anonymous.SearchAsync(new AuditLogQuery(Nov2))).Error);

        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);
        var reversed = new ReportPeriod(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 1));
        Assert.Equal(ReportErrors.InvalidPeriod, (await sut.SearchAsync(new AuditLogQuery(reversed))).Error);
        var tooLong = new ReportPeriod(new DateOnly(2025, 1, 1), new DateOnly(2026, 11, 2));
        Assert.Equal("validation", (await sut.GetSummaryAsync(tooLong)).Error.Code);
    }

    [Fact]
    public async Task Usernames_include_unknown_names_from_failed_logins()
    {
        var (sut, _, _, _) = Create(SampleLogs(), Permissions.AuditLogView);

        var names = (await sut.ListUsernamesAsync()).Value;

        Assert.Equal(["khach", "quanly", "thukho", "thungan1", "thungan2"], names);
    }

    [Fact]
    public async Task Export_writes_filtered_rows_with_filter_line_and_audits_itself()
    {
        var (sut, writer, audit, uow) = Create(SampleLogs(), Permissions.AuditLogView);
        var query = new AuditLogQuery(Nov2, Category: AuditCategory.Sales, PageIndex: 5, PageSize: 1);

        var file = (await sut.ExportAsync(query)).Value;

        Assert.Equal("NhatKy_20261102.test", file.FileName);
        var sheet = Assert.Single(writer.Written!);
        Assert.Equal("NHẬT KÝ HỆ THỐNG", sheet.Title);
        Assert.Equal(2, sheet.Rows.Count); // paging ignored
        Assert.Equal("02/11/2026 09:00:00", sheet.Rows[0][0]);
        Assert.Equal("Hủy hóa đơn", sheet.Rows[0][3]);
        Assert.Contains("Bộ lọc: Nhóm: Bán hàng & trả hàng", sheet.Subtitles);
        Assert.StartsWith("Người xuất:", sheet.Subtitles[^1]);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.AuditLogExported, entry.Action);
        Assert.Contains("2 dòng", entry.Details);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Export_refuses_empty_or_oversized_results()
    {
        var (sut, _, audit, _) = Create(SampleLogs(), Permissions.AuditLogView);
        var empty = await sut.ExportAsync(new AuditLogQuery(Nov2, Search: "không-có"));
        Assert.Equal(AuditLogErrors.NothingToExport, empty.Error);

        var many = Enumerable.Range(1, AuditLogService.MaxExportRows + 1)
            .Select(i => Log(i, 2, 8, AuditActions.SaleCompleted)).ToList();
        var (big, _, bigAudit, _) = Create(many, Permissions.AuditLogView);
        var refused = await big.ExportAsync(new AuditLogQuery(Nov2));
        Assert.Equal("validation", refused.Error.Code);
        Assert.Contains("20.001", refused.Error.Message);
        Assert.Empty(audit.Entries);
        Assert.Empty(bigAudit.Entries);
    }

    [Fact]
    public void Filters_are_described_for_the_export_header()
    {
        Assert.Null(AuditLogService.DescribeFilters(new AuditLogQuery(Nov2)));
        Assert.Equal(
            "Hành động: Đổi giá bán; Chỉ sự kiện quan trọng; Người dùng: quanly; Tìm: \"SUA\"",
            AuditLogService.DescribeFilters(new AuditLogQuery(Nov2, Action: AuditActions.PriceChanged, MinSeverity: AuditSeverity.Critical, Username: "quanly", Search: " SUA ")));
    }

    private static (AuditLogService Sut, CapturingWriter Writer, RecordingAuditService Audit, FakeUnitOfWork Uow) Create(
        List<AuditLog> logs, params string[] permissions)
    {
        var clock = new FakeClock { Now = new DateTimeOffset(2026, 11, 2, 14, 5, 0, TimeSpan.Zero) };
        var session = new UserSessionStore();
        session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), clock.Now));
        var writer = new CapturingWriter();
        var audit = new RecordingAuditService();
        var uow = new FakeUnitOfWork();
        var sut = new AuditLogService(new InMemoryAuditLogRepository(logs), session, writer, audit, uow, clock,
            new StoreProfile { Name = "Tạp hóa Cô Ba" });
        return (sut, writer, audit, uow);
    }

    private sealed class CapturingWriter : IReportWorkbookWriter
    {
        public IReadOnlyList<ReportSheet>? Written { get; private set; }

        public string FileExtension => ".test";

        public Task<byte[]> WriteAsync(IReadOnlyList<ReportSheet> sheets, CancellationToken cancellationToken = default)
        {
            Written = sheets;
            return Task.FromResult(new byte[] { 1 });
        }
    }

    /// <summary>Mirrors the SQL repository: range, action list, exact user, case-insensitive substring search, ordering.</summary>
    private sealed class InMemoryAuditLogRepository(List<AuditLog> logs) : IAuditLogRepository
    {
        public Task<AuditLogSearchResult> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken = default)
        {
            var query = logs.Where(a => a.Timestamp >= filter.From && a.Timestamp < filter.To);
            if (filter.Actions is { } actions)
            {
                query = query.Where(a => actions.Contains(a.Action));
            }

            if (filter.Username is { } user)
            {
                query = query.Where(a => a.Username == user);
            }

            if (filter.Search is { } search)
            {
                query = query.Where(a => new[] { a.Username, a.EntityId, a.Details }
                    .Any(t => t?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
            }

            var matched = query.ToList();
            var ordered = filter.OldestFirst
                ? matched.OrderBy(a => a.Timestamp).ThenBy(a => a.Id)
                : matched.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);
            return Task.FromResult(new AuditLogSearchResult(ordered.Skip(filter.Skip).Take(filter.Take).ToList(), matched.Count));
        }

        public Task<IReadOnlyDictionary<string, int>> CountByActionAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(logs
                .Where(a => a.Timestamp >= from && a.Timestamp < to)
                .GroupBy(a => a.Action)
                .ToDictionary(g => g.Key, g => g.Count()));

        public Task<IReadOnlyList<string>> ListUsernamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(logs.Select(a => a.Username).OfType<string>().Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
