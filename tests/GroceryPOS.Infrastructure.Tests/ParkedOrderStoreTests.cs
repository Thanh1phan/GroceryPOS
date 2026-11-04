using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Sales;
using GroceryPOS.Infrastructure.Sales;

namespace GroceryPOS.Infrastructure.Tests;

public sealed class ParkedOrderStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "grocerypos-parked-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "sub", "parked-orders.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ParkedOrder Order(string label, DateTimeOffset at) => new(
        Guid.NewGuid(),
        label,
        at,
        7,
        "Trần Thị B",
        [
            new ParkedLine(1, "SUA01", "Sữa tươi Vinamilk 1L", "hộp", 35_000, 2, DiscountInput.Percent(10)),
            new ParkedLine(2, "GAO01", "Gạo ST25", "kg", 32_000, 1.5m, null),
        ],
        9,
        "Nguyễn Văn A",
        "0901234567",
        new DiscountInput(DiscountKind.Amount, 5_000),
        "Khách quay lại",
        106_000);

    [Fact]
    public async Task Orders_survive_a_new_store_instance_oldest_first()
    {
        var now = new DateTimeOffset(2026, 11, 4, 3, 0, 0, TimeSpan.Zero);
        var later = Order("Đơn tạm 2", now);
        var earlier = Order("Chị Lan", now.AddMinutes(-10));
        var store = new JsonFileParkedOrderStore(FilePath);
        await store.SaveAsync(later);
        await store.SaveAsync(earlier);

        var reloaded = await new JsonFileParkedOrderStore(FilePath).ListAsync();

        Assert.Equal(["Chị Lan", "Đơn tạm 2"], reloaded.Select(o => o.Label));
        var first = reloaded[0];
        Assert.Equal(earlier.Id, first.Id);
        Assert.Equal(earlier.ParkedAt, first.ParkedAt);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(earlier.Lines[0], first.Lines[0]);
        Assert.Equal(1.5m, first.Lines[1].Quantity);
        Assert.Equal(DiscountInput.Amount(5_000), first.InvoiceDiscount);
        Assert.Equal("Nguyễn Văn A", first.CustomerName);
        Assert.Contains("Sữa tươi", await File.ReadAllTextAsync(FilePath)); // Vietnamese kept readable
    }

    [Fact]
    public async Task Save_replaces_by_id_and_remove_reports_whether_it_existed()
    {
        var store = new JsonFileParkedOrderStore(FilePath);
        var order = Order("Đơn tạm 1", DateTimeOffset.UtcNow);
        await store.SaveAsync(order);
        await store.SaveAsync(order with { Label = "Anh Tư" });

        Assert.Equal("Anh Tư", Assert.Single(await store.ListAsync()).Label);
        Assert.Equal("Anh Tư", (await store.GetAsync(order.Id))!.Label);
        Assert.True(await store.RemoveAsync(order.Id));
        Assert.False(await store.RemoveAsync(order.Id));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Missing_or_corrupt_file_starts_empty_and_keeps_the_bad_copy()
    {
        Assert.Empty(await new JsonFileParkedOrderStore(FilePath).ListAsync());

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await File.WriteAllTextAsync(FilePath, "{ not json");
        var store = new JsonFileParkedOrderStore(FilePath);

        Assert.Empty(await store.ListAsync());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.bad"));
        await store.SaveAsync(Order("Đơn tạm 1", DateTimeOffset.UtcNow));
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task Concurrent_saves_are_not_lost()
    {
        var store = new JsonFileParkedOrderStore(FilePath);

        await Task.WhenAll(Enumerable.Range(1, 10).Select(i => store.SaveAsync(Order($"Đơn tạm {i}", DateTimeOffset.UtcNow.AddSeconds(i)))));

        Assert.Equal(10, (await store.ListAsync()).Count);
    }

    [Fact]
    public void Default_path_is_under_local_app_data()
    {
        var path = new ParkedOrderOptions().ResolveFilePath();

        Assert.EndsWith(Path.Combine("GroceryPOS", "parked-orders.json"), path);
        Assert.Equal(Path.GetFullPath("custom.json"), new ParkedOrderOptions { FilePath = "custom.json" }.ResolveFilePath());
    }
}
