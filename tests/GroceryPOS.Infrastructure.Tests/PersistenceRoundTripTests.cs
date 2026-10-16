using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Domain.Shifts;
using GroceryPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GroceryPOS.Infrastructure.Tests;

/// <summary>Saves real aggregates through EF Core + SQLite and reads them back in a fresh context.</summary>
public sealed class PersistenceRoundTripTests : IDisposable
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Roles_keep_their_permissions_and_users_their_role()
    {
        await using (var context = _db.CreateContext())
        {
            var roles = SystemRoles.CreateDefaults().ToList();
            context.Roles.AddRange(roles);
            context.Users.Add(new User("ThuNgan1", "Trần Thị Bình", "hash", roles.Single(r => r.Name == SystemRoles.Cashier)));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var user = await context.Users.Include(u => u.Role).SingleAsync(u => u.Username == "thungan1");
            Assert.Equal(SystemRoles.Cashier, user.Role.Name);
            Assert.True(user.Role.HasPermission(Permissions.SalesCreate));
            Assert.False(user.Role.HasPermission(Permissions.ReportsView));

            var admin = await context.Roles.SingleAsync(r => r.Name == SystemRoles.Admin);
            Assert.Equal(Permissions.All.Count, admin.Permissions.Count);
        }
    }

    [Fact]
    public async Task Completed_sale_round_trips_with_discounts_payments_and_totals()
    {
        int saleId;
        decimal total, vat, profit;
        await using (var context = _db.CreateContext())
        {
            var (cashier, products, customer) = await SeedAsync(context);
            var sale = new Sale("HD261016-0001", cashier.Id, new DateTimeOffset(2026, 10, 16, 9, 30, 0, Vietnam));
            var milk = sale.AddItem(products[0], 2);
            sale.AddItem(products[1], 1.5m);
            sale.AddItem(products[2], 3); // keeps the shared Discount.None instance
            sale.SetLineDiscount(milk, Discount.Percent(10));
            sale.SetInvoiceDiscount(Discount.Amount(5_000));
            sale.AttachCustomer(customer);
            sale.AddPayment(PaymentMethod.BankTransfer, 50_000, "FT123");
            sale.AddPayment(PaymentMethod.Cash, 200_000);
            sale.Complete(sale.SaleDate, LoyaltyPolicy.Default);
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
            (saleId, total, vat, profit) = (sale.Id, sale.Total, sale.VatTotal, sale.Profit);
        }

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);

            Assert.Equal(SaleStatus.Completed, sale.Status);
            Assert.Equal(3, sale.Lines.Count);
            Assert.Equal(2, sale.Payments.Count);
            Assert.Equal(Discount.Percent(10), sale.Lines[0].Discount);
            Assert.Equal(Discount.None, sale.Lines[2].Discount);
            Assert.Equal(Discount.Amount(5_000), sale.InvoiceDiscount);
            Assert.Equal(1.5m, sale.Lines[1].Quantity);
            Assert.Equal(total, sale.Total);
            Assert.Equal(vat, sale.VatTotal);
            Assert.Equal(profit, sale.Profit);
            Assert.Equal(sale.Total, sale.Lines.Sum(l => l.FinalAmount));
            Assert.Equal("FT123", sale.Payments.Single(p => p.Method == PaymentMethod.BankTransfer).Reference);
            Assert.Equal(TimeSpan.FromHours(7), sale.SaleDate.Offset);
            Assert.NotNull(sale.CustomerId);
        }
    }

    [Fact]
    public async Task Period_filters_on_dates_run_in_sqlite_across_utc_offsets()
    {
        await using (var context = _db.CreateContext())
        {
            var (cashier, products, _) = await SeedAsync(context);
            // 23:30 on the 15th in Vietnam is 16:30 UTC on the 15th; 00:30 on the 16th is 17:30 UTC on the 15th.
            AddCompletedSale(context, "HD261015-0001", cashier.Id, products[0], new DateTimeOffset(2026, 10, 15, 23, 30, 0, Vietnam));
            AddCompletedSale(context, "HD261016-0001", cashier.Id, products[0], new DateTimeOffset(2026, 10, 16, 0, 30, 0, Vietnam));
            AddCompletedSale(context, "HD261016-0002", cashier.Id, products[0], new DateTimeOffset(2026, 10, 16, 21, 0, 0, Vietnam));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var from = new DateTimeOffset(2026, 10, 16, 0, 0, 0, Vietnam);
            var to = from.AddDays(1);
            var codes = await context.Sales
                .Where(s => s.Status == SaleStatus.Completed && s.CompletedAt >= from && s.CompletedAt < to)
                .OrderBy(s => s.CompletedAt)
                .Select(s => s.Code)
                .ToListAsync();

            Assert.Equal(new[] { "HD261016-0001", "HD261016-0002" }, codes);
        }
    }

    [Fact]
    public async Task Customer_return_links_to_sale_line_and_updates_returned_quantity()
    {
        int saleId;
        await using (var context = _db.CreateContext())
        {
            var (cashier, products, _) = await SeedAsync(context);
            var sale = AddCompletedSale(context, "HD261016-0001", cashier.Id, products[0], new DateTimeOffset(2026, 10, 16, 9, 0, 0, Vietnam), quantity: 4);
            await context.SaveChangesAsync();
            var saleReturn = SaleReturn.Create("TH261016-0001", sale, cashier.Id, sale.SaleDate.AddHours(1), "Hộp bị móp",
                PaymentMethod.Cash, [new ReturnItem(sale.Lines[0], 1, Restock: false)]);
            context.SaleReturns.Add(saleReturn);
            await context.SaveChangesAsync();
            saleId = sale.Id;
        }

        await using (var context = _db.CreateContext())
        {
            var sale = await context.Sales.SingleAsync(s => s.Id == saleId);
            var saleReturn = await context.SaleReturns.SingleAsync(r => r.SaleId == saleId);

            Assert.Equal(1, sale.Lines[0].ReturnedQuantity);
            Assert.Equal(sale.RefundedTotal, saleReturn.RefundTotal);
            Assert.Equal(sale.Lines[0].Id, saleReturn.Lines[0].SaleLineId);
            Assert.False(saleReturn.Lines[0].Restock);
        }
    }

    [Fact]
    public async Task Shift_totals_are_null_while_open_and_frozen_after_closing()
    {
        int shiftId;
        await using (var context = _db.CreateContext())
        {
            var (cashier, _, _) = await SeedAsync(context);
            var opened = new DateTimeOffset(2026, 10, 16, 7, 0, 0, Vietnam);
            var shift = new Shift("CA261016-0001", cashier.Id, opened, 500_000);
            shift.AddCashMovement(CashMovementType.PayOut, 30_000, "Mua đá lạnh", opened.AddHours(2), cashier.Id);
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
            shiftId = shift.Id;
        }

        await using (var context = _db.CreateContext())
        {
            var shift = await context.Shifts.SingleAsync(s => s.Id == shiftId);
            Assert.Null(shift.Totals);
            Assert.Equal(30_000, shift.PayOutTotal);

            var totals = new ShiftTotals(12, 1_250_000, 15_000, 900_000, 350_000, 0, 1, 20_000, 20_000, 0, 0, 0);
            shift.Close(totals, 1_350_000, 500_000, null, shift.OpenedAt.AddHours(8), shift.UserId, 5_000);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var shift = await context.Shifts.SingleAsync(s => s.Id == shiftId);
            Assert.Equal(ShiftStatus.Closed, shift.Status);
            Assert.Equal(new ShiftTotals(12, 1_250_000, 15_000, 900_000, 350_000, 0, 1, 20_000, 20_000, 0, 0, 0), shift.Totals);
            Assert.Equal(1_350_000, shift.ExpectedCash);
            Assert.Equal(0, shift.Variance);
            Assert.Single(shift.CashMovements);
        }
    }

    [Fact]
    public async Task Completed_goods_receipt_persists_lines_ledger_and_new_cost()
    {
        int receiptId, productId;
        await using (var context = _db.CreateContext())
        {
            var (user, products, _) = await SeedAsync(context);
            var supplier = new Supplier("NCC001", "Công ty Vinamilk");
            context.Suppliers.Add(supplier);
            await context.SaveChangesAsync();

            var receipt = new GoodsReceipt("PN261016-0001", supplier.Id, user.Id, new DateTimeOffset(2026, 10, 16, 8, 0, 0, Vietnam));
            receipt.AddLine(products[0], 24, 6_500, newSellPrice: 9_000);
            receipt.SetPaidAmount(100_000);
            var postings = receipt.Complete(products.ToDictionary(p => p.Id), receipt.ReceiptDate.AddMinutes(30), user.Id);
            supplier.AddDebt(receipt.DebtAmount);
            context.GoodsReceipts.Add(receipt);
            context.StockMovements.AddRange(postings.Select(p => p.Movement));
            await context.SaveChangesAsync();
            (receiptId, productId) = (receipt.Id, products[0].Id);
        }

        await using (var context = _db.CreateContext())
        {
            var receipt = await context.GoodsReceipts.SingleAsync(r => r.Id == receiptId);
            var product = await context.Products.SingleAsync(p => p.Id == productId);
            var movement = await context.StockMovements.SingleAsync(m => m.ReferenceCode == "PN261016-0001");
            var supplier = await context.Suppliers.SingleAsync();

            Assert.Equal(GoodsReceiptStatus.Completed, receipt.Status);
            Assert.Equal(156_000, receipt.Total);
            Assert.Equal(56_000, supplier.DebtBalance);
            Assert.Equal(9_000, product.SellPrice);
            Assert.Equal(124, product.StockQuantity);
            Assert.Equal(StockMovementType.GoodsReceipt, movement.Type);
            Assert.Equal(24, movement.Quantity);
            Assert.Equal(6_500, movement.UnitCost);
        }
    }

    [Fact]
    public async Task Auditable_entities_are_stamped_with_user_and_time()
    {
        _db.CurrentUser.Session = new UserSession(1, "quanly", "Quản lý", SystemRoles.Manager, new HashSet<string>(), _db.Clock.Now);
        await using (var context = _db.CreateContext())
        {
            context.Categories.Add(new Category("Đồ uống"));
            await context.SaveChangesAsync();
        }

        _db.Clock.Now = _db.Clock.Now.AddHours(3);
        _db.CurrentUser.Session = null;
        await using (var context = _db.CreateContext())
        {
            var category = await context.Categories.SingleAsync();
            category.Update("Nước giải khát", null);
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var category = await context.Categories.SingleAsync();
            Assert.Equal("quanly", category.CreatedBy);
            Assert.Equal(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero), category.CreatedAt);
            Assert.Equal(AuditableEntityInterceptor.SystemUser, category.UpdatedBy);
            Assert.Equal(new DateTimeOffset(2026, 10, 16, 11, 0, 0, TimeSpan.Zero), category.UpdatedAt);
        }
    }

    [Fact]
    public async Task Duplicate_product_codes_and_barcodes_are_rejected_but_missing_barcodes_are_not()
    {
        await using var context = _db.CreateContext();
        var category = new Category("Bánh kẹo");
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        context.Products.Add(new Product("BK01", "Bánh Chocopie", category.Id, "hộp", 40_000, 52_000));
        context.Products.Add(new Product("BK02", "Kẹo Alpenliebe", category.Id, "gói", 15_000, 20_000));
        await context.SaveChangesAsync();

        var duplicateCode = new Product("bk01", "Trùng mã", category.Id, "hộp", 1, 2);
        context.Products.Add(duplicateCode);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(duplicateCode).State = EntityState.Detached;

        var first = await context.Products.SingleAsync(p => p.Code == "BK01");
        first.SetBarcode("NB-0001");
        await context.SaveChangesAsync();
        var duplicateBarcode = new Product("BK03", "Trùng mã vạch", category.Id, "gói", 1, 2);
        duplicateBarcode.SetBarcode("NB-0001");
        context.Products.Add(duplicateBarcode);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_log_is_append_only_text()
    {
        await using (var context = _db.CreateContext())
        {
            context.AuditLogs.Add(new AuditLog(_db.Clock.Now, AuditActions.LoginFailed, null, "khongton", details: "Sai mật khẩu"));
            await context.SaveChangesAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var entry = await context.AuditLogs.SingleAsync(a => a.Action == AuditActions.LoginFailed);
            Assert.Null(entry.UserId);
            Assert.Equal("Sai mật khẩu", entry.Details);
        }
    }

    private static Sale AddCompletedSale(GroceryDbContext context, string code, int cashierId, Product product, DateTimeOffset at, decimal quantity = 1)
    {
        var sale = new Sale(code, cashierId, at);
        sale.AddItem(product, quantity);
        sale.AddPayment(PaymentMethod.Cash, sale.Total);
        sale.Complete(at, LoyaltyPolicy.Default);
        context.Sales.Add(sale);
        return sale;
    }

    private static async Task<(User Cashier, List<Product> Products, Customer Customer)> SeedAsync(GroceryDbContext context)
    {
        var role = new Role(SystemRoles.Cashier, isSystem: true);
        var cashier = new User("thungan", "Trần Thị Bình", "hash", role);
        var category = new Category("Sữa & đồ uống");
        context.AddRange(role, cashier, category);
        await context.SaveChangesAsync();

        var products = new List<Product>
        {
            new("SUA01", "Sữa tươi Vinamilk 1L", category.Id, "hộp", 25_000, 32_000, VatRate.Five),
            new("GAO01", "Gạo ST25", category.Id, "kg", 28_000, 35_000, VatRate.Five),
            new("NUOC01", "Nước suối Lavie 500ml", category.Id, "chai", 3_500, 5_000, VatRate.Ten),
        };
        foreach (var product in products)
        {
            product.AdjustStock(100);
        }

        var customer = new Customer("KH001", "Nguyễn Văn An", "0901 234 567");
        context.AddRange(products);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return (cashier, products, customer);
    }
}
