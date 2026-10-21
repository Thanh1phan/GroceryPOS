using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Infrastructure.Persistence;
using GroceryPOS.Infrastructure.Persistence.Repositories;

namespace GroceryPOS.Infrastructure.Tests;

/// <summary>Repository queries against real SQLite (translation to SQL, includes, ordering, filters).</summary>
public sealed class RepositoryTests : IDisposable
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task User_lookup_is_case_insensitive_and_loads_role_permissions()
    {
        await using (var context = _db.CreateContext())
        {
            var role = SystemRoles.CreateDefaults().Single(r => r.Name == SystemRoles.Cashier);
            context.Users.Add(new User("ThuNgan1", "Trần Thị Bình", "hash", role));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var users = new UserRepository(context);
            var user = await users.GetByUsernameAsync(" THUNGAN1 ");

            Assert.NotNull(user);
            Assert.Equal(SystemRoles.Cashier, user!.Role.Name);
            Assert.Contains(Permissions.SalesCreate, user.Role.PermissionKeys);
            Assert.True(await users.UsernameExistsAsync("thungan1"));
            Assert.False(await users.UsernameExistsAsync("khong-co"));
            Assert.NotNull((await users.GetByIdAsync(user.Id))!.Role);
            Assert.Equal(SystemRoles.Cashier, (await new RoleRepository(context).GetByNameAsync(SystemRoles.Cashier))!.Name);
        }
    }

    [Fact]
    public async Task Product_search_ignores_accents_and_case_ranks_exact_code_first_and_skips_inactive()
    {
        await SeedProductsAsync();

        await using var context = _db.CreateContext();
        var products = new ProductRepository(context);

        var milk = await products.SearchAsync("sua tuoi", 10);
        Assert.Equal(new[] { "SUA02", "SUA01" }, milk.Select(p => p.Code)); // "Sữa tươi TH..." < "Sữa tươi Vinamilk..."; inactive SUA03 skipped
        Assert.NotNull(milk[0].Category);

        var sugar = await products.SearchAsync("DUONG", 10);
        Assert.Equal("DUONG01", Assert.Single(sugar).Code);

        var byCode = await products.SearchAsync("gao01", 10);
        Assert.Equal("GAO01", byCode[0].Code);

        var byBarcode = await products.SearchAsync("8934673", 10);
        Assert.Equal("SUA01", Assert.Single(byBarcode).Code);

        Assert.Single(await products.SearchAsync("s", 1));
        Assert.Empty(await products.SearchAsync("   ", 10));
    }

    [Fact]
    public async Task Product_lookups_by_code_barcode_ids_and_low_stock()
    {
        var ids = await SeedProductsAsync();

        await using var context = _db.CreateContext();
        var products = new ProductRepository(context);

        Assert.Equal("SUA01", (await products.GetByBarcodeAsync("8934673000017"))!.Code);
        Assert.Equal("GAO01", (await products.GetByCodeAsync("gao01"))!.Code);
        Assert.Equal(2, (await products.GetByIdsAsync(new[] { ids[0], ids[1], ids[1] })).Count);
        Assert.Empty(await products.GetByIdsAsync(Array.Empty<int>()));

        // Low stock: at or below minimum (decimal comparison must work on SQLite), active only.
        var low = await products.ListLowStockAsync();
        Assert.Equal(new[] { "GAO01", "DUONG01" }, low.Select(p => p.Code));
    }

    [Fact]
    public async Task Last_code_with_prefix_and_period_queries_for_sales()
    {
        int cashierId;
        await using (var context = _db.CreateContext())
        {
            var (cashier, product) = await SeedCashierAndProductAsync(context);
            cashierId = cashier.Id;
            AddSale(context, "HD261015-0007", cashierId, product, new DateTimeOffset(2026, 10, 15, 22, 0, 0, Vietnam));
            AddSale(context, "HD261016-0002", cashierId, product, new DateTimeOffset(2026, 10, 16, 10, 0, 0, Vietnam));
            AddSale(context, "HD261016-0010", cashierId, product, new DateTimeOffset(2026, 10, 16, 11, 0, 0, Vietnam));
            AddSale(context, "HD261016-0009", cashierId, product, new DateTimeOffset(2026, 10, 16, 9, 0, 0, Vietnam));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var sales = new SaleRepository(context);
            Assert.Equal("HD261016-0010", await sales.GetLastCodeWithPrefixAsync("HD261016-"));
            Assert.Null(await sales.GetLastCodeWithPrefixAsync("HD261017-"));

            var day = new DateTimeOffset(2026, 10, 16, 0, 0, 0, Vietnam);
            var inDay = await sales.ListCompletedInPeriodAsync(day, day.AddDays(1));
            Assert.Equal(new[] { "HD261016-0009", "HD261016-0002", "HD261016-0010" }, inDay.Select(s => s.Code));
            Assert.All(inDay, s => Assert.Single(s.Lines));
            Assert.All(inDay, s => Assert.Single(s.Payments));

            var byCashier = await sales.ListCompletedByCashierAsync(cashierId, day, day.AddHours(10)); // inclusive end
            Assert.Equal(new[] { "HD261016-0009", "HD261016-0002" }, byCashier.Select(s => s.Code));
            Assert.Empty(await sales.ListCompletedByCashierAsync(cashierId + 1, day, day.AddDays(1)));

            var sale = await sales.GetByCodeAsync("hd261016-0002");
            Assert.NotNull(sale);
            Assert.Single(sale!.Lines);
        }
    }

    [Fact]
    public async Task Shift_repository_finds_open_shift_and_searches_by_user()
    {
        int userId;
        await using (var context = _db.CreateContext())
        {
            var (cashier, _) = await SeedCashierAndProductAsync(context);
            userId = cashier.Id;
            var shift = new Shift("CA261016-0001", userId, new DateTimeOffset(2026, 10, 16, 7, 0, 0, Vietnam), 500_000);
            shift.AddCashMovement(CashMovementType.PayOut, 50_000, "Mua túi nilon", shift.OpenedAt.AddHours(1), userId);
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var shifts = new ShiftRepository(context);
            var open = await shifts.GetOpenByUserAsync(userId);
            Assert.NotNull(open);
            Assert.Single(open!.CashMovements);
            Assert.Null(await shifts.GetLastClosedAsync());
            Assert.Equal("CA261016-0001", await shifts.GetLastCodeWithPrefixAsync("CA261016-"));
            Assert.Single(await shifts.SearchAsync(new ShiftFilter(UserId: userId, Status: ShiftStatus.Open)));
            Assert.Empty(await shifts.SearchAsync(new ShiftFilter(Status: ShiftStatus.Closed)));
        }
    }

    [Fact]
    public async Task PriceHistory_lists_by_product_newest_first_within_range()
    {
        int productId;
        await using (var context = _db.CreateContext())
        {
            var category = new Category("Tạp hóa");
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product("SUA01", "Sữa tươi Vinamilk 1L", category.Id, "hộp", 10_000, 12_000);
            context.Products.Add(product);
            await context.SaveChangesAsync();
            productId = product.Id;

            var change1 = product.ChangePrices(11_000, 13_000);
            context.PriceHistories.Add(PriceHistory.From(
                product, change1, new DateTimeOffset(2026, 10, 1, 8, 0, 0, Vietnam), null, PriceChangeSource.ManualEdit)!);

            var change2 = product.ChangePrices(12_000, 14_000);
            context.PriceHistories.Add(PriceHistory.From(
                product, change2, new DateTimeOffset(2026, 10, 10, 8, 0, 0, Vietnam), null, PriceChangeSource.GoodsReceipt)!);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var history = new PriceHistoryRepository(context);
            var all = await history.ListByProductAsync(productId);
            Assert.Equal(new[] { 12_000m, 11_000m }, all.Select(h => h.NewCostPrice));

            var limited = await history.ListByProductAsync(
                productId, from: new DateTimeOffset(2026, 10, 5, 0, 0, 0, Vietnam));
            Assert.Equal(12_000m, Assert.Single(limited).NewCostPrice);
        }
    }

    [Fact]
    public async Task Customer_is_found_by_normalized_phone()
    {
        await using (var context = _db.CreateContext())
        {
            context.Customers.Add(new Customer("KH001", "Nguyễn Văn An", "+84 901 234 567"));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var customers = new CustomerRepository(context);
            Assert.Equal("KH001", (await customers.GetByPhoneAsync(PhoneNumber.NormalizeOptional("0901234567")!))!.Code);
        }
    }

    private async Task<int[]> SeedProductsAsync()
    {
        await using var context = _db.CreateContext();
        var category = new Category("Tạp hóa");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        Product Make(string code, string name, decimal stock, decimal min, string? barcode = null, bool active = true)
        {
            var product = new Product(code, name, category.Id, "cái", 10_000, 12_000);
            product.SetBarcode(barcode);
            product.SetMinStockLevel(min);
            product.AdjustStock(stock);
            if (!active)
            {
                product.Deactivate();
            }

            return product;
        }

        var products = new[]
        {
            Make("SUA01", "Sữa tươi Vinamilk 1L", 50, 10, "8934673000017"),
            Make("SUA02", "Sữa tươi TH True Milk 1L", 30, 10),
            Make("SUA03", "Sữa tươi ngừng bán", 0, 5, active: false),
            Make("GAO01", "Gạo ST25", 4.5m, 5),
            Make("DUONG01", "Đường Biên Hòa 1kg", 5, 5),
        };
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        return products.Select(p => p.Id).ToArray();
    }

    private static async Task<(User Cashier, Product Product)> SeedCashierAndProductAsync(GroceryDbContext context)
    {
        var role = new Role(SystemRoles.Cashier, isSystem: true);
        var cashier = new User("thungan", "Trần Thị Bình", "hash", role);
        var category = new Category("Đồ uống");
        context.AddRange(role, cashier, category);
        await context.SaveChangesAsync();

        var product = new Product("NUOC01", "Nước suối 500ml", category.Id, "chai", 3_500, 5_000);
        product.AdjustStock(100);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return (cashier, product);
    }

    private static void AddSale(GroceryDbContext context, string code, int cashierId, Product product, DateTimeOffset at)
    {
        var sale = new Sale(code, cashierId, at);
        sale.AddItem(product, 1);
        sale.AddPayment(PaymentMethod.Cash, sale.Total);
        sale.Complete(at, LoyaltyPolicy.Default);
        context.Sales.Add(sale);
    }
}
