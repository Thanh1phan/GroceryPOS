using GroceryPOS.Application.Catalog;
using GroceryPOS.Application.Common;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Presentation.Catalog;

namespace GroceryPOS.Presentation.Tests;

public sealed class ProductListPresenterTests
{
    private sealed class FakeListView : RecordingView, IProductListView
    {
        public bool EditorResult { get; set; }
        public string? FileToImport { get; set; }
        public List<ProductDetail?> OpenedFor { get; } = new();
        public List<ProductListItem> HistoryFor { get; } = new();
        public List<(string Name, byte[] Content)> Saved { get; } = new();
        public List<ImportSummary> Summaries { get; } = new();
        public List<string> FileProblems { get; } = new();
        public List<string> BusyMessages { get; } = new();
        public int Reloads { get; private set; }

        public bool OpenEditor(ProductDetail? product)
        {
            OpenedFor.Add(product);
            return EditorResult;
        }

        public void OpenPriceHistory(ProductListItem product) => HistoryFor.Add(product);

        public string? PickImportFile() => FileToImport;

        public Task SaveExcelAsync(string suggestedFileName, byte[] content)
        {
            Saved.Add((suggestedFileName, content));
            return Task.CompletedTask;
        }

        public void ShowImportSummary(ImportSummary summary) => Summaries.Add(summary);

        public void ShowFileProblem(string message, string title, string details) => FileProblems.Add(message);

        public Task<T> RunBusyAsync<T>(Func<Task<T>> work, string message)
        {
            BusyMessages.Add(message);
            return work();
        }

        public Task ReloadAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    internal static readonly ProductListItem Milk = new(7, "SUA01", "8934673123457", "Sữa tươi Vinamilk 180ml", 1, "Sữa", "hộp", 6_500, 8_000, 8, 48, 12, true);

    internal static readonly ProductDetail MilkDetail = new(7, "SUA01", "8934673123457", "Sữa tươi Vinamilk 180ml", 1, "Sữa", "hộp", 6_500, 8_000, 8, 48, 12, true);

    private static (TestHost Host, ProductListPresenter Presenter, FakeListView View) Create(bool canManage = true)
    {
        var host = canManage ? new TestHost(Permissions.ProductsManage) : new TestHost(Permissions.ProductsView);
        host.Products.Items.Add(Milk);
        host.Products.Details[7] = MilkDetail;
        var presenter = host.Get<ProductListPresenter>();
        var view = new FakeListView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Static_texts()
    {
        Assert.Equal("Ngừng bán", ProductListPresenter.ToggleText(Milk));
        Assert.Equal("Bán lại", ProductListPresenter.ToggleText(Milk with { IsActive = false }));
        Assert.Equal("SanPham_20261114.xlsx", ProductListPresenter.ExportFileName(new DateTime(2026, 11, 14, 22, 5, 0)));
        Assert.Contains("Sữa tươi Vinamilk 180ml", ProductListPresenter.StopQuestion(Milk));
    }

    [Fact]
    public async Task Load_returns_products_or_toasts_the_error()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        Assert.Equal(new[] { Milk }, await presenter.LoadAsync());

        host.Products.FailWith = Error.Forbidden();
        Assert.Null(await presenter.LoadAsync());
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Errors);
    }

    [Fact]
    public async Task Viewers_only_get_the_price_history()
    {
        var (host, presenter, view) = Create(canManage: false);
        using var _ = host;
        Assert.False(presenter.CanManage);

        await presenter.AddAsync();
        await presenter.EditAsync(Milk);
        await presenter.ToggleActiveAsync(Milk);
        await presenter.ImportAsync();
        await presenter.ExportAsync();
        presenter.ShowPriceHistory(Milk);

        Assert.Empty(view.OpenedFor);
        Assert.Empty(host.Products.Calls);
        Assert.Equal(5, view.Warnings.Count);
        Assert.Same(Milk, Assert.Single(view.HistoryFor));
    }

    [Fact]
    public async Task Edit_opens_fresh_details_then_toasts_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        view.EditorResult = true;

        await presenter.EditAsync(Milk);

        Assert.Equal(new[] { "get:7" }, host.Products.Calls);
        Assert.Same(MilkDetail, Assert.Single(view.OpenedFor));
        Assert.Equal(new[] { "Đã lưu sản phẩm SUA01 – Sữa tươi Vinamilk 180ml." }, view.Successes);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Edit_of_a_removed_product_warns_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        host.Products.GetFailsWith = Error.NotFound("Không tìm thấy sản phẩm.");

        await presenter.EditAsync(Milk);

        Assert.Empty(view.OpenedFor);
        Assert.Equal(new[] { "Không tìm thấy sản phẩm." }, view.Warnings);
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Cancelled_add_does_nothing()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.AddAsync();

        Assert.Null(Assert.Single(view.OpenedFor));
        Assert.Empty(view.Successes);
        Assert.Equal(0, view.Reloads);
    }

    [Fact]
    public async Task Stop_selling_asks_first_and_resume_does_not()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        view.ConfirmAnswer = false;
        await presenter.ToggleActiveAsync(Milk);
        Assert.Empty(host.Products.Calls);

        view.ConfirmAnswer = true;
        await presenter.ToggleActiveAsync(Milk);
        await presenter.ToggleActiveAsync(Milk with { IsActive = false });

        Assert.Equal(2, view.Questions.Count);
        Assert.Equal(new[] { "active:7:False", "active:7:True" }, host.Products.Calls);
        Assert.Equal(new[] { "Đã ngừng bán Sữa tươi Vinamilk 180ml.", "Đã bán lại Sữa tươi Vinamilk 180ml." }, view.Successes);
        Assert.Equal(2, view.Reloads);
    }

    [Fact]
    public async Task Export_saves_the_workbook_behind_the_overlay()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ExportAsync();

        var saved = Assert.Single(view.Saved);
        Assert.StartsWith("SanPham_", saved.Name);
        Assert.Equal(host.Products.Export, saved.Content);
        Assert.Single(view.BusyMessages);

        host.Products.FailWith = Error.Forbidden();
        await presenter.ExportAsync();
        Assert.Single(view.Saved);
        Assert.Equal(new[] { Error.Forbidden().Message }, view.Warnings);
    }

    [Fact]
    public async Task Import_reads_the_file_shows_the_summary_and_reloads()
    {
        var (host, presenter, view) = Create();
        using var _ = host;
        var path = Path.Combine(Path.GetTempPath(), $"grocerypos-import-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "rows");
        try
        {
            view.FileToImport = path;
            await presenter.ImportAsync();
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Equal("rows", host.Products.ImportedText);
        Assert.Same(host.Products.Import, Assert.Single(view.Summaries));
        Assert.Equal(1, view.Reloads);
    }

    [Fact]
    public async Task Import_cancelled_or_unreadable_file()
    {
        var (host, presenter, view) = Create();
        using var _ = host;

        await presenter.ImportAsync(); // no file picked
        Assert.Empty(host.Products.Calls);

        view.FileToImport = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xlsx");
        await presenter.ImportAsync();

        Assert.Equal(new[] { ProductListPresenter.UnreadableFile }, view.FileProblems);
        Assert.Empty(view.Summaries);
        Assert.Equal(0, view.Reloads);
    }
}
