using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Authentication;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Infrastructure.Auditing;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class InfrastructureServicesTests : IDisposable
{
    // Lowest BCrypt cost keeps the tests fast; production uses BCryptPasswordHasher.DefaultWorkFactor.
    private static readonly BCryptPasswordHasher FastHasher = new(workFactor: 4);
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void BCrypt_hashes_are_salted_and_verify()
    {
        var first = FastHasher.Hash("matkhau123");
        var second = FastHasher.Hash("matkhau123");

        Assert.NotEqual(first, second);
        Assert.StartsWith("$2", first);
        Assert.True(FastHasher.Verify("matkhau123", first));
        Assert.False(FastHasher.Verify("MatKhau123", first));
        Assert.False(FastHasher.Verify("matkhau123", "not-a-bcrypt-hash"));
        Assert.False(FastHasher.Verify("", first));
    }

    [Fact]
    public async Task Initializer_seeds_roles_admin_and_category_once()
    {
        for (var run = 0; run < 2; run++)
        {
            await using var context = _db.CreateContext();
            await new DatabaseInitializer(context, FastHasher, new SeedOptions()).InitializeAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var roles = await context.Roles.OrderBy(r => r.Name).ToListAsync();
            Assert.Equal(4, roles.Count);
            Assert.All(roles, r => Assert.True(r.IsSystem));
            Assert.Equal(Permissions.All.Count, roles.Single(r => r.Name == SystemRoles.Admin).Permissions.Count);

            var admin = await context.Users.Include(u => u.Role).SingleAsync();
            Assert.Equal("admin", admin.Username);
            Assert.Equal(SystemRoles.Admin, admin.Role.Name);
            Assert.True(admin.MustChangePassword);
            Assert.True(FastHasher.Verify("admin123", admin.PasswordHash));
            Assert.Equal(1, await context.Categories.CountAsync());
        }
    }

    [Fact]
    public async Task Initializer_grants_admin_permissions_added_later_and_keeps_other_edits()
    {
        await using (var context = _db.CreateContext())
        {
            await new DatabaseInitializer(context, FastHasher, new SeedOptions()).InitializeAsync();
            var cashier = await context.Roles.SingleAsync(r => r.Name == SystemRoles.Cashier);
            cashier.Revoke(Permissions.ReturnsCreate);
            await context.SaveChangesAsync();

            // Simulate a database created before a permission existed.
            await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM RolePermissions WHERE Permission = {0} AND RoleId = (SELECT Id FROM Roles WHERE Name = {1})",
                Permissions.ShiftsSupervise, SystemRoles.Admin);
        }

        await using (var context = _db.CreateContext())
        {
            await new DatabaseInitializer(context, FastHasher, new SeedOptions()).InitializeAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var admin = await context.Roles.SingleAsync(r => r.Name == SystemRoles.Admin);
            Assert.True(admin.HasPermission(Permissions.ShiftsSupervise));
            var cashier = await context.Roles.SingleAsync(r => r.Name == SystemRoles.Cashier);
            Assert.False(cashier.HasPermission(Permissions.ReturnsCreate));
        }
    }

    [Fact]
    public async Task Audit_entries_are_saved_with_the_unit_of_work_and_stamped_with_the_user()
    {
        _db.CurrentUser.Session = new UserSession(7, "quanly", "Lê Văn C", SystemRoles.Manager,
            new HashSet<string>(), _db.Clock.Now);

        await using (var context = _db.CreateContext())
        {
            var audit = new AuditService(context, _db.Clock, _db.CurrentUser);
            audit.Record(AuditActions.PriceChanged, "Product", "SUA01", new string('x', 5000));
            audit.RecordFor(null, "khach", AuditActions.LoginFailed, "Tài khoản không tồn tại");
            Assert.Equal(0, await context.AuditLogs.AsNoTracking().CountAsync()); // nothing written before save
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var entries = await context.AuditLogs.OrderBy(a => a.Id).ToListAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal((7, "quanly", "SUA01"), (entries[0].UserId!.Value, entries[0].Username, entries[0].EntityId));
            Assert.Equal(4000, entries[0].Details!.Length);
            Assert.Equal(_db.Clock.Now, entries[0].Timestamp);
            Assert.Null(entries[1].UserId);
            Assert.Equal("khach", entries[1].Username);
        }
    }

    [Fact]
    public async Task AddInfrastructure_wires_a_working_login_against_a_seeded_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grocerypos-test-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IUserSessionStore, UserSessionStore>()
                .AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<IUserSessionStore>())
                .AddSingleton<IPasswordHasher>(FastHasher)
                .AddInfrastructure(new DatabaseOptions { ConnectionString = $"Data Source={path};Pooling=False" })
                .AddScoped<IAuthService, AuthService>();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                Assert.True((await auth.LoginAsync("admin", "sai-mat-khau")).IsFailure);
                var login = await auth.LoginAsync("Admin", "admin123");
                Assert.True(login.IsSuccess);
                Assert.True(login.Value.MustChangePassword);
                Assert.Contains(Permissions.RolesManage, login.Value.Permissions);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<GroceryDbContext>();
                var actions = await context.AuditLogs.OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
                Assert.Equal(new[] { AuditActions.LoginFailed, AuditActions.LoginSucceeded }, actions);
                Assert.Same(context, scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
