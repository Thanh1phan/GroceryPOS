using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Application.Tests.Fakes;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Application.Tests;

public class CategoryServiceTests
{
    private readonly InMemoryCategoryRepository _categories = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly UserSessionStore _session = new();
    private readonly RecordingAuditService _audit = new();
    private readonly FakeClock _clock = new();

    public CategoryServiceTests() => SignIn(Permissions.ProductsView, Permissions.CategoriesManage);

    private CategoryService CreateSut() => new(_categories, _uow, _session, _audit);

    private void SignIn(params string[] permissions) =>
        _session.SignIn(new UserSession(1, "admin", "Quản trị viên", SystemRoles.Admin, permissions.ToHashSet(), _clock.Now));

    [Fact]
    public async Task Create_adds_category_audits_and_saves()
    {
        var result = await CreateSut().CreateAsync(new SaveCategoryRequest("Đồ uống", "Nước giải khát"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Đồ uống", result.Value.Name);
        Assert.True(result.Value.IsActive);
        Assert.Single(_categories.All);
        Assert.Equal(AuditActions.CategoryCreated, Assert.Single(_audit.Entries).Action);
        Assert.Equal(1, _uow.SaveCount);
    }

    [Fact]
    public async Task Create_rejects_duplicate_name_case_insensitively()
    {
        await CreateSut().CreateAsync(new SaveCategoryRequest("Đồ uống", null));
        var duplicate = await CreateSut().CreateAsync(new SaveCategoryRequest("đồ UỐNG", null));

        Assert.Equal("conflict", duplicate.Error.Code);
        Assert.Single(_categories.All);
    }

    [Fact]
    public async Task Update_renames_category_without_tripping_on_its_own_name()
    {
        var created = await CreateSut().CreateAsync(new SaveCategoryRequest("Đồ uống", null));

        var result = await CreateSut().UpdateAsync(created.Value.Id, new SaveCategoryRequest("Đồ uống", "Cập nhật mô tả"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Cập nhật mô tả", result.Value.Description);
        Assert.Contains(AuditActions.CategoryUpdated, _audit.Actions);
    }

    [Fact]
    public async Task Update_missing_category_returns_not_found()
    {
        var result = await CreateSut().UpdateAsync(999, new SaveCategoryRequest("Đồ uống", null));
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Deactivate_then_activate_toggles_status_and_audits()
    {
        var created = await CreateSut().CreateAsync(new SaveCategoryRequest("Đồ uống", null));

        var deactivated = await CreateSut().DeactivateAsync(created.Value.Id);
        Assert.True(deactivated.IsSuccess);
        Assert.False((await CreateSut().ListAsync()).Value.Single().IsActive);

        var activated = await CreateSut().ActivateAsync(created.Value.Id);
        Assert.True(activated.IsSuccess);
        Assert.True((await CreateSut().ListAsync()).Value.Single().IsActive);
        Assert.Equal(2, _audit.Actions.Count(a => a == AuditActions.CategoryStatusChanged));
    }

    [Fact]
    public async Task Operations_require_permission_and_sign_in()
    {
        SignIn(Permissions.ProductsView);
        var forbidden = await CreateSut().CreateAsync(new SaveCategoryRequest("Đồ uống", null));
        Assert.Equal("forbidden", forbidden.Error.Code);

        _session.SignOut();
        var unauthenticated = await CreateSut().ListAsync();
        Assert.Equal("auth.required", unauthenticated.Error.Code);
    }
}
