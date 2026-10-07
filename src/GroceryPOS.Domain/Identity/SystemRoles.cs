namespace GroceryPOS.Domain.Identity;

/// <summary>Built-in roles seeded on first run, with their default permission sets.</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Quản lý";
    public const string Cashier = "Thu ngân";
    public const string Storekeeper = "Thủ kho";

    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> DefaultPermissions { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [Admin] = Permissions.All,
            [Manager] = Permissions.All
                .Where(p => p is not Permissions.RolesManage and not Permissions.SettingsManage)
                .ToArray(),
            [Cashier] = new[]
            {
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.CustomersView,
                Permissions.CustomersManage,
                Permissions.SalesCreate,
                Permissions.ReturnsCreate,
                Permissions.ShiftsManage,
            },
            [Storekeeper] = new[]
            {
                Permissions.DashboardView,
                Permissions.ProductsView,
                Permissions.ProductsManage,
                Permissions.CategoriesManage,
                Permissions.SuppliersManage,
                Permissions.PurchasesManage,
                Permissions.InventoryView,
                Permissions.InventoryAdjust,
            },
        };

    public static IEnumerable<Role> CreateDefaults()
    {
        foreach (var (name, permissions) in DefaultPermissions)
        {
            var role = new Role(name, description: null, isSystem: true);
            foreach (var permission in permissions)
            {
                role.Grant(permission);
            }

            yield return role;
        }
    }
}
