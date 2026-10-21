using GroceryPOS.Application.Catalog;
using GroceryPOS.UI.Controls.Buttons;
using GroceryPOS.UI.Controls.Grids;
using GroceryPOS.UI.Controls.Inputs;
using GroceryPOS.UI.Controls.Theming;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.App.Forms.Dialogs;

/// <summary>Read-only price-change history of one product (manual edits and goods-receipt cost changes alike).</summary>
internal sealed class PriceHistoryForm : Form
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _productId;
    private readonly ModernDatePicker _from;
    private readonly ModernDatePicker _to;
    private readonly ModernDataGrid _grid;

    public PriceHistoryForm(IServiceScopeFactory scopeFactory, int productId, string productName)
    {
        _scopeFactory = scopeFactory;
        _productId = productId;

        Text = $"Lịch sử giá – {productName}";
        ClientSize = new Size(1040, 480);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;

        _from = new ModernDatePicker { Location = new Point(16, 16), Width = 160, PlaceholderText = "Từ ngày" };
        _to = new ModernDatePicker { Location = new Point(184, 16), Width = 160, PlaceholderText = "Đến ngày" };

        var filter = new ModernButton { Text = "Lọc", Variant = ButtonVariant.Secondary, Location = new Point(352, 16), Size = new Size(80, 36) };
        filter.Click += async (_, _) => await ReloadAsync();

        var clear = new ModernButton { Text = "Bỏ lọc", Variant = ButtonVariant.Ghost, Location = new Point(440, 16), Size = new Size(80, 36) };
        clear.Click += async (_, _) =>
        {
            _from.Value = null;
            _to.Value = null;
            await ReloadAsync();
        };

        _grid = new ModernDataGrid { Location = new Point(16, 64), Size = new Size(1008, 400), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, EmptyText = "Chưa có thay đổi giá nào." };
        _grid.AddDateColumn(nameof(PriceHistoryEntry.ChangedAt), "Thời gian", withTime: true);
        _grid.AddMoneyColumn(nameof(PriceHistoryEntry.OldCostPrice), "Giá vốn cũ");
        _grid.AddMoneyColumn(nameof(PriceHistoryEntry.NewCostPrice), "Giá vốn mới");
        _grid.AddMoneyColumn(nameof(PriceHistoryEntry.OldSellPrice), "Giá bán cũ");
        _grid.AddMoneyColumn(nameof(PriceHistoryEntry.NewSellPrice), "Giá bán mới");
        _grid.AddTextColumn(nameof(PriceHistoryEntry.ChangedByUsername), "Người thay đổi", width: 120, searchable: false);
        _grid.AddTextColumn(nameof(PriceHistoryEntry.SourceName), "Nguồn", width: 100, searchable: false);
        _grid.AddTextColumn(nameof(PriceHistoryEntry.Note), "Ghi chú", width: 0);

        Controls.AddRange([_from, _to, filter, clear, _grid]);

        Load += async (_, _) =>
        {
            ThemeManager.Track(this);
            await ReloadAsync();
        };
    }

    private async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var priceHistory = scope.ServiceProvider.GetRequiredService<IPriceHistoryService>();
        var from = _from.Value is { } fromDate ? new DateTimeOffset(fromDate) : (DateTimeOffset?)null;
        var to = _to.Value is { } toDate ? new DateTimeOffset(toDate.AddDays(1)) : (DateTimeOffset?)null;
        var result = await priceHistory.ListByProductAsync(_productId, from, to);
        if (result.IsSuccess)
        {
            _grid.SetItems(result.Value);
        }
    }
}
