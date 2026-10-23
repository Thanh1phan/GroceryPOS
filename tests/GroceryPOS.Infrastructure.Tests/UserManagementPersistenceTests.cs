using GroceryPOS.Application.Abstractions;
using GroceryPOS.Application.Users;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Infrastructure.Auditing;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Repositories;
using GroceryPOS.Infrastructure.Persistence.Seeding;
using GroceryPOS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Tests;

/// <summary>User and role management against a real (in-memory SQLite) database.</summary>
public sealed class UserManagementPersistenceTests : IDisposable
{
    private static readonly BCryptPasswordHasher FastHasher = new(workFactor: 4);
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private (UserManagementService Users, RoleService Roles) CreateServices(GroceryDbContext context)
    {
        var audit = new AuditService(context, _db.Clock, _db.CurrentUser);
        var users = new UserRepository(context);
        var roles = new RoleRepository(context);
        return (new UserManagementService(users, roles, context, FastHasher, _db.CurrentUser, audit, _db.Clock),
            new RoleService(roles, users, context, _db.CurrentUser, audit));
    }

    [Fact]
    public async Task Custom_role_and_user_round_trip_and_the_role_is_deleted_with_its_permissions()
    {
        int roleId;
        int userId;
        await using (var context = _db.CreateContext())
        {
            await new DatabaseInitializer(context, FastHasher, new SeedOptions()).InitializeAsync();
            var admin = await context.Users.Include(u => u.Role).SingleAsync();
            _db.CurrentUser.Session = new UserSession(admin.Id, admin.Username, admin.FullName, admin.Role.Name, admin.Role.PermissionKeys, _db.Clock.Now);

            var (users, roles) = CreateServices(context);
            var role = await roles.CreateAsync(new SaveRoleRequest("Kế toán", "Báo cáo", new[] { Permissions.ReportsView, Permissions.DashboardView }));
            Assert.True(role.IsSuccess, role.IsFailure ? role.Error.Message : null);
            roleId = role.Value.Id;

            var user = await users.CreateAsync(new CreateUserRequest("KeToan", "Ngô Thị Hà", roleId, "tamthoi123", "tamthoi123"));
            Assert.True(user.IsSuccess, user.IsFailure ? user.Error.Message : null);
            userId = user.Value.Id;
        }

        await using (var context = _db.CreateContext())
        {
            var (users, roles) = CreateServices(context);
            var list = (await roles.ListAsync()).Value;
            var custom = list.Single(r => r.Id == roleId);
            Assert.Equal(1, custom.UserCount);
            Assert.Equal(2, custom.Permissions.Count);

            Assert.True((await roles.SavePermissionMatrixAsync(new[] { new RolePermissionsUpdate(roleId, new[] { Permissions.ReportsView }) })).IsSuccess);
            Assert.Equal("conflict", (await roles.DeleteAsync(roleId)).Error.Code);

            var cashierRole = list.Single(r => r.Name == SystemRoles.Cashier).Id;
            Assert.True((await users.UpdateAsync(userId, new UpdateUserRequest("Ngô Thị Hà", cashierRole))).IsSuccess);
            Assert.True((await roles.DeleteAsync(roleId)).IsSuccess);
        }

        await using (var context = _db.CreateContext())
        {
            Assert.False(await context.Roles.AnyAsync(r => r.Id == roleId));
            Assert.Equal(0, await context.Set<RolePermission>().CountAsync(p => p.RoleId == roleId));
            var user = await context.Users.Include(u => u.Role).SingleAsync(u => u.Id == userId);
            Assert.Equal(("ketoan", SystemRoles.Cashier, true), (user.Username, user.Role.Name, user.MustChangePassword));
            var actions = await context.AuditLogs.Select(a => a.Action).ToListAsync();
            Assert.Contains("role.created", actions);
            Assert.Contains("user.created", actions);
            Assert.Contains("role.deleted", actions);
        }
    }
}
