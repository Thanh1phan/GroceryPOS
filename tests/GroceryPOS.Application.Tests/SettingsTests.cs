using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Auditing;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Application.Sales.Receipts;
using GroceryPOS.Application.Settings;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Application.Validation;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Settings;

namespace GroceryPOS.Application.Tests;

public class StoreSettingsCodecTests
{
    private static readonly StoreSettings Custom = new()
    {
        StoreName = "Tạp hóa Cô Ba",
        Address = "12 Nguyễn Trãi, Q.1",
        Phone = "0909123456",
        TaxCode = "0312345678",
        ReceiptFooter = "Cảm ơn quý khách!\nĐổi trả trong 3 ngày",
        ReceiptPaperWidthMm = 58,
        PreviewReceiptAfterCheckout = false,
        VndPerPoint = 20_000,
        PointValueVnd = 200,
        ReturnWindowDays = 3,
        AllowNegativeStock = false,
        RequireOpenShift = false,
        ShiftVarianceNoteThreshold = 10_000,
    };

    [Fact]
    public void Values_round_trip_through_the_key_value_rows()
    {
        var values = StoreSettingsCodec.ToValues(Custom);

        Assert.Equal(StoreSettingsCodec.AllKeys.Count, values.Count);
        Assert.Equal("20000", values[StoreSettingsCodec.Keys.VndPerPoint]);
        Assert.Equal("false", values[StoreSettingsCodec.Keys.RequireOpenShift]);
        Assert.Equal(Custom, StoreSettingsCodec.Read(values, new StoreSettings()));
    }

    [Fact]
    public void Missing_and_unreadable_values_keep_the_defaults_and_are_reported()
    {
        var defaults = new StoreSettings { StoreName = "Mặc định", ReturnWindowDays = 7 };
        var rejected = new List<string>();
        var values = new Dictionary<string, string>
        {
            [StoreSettingsCodec.Keys.StoreName] = "   ",
            [StoreSettingsCodec.Keys.ReturnWindowDays] = "bảy",
            [StoreSettingsCodec.Keys.ReceiptPaperWidth] = "72",
            [StoreSettingsCodec.Keys.VndPerPoint] = "-5",
            [StoreSettingsCodec.Keys.AllowNegativeStock] = "có",
            [StoreSettingsCodec.Keys.PointValue] = "150",
            ["unknown.key"] = "x",
        };

        var settings = StoreSettingsCodec.Read(values, defaults, rejected);

        Assert.Equal("Mặc định", settings.StoreName);
        Assert.Equal(7, settings.ReturnWindowDays);
        Assert.Equal(80, settings.ReceiptPaperWidthMm);
        Assert.Equal(defaults.VndPerPoint, settings.VndPerPoint);
        Assert.Equal(defaults.AllowNegativeStock, settings.AllowNegativeStock);
        Assert.Equal(150, settings.PointValueVnd);
        Assert.Equal(5, rejected.Count);
    }

    [Fact]
    public void Diff_lists_changes_with_vietnamese_labels_and_display_values()
    {
        var before = new StoreSettings();
        var after = before with { VndPerPoint = 0, ReturnWindowDays = 3, AllowNegativeStock = false, StoreName = "Cô Ba" };

        var changes = StoreSettingsCodec.Diff(before, after);

        Assert.Equal(
            [StoreSettingsCodec.Keys.StoreName, StoreSettingsCodec.Keys.VndPerPoint, StoreSettingsCodec.Keys.ReturnWindowDays, StoreSettingsCodec.Keys.AllowNegativeStock],
            changes.Select(c => c.Key));
        Assert.Equal("Số tiền cho 1 điểm: 10.000 ₫ → tắt tích điểm", changes[1].ToString());
        Assert.Equal("Hạn đổi trả: 7 ngày → 3 ngày", changes[2].ToString());
        Assert.Equal("Cho bán khi hết tồn kho: bật → tắt", changes[3].ToString());
        Assert.Empty(StoreSettingsCodec.Diff(after, after with { }));
    }

    [Fact]
    public void Audit_text_names_the_section_and_is_capped()
    {
        var changes = StoreSettingsCodec.Diff(new StoreSettings(), new StoreSettings { StoreName = "Cô Ba" });

        Assert.Equal("Cửa hàng – Tên cửa hàng: \"Tạp hóa GroceryPOS\" → \"Cô Ba\"", StoreSettingsCodec.DescribeForAudit(changes));
        Assert.Equal(20, StoreSettingsCodec.DescribeForAudit(changes, maxLength: 20).Length);
    }

    [Fact]
    public void Settings_map_onto_store_profile_and_sales_options_keeping_other_fields()
    {
        var store = Custom.ApplyTo(new StoreProfile { Name = "Cũ" });
        var sales = Custom.ApplyTo(SalesOptions.Default with { InvoicePrefix = "BL" });

        Assert.Equal("Tạp hóa Cô Ba", store.Name);
        Assert.Equal(58, store.ReceiptPaperWidthMm);
        Assert.False(store.PreviewReceiptAfterCheckout);
        Assert.Equal("BL", sales.InvoicePrefix);
        Assert.Equal(new LoyaltyPolicy(20_000, 200), sales.Loyalty);
        Assert.Equal(3, sales.ReturnWindowDays);
        Assert.False(sales.RequireOpenShift);
        Assert.Equal(Custom, StoreSettings.From(store, sales));
    }

    [Fact]
    public void Normalize_trims_fields_and_unifies_footer_lines()
    {
        var normalized = new StoreSettings
        {
            StoreName = "  Cô Ba ",
            Address = "   ",
            ReceiptFooter = "\r\n  Cảm ơn quý khách!  \r\nHẹn gặp lại\r\n\r\n",
        }.Normalize();

        Assert.Equal("Cô Ba", normalized.StoreName);
        Assert.Null(normalized.Address);
        Assert.Equal("Cảm ơn quý khách!\nHẹn gặp lại", normalized.ReceiptFooter);
        Assert.Equal(2, StoreSettings.FooterLineCount(normalized.ReceiptFooter));
        Assert.Equal(0, StoreSettings.FooterLineCount("  "));
    }
}

public class StoreSettingsValidatorTests
{
    private readonly StoreSettingsValidator _validator = new();

    private IReadOnlyDictionary<string, string[]> Errors(StoreSettings settings) => _validator.Validate(settings).ToFieldErrors();

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(_validator.Validate(new StoreSettings()).IsValid);
    }

    [Fact]
    public void Store_identity_is_checked()
    {
        var errors = Errors(new StoreSettings { StoreName = " ", Phone = "12ab", TaxCode = "12345" });

        Assert.Contains(nameof(StoreSettings.StoreName), errors.Keys);
        Assert.Contains(nameof(StoreSettings.Phone), errors.Keys);
        Assert.Contains(nameof(StoreSettings.TaxCode), errors.Keys);
    }

    [Fact]
    public void Footer_is_limited_in_lines_and_paper_to_58_or_80()
    {
        var errors = Errors(new StoreSettings { ReceiptFooter = "1\n2\n3\n4\n5\n6\n7", ReceiptPaperWidthMm = 76 });

        Assert.Equal("Lời cảm ơn tối đa 6 dòng.", errors[nameof(StoreSettings.ReceiptFooter)][0]);
        Assert.Contains(nameof(StoreSettings.ReceiptPaperWidthMm), errors.Keys);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 500, true)]
    [InlineData(10_000, 100, true)]
    [InlineData(10_000, 10_000, true)]
    [InlineData(10_000, 10_001, false)]
    [InlineData(500, 0, false)]
    [InlineData(10_000.5, 0, false)]
    public void Loyalty_rules(decimal vndPerPoint, decimal pointValue, bool valid)
    {
        Assert.Equal(valid, _validator.Validate(new StoreSettings { VndPerPoint = vndPerPoint, PointValueVnd = pointValue }).IsValid);
    }

    [Fact]
    public void Sales_rules_have_ranges()
    {
        var errors = Errors(new StoreSettings { ReturnWindowDays = 91, ShiftVarianceNoteThreshold = -1 });

        Assert.Contains(nameof(StoreSettings.ReturnWindowDays), errors.Keys);
        Assert.Contains(nameof(StoreSettings.ShiftVarianceNoteThreshold), errors.Keys);
    }
}

public class SettingsTextTests
{
    [Fact]
    public void Loyalty_summary_explains_earning_redeeming_and_rebate()
    {
        var lines = SettingsText.LoyaltySummary(10_000, 100);

        Assert.Equal(
        [
            "Mỗi 10.000 ₫ được 1 điểm – hóa đơn 250.000 ₫ được 25 điểm.",
            "Đổi 100 điểm được giảm 10.000 ₫.",
            "Tương đương hoàn lại 1% giá trị mua hàng.",
        ], lines);
    }

    [Fact]
    public void Loyalty_summary_when_parts_are_off()
    {
        Assert.Single(SettingsText.LoyaltySummary(0, 0));
        Assert.Equal(
            ["Mỗi 20.000 ₫ được 1 điểm – hóa đơn 250.000 ₫ được 12 điểm.", "Không cho đổi điểm lấy giảm giá."],
            SettingsText.LoyaltySummary(20_000, 0));
        Assert.Equal("Không cộng điểm khi mua hàng (khách chỉ dùng điểm đang có).", SettingsText.LoyaltySummary(0, 100)[0]);
        Assert.Equal(1.5m, SettingsText.RebatePercent(20_000, 300));
        Assert.Null(SettingsText.RebatePercent(0, 300));
    }

    [Fact]
    public void Warnings_flag_risky_choices()
    {
        Assert.Single(SettingsText.Warnings(new StoreSettings())); // negative stock is allowed by default

        var risky = SettingsText.Warnings(new StoreSettings { VndPerPoint = 1_000, PointValueVnd = 200, RequireOpenShift = false, ReturnWindowDays = 0 });
        Assert.Equal(4, risky.Count);
        Assert.StartsWith("Tỷ lệ hoàn lại 20%", risky[0]);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(58)]
    public void Sample_receipt_uses_the_edited_store_and_loyalty_and_fits_the_paper(int paper)
    {
        var store = new StoreProfile { Name = "Tạp hóa Cô Ba", ReceiptFooter = "Cảm ơn quý khách!\nHẹn gặp lại" };
        var receipt = SettingsText.SampleReceipt(store, new LoyaltyPolicy(10_000, 100), new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.FromHours(7)));
        var columns = ReceiptFormatter.ColumnsFor(paper);
        var lines = ReceiptFormatter.Format(receipt, columns);
        var text = ReceiptFormatter.ToText(lines);

        Assert.Equal(SettingsText.ExampleBasket, receipt.Total);
        Assert.Equal("HD261105-0001", receipt.Code);
        Assert.Equal(25, receipt.Member!.PointsEarned);
        Assert.All(lines, l => Assert.Equal(columns, l.Text.Length));
        Assert.Equal("TẠP HÓA CÔ BA", lines[0].Text.Trim());
        Assert.Contains("Hẹn gặp lại", text);
        Assert.Null(SettingsText.SampleReceipt(store, new LoyaltyPolicy(0, 0), DateTimeOffset.Now).Member);
    }
}

public class SettingsServiceTests
{
    private sealed class InMemorySettings : IAppSettingRepository
    {
        public List<AppSetting> Rows { get; } = new();

        public Task<AppSetting?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<AppSetting>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppSetting>>(Rows.ToList());

        public void Add(AppSetting entity) => Rows.Add(entity);

        public void Remove(AppSetting entity) => Rows.Remove(entity);
    }

    private static (SettingsService Sut, InMemorySettings Repo, StoreSettingsState State, RecordingAuditService Audit, FakeUnitOfWork Uow) Create(
        params string[] permissions)
    {
        var session = new UserSessionStore();
        session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), DateTimeOffset.UnixEpoch));
        var repo = new InMemorySettings();
        var state = new StoreSettingsState(new StoreProfile { Name = "Từ appsettings", Phone = "0909000000" }, SalesOptions.Default);
        var audit = new RecordingAuditService();
        var uow = new FakeUnitOfWork();
        return (new SettingsService(repo, state, session, audit, uow), repo, state, audit, uow);
    }

    [Fact]
    public async Task Requires_settings_permission()
    {
        var (sut, _, _, _, _) = Create(Permissions.ReportsView);

        Assert.Equal(SettingsErrors.Denied, (await sut.GetAsync()).Error);
        Assert.Equal(SettingsErrors.Denied, (await sut.SaveAsync(new StoreSettings())).Error);
        Assert.Equal(SettingsErrors.Denied, sut.GetDefaults().Error);

        var anonymous = new SettingsService(new InMemorySettings(), new StoreSettingsState(), new UserSessionStore(), new RecordingAuditService(), new FakeUnitOfWork());
        Assert.Equal("auth.required", (await anonymous.GetAsync()).Error.Code);
    }

    [Fact]
    public async Task Get_returns_appsettings_defaults_overlaid_with_stored_values()
    {
        var (sut, repo, state, _, _) = Create(Permissions.SettingsManage);
        repo.Add(new AppSetting(StoreSettingsCodec.Keys.StoreName, "Tạp hóa Cô Ba"));
        repo.Add(new AppSetting(StoreSettingsCodec.Keys.VndPerPoint, "5000"));

        var settings = (await sut.GetAsync()).Value;

        Assert.Equal("Tạp hóa Cô Ba", settings.StoreName);
        Assert.Equal("0909000000", settings.Phone);
        Assert.Equal(5_000, settings.VndPerPoint);
        Assert.Equal("Tạp hóa Cô Ba", state.Store.Name);
        Assert.Equal(5_000, state.Sales.Loyalty.VndPerPoint);
        Assert.Equal("Từ appsettings", sut.GetDefaults().Value.StoreName);
    }

    [Fact]
    public async Task Save_writes_every_key_audits_the_changes_and_applies_them()
    {
        var (sut, repo, state, audit, uow) = Create(Permissions.SettingsManage);
        var changedEvents = 0;
        state.Changed += (_, _) => changedEvents++;
        var edited = state.Settings with { StoreName = "  Tạp hóa Cô Ba ", ReturnWindowDays = 3, RequireOpenShift = false };

        var result = await sut.SaveAsync(edited);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Changes.Count);
        Assert.Equal(StoreSettingsCodec.AllKeys.Count, repo.Rows.Count);
        Assert.Equal("Tạp hóa Cô Ba", repo.Rows.Single(r => r.Key == StoreSettingsCodec.Keys.StoreName).Value);
        Assert.Equal(1, uow.SaveCount);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditActions.SettingsChanged, entry.Action);
        Assert.Contains("Hạn đổi trả: 7 ngày → 3 ngày", entry.Details);
        Assert.Equal("Tạp hóa Cô Ba", state.Store.Name);
        Assert.Equal(3, state.Sales.ReturnWindowDays);
        Assert.False(state.Sales.RequireOpenShift);
        Assert.Equal(1, changedEvents);
    }

    [Fact]
    public async Task Saving_again_updates_existing_rows_and_nothing_changed_writes_nothing()
    {
        var (sut, repo, state, audit, uow) = Create(Permissions.SettingsManage);
        await sut.SaveAsync(state.Settings with { VndPerPoint = 20_000 });

        var second = await sut.SaveAsync(state.Settings with { VndPerPoint = 50_000 });
        var none = await sut.SaveAsync(state.Settings);

        Assert.Single(second.Value.Changes);
        Assert.Equal("50000", repo.Rows.Single(r => r.Key == StoreSettingsCodec.Keys.VndPerPoint).Value);
        Assert.Equal(StoreSettingsCodec.AllKeys.Count, repo.Rows.Count);
        Assert.False(none.Value.HasChanges);
        Assert.Equal(2, uow.SaveCount);
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Invalid_settings_are_not_saved()
    {
        var (sut, repo, state, audit, uow) = Create(Permissions.SettingsManage);

        var result = await sut.SaveAsync(state.Settings with { StoreName = "", VndPerPoint = 1_000, PointValueVnd = 2_000 });

        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(repo.Rows);
        Assert.Empty(audit.Entries);
        Assert.Equal(0, uow.SaveCount);
        Assert.Equal("Từ appsettings", state.Store.Name);
    }

    [Fact]
    public void Settings_change_is_a_known_audit_action_in_the_system_group()
    {
        var info = AuditCatalog.Describe(AuditActions.SettingsChanged);

        Assert.Equal("Thay đổi cấu hình", info.Name);
        Assert.Equal(AuditCategory.System, info.Category);
        Assert.Contains(AuditCategory.System, AuditCatalog.Categories);
    }
}
