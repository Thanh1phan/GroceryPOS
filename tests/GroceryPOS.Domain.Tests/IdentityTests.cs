using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Identity;

namespace GroceryPOS.Domain.Tests;

public class UserTests
{
    private static readonly LockoutPolicy Policy = new(3, TimeSpan.FromMinutes(15));

    private static User NewUser() => new("  ThuNgan01 ", "Trần Thị B", "hash", new Role("Thu ngân"));

    [Fact]
    public void Username_is_trimmed_and_lowercased()
    {
        Assert.Equal("thungan01", NewUser().Username);
    }

    [Fact]
    public void Account_locks_after_max_failed_attempts()
    {
        var user = NewUser();

        Assert.False(user.RegisterFailedLogin(TestData.Now, Policy));
        Assert.False(user.RegisterFailedLogin(TestData.Now, Policy));
        Assert.True(user.RegisterFailedLogin(TestData.Now, Policy));

        Assert.True(user.IsLockedOut(TestData.Now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(TestData.Now.AddMinutes(15)));
    }

    [Fact]
    public void Successful_login_resets_failed_count()
    {
        var user = NewUser();
        user.RegisterFailedLogin(TestData.Now, Policy);
        user.RegisterFailedLogin(TestData.Now, Policy);

        user.RegisterSuccessfulLogin(TestData.Now);

        Assert.Equal(0, user.AccessFailedCount);
        Assert.Equal(TestData.Now, user.LastLoginAt);
    }

    [Fact]
    public void Unlock_clears_lockout()
    {
        var user = NewUser();
        for (var i = 0; i < 3; i++)
        {
            user.RegisterFailedLogin(TestData.Now, Policy);
        }

        user.Unlock();

        Assert.False(user.IsLockedOut(TestData.Now));
    }
}

public class RoleTests
{
    [Fact]
    public void Grant_rejects_unknown_permissions()
    {
        var role = new Role("Kế toán");

        Assert.Throws<DomainException>(() => role.Grant("does.not.exist"));
    }

    [Fact]
    public void SetPermissions_replaces_the_set()
    {
        var role = new Role("Kế toán");
        role.Grant(Permissions.ReportsView);

        role.SetPermissions(new[] { Permissions.DashboardView, Permissions.ProductsView });

        Assert.False(role.HasPermission(Permissions.ReportsView));
        Assert.True(role.HasPermission(Permissions.DashboardView));
        Assert.Equal(2, role.PermissionKeys.Count);
    }

    [Fact]
    public void Default_cashier_role_can_sell_but_not_manage_users()
    {
        var cashier = SystemRoles.DefaultPermissions[SystemRoles.Cashier];

        Assert.Contains(Permissions.SalesCreate, cashier);
        Assert.DoesNotContain(Permissions.UsersManage, cashier);
    }
}
