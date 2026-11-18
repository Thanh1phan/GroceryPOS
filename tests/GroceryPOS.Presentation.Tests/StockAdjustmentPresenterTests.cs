using GroceryPOS.Application.Common;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Presentation.Inventory;
using GroceryPOS.Presentation.Mvp;

namespace GroceryPOS.Presentation.Tests;

public sealed class StockAdjustmentPresenterTests
{
    private sealed class FakeAdjustmentView : RecordingView, IStockAdjustmentView
    {
        public decimal TypedQuantity { get; set; }
        public int QuantityFocused { get; private set; }

        public decimal CommitQuantity() => TypedQuantity;

        public void FocusQuantity() => QuantityFocused++;
    }

    private static readonly StockItem Milk = new(1, "SP001", "8930001", "Sữa tươi", 1, "Sữa", "hộp", 10, 5, 7_000, 9_000, true);

    private static (TestHost Host, StockAdjustmentPresenter Presenter, FakeAdjustmentView View) Create()
    {
        var host = new TestHost(Permissions.InventoryAdjust);
        host.Inventory.Stock.Add(Milk);
        var presenter = host.Get<StockAdjustmentPresenter>().For(Milk);
        var view = new FakeAdjustmentView();
        presenter.Attach(view);
        return (host, presenter, view);
    }

    [Fact]
    public void Header_texts_describe_the_product()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        Assert.Equal("SP001 – Sữa tươi", presenter.Heading);
        Assert.StartsWith("Tồn hiện tại: 10 hộp · định mức 5 · giá vốn 7.000", presenter.CurrentStockText);
        Assert.Equal("Số lượng điều chỉnh (hộp)", presenter.QuantityCaption);
    }

    [Fact]
    public void Reason_options_follow_the_draft_order_and_name_their_direction()
    {
        var options = StockAdjustmentPresenter.ReasonOptions;

        Assert.Equal(StockAdjustmentDraft.Reasons, options.Select(o => o.Reason));
        Assert.EndsWith("(giảm)", options[0].Name);
        Assert.EndsWith("(tăng hoặc giảm)", options.Single(o => o.Reason == StockAdjustmentReason.Other).Name);
    }

    [Fact]
    public void Preview_moves_from_hint_to_result_to_low_stock_warning_to_blocker()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        Assert.Equal(Tone.Muted, presenter.Preview.Tone);
        Assert.Null(presenter.SuggestedTarget);

        presenter.SetQuantity(2);
        Assert.Equal(Tone.Danger, presenter.Preview.Tone); // a decrease above the minimum
        Assert.StartsWith("Tồn: 10 → 8 hộp (−2)", presenter.Preview.Text);
        Assert.Equal(8, presenter.SuggestedTarget);
        Assert.True(presenter.SaveIsDanger);

        presenter.SetQuantity(6);
        Assert.Equal(Tone.Warning, presenter.Preview.Tone);
        Assert.Contains("dưới định mức", presenter.Preview.Text);

        presenter.SetQuantity(12);
        Assert.Equal(Tone.Danger, presenter.Preview.Tone);
        Assert.Contains("không thể giảm 12", presenter.Preview.Text);
        Assert.Equal(0, presenter.SuggestedTarget);
    }

    [Fact]
    public void Increase_reason_previews_in_green_with_the_primary_button()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        presenter.SetReason(StockAdjustmentReason.Found);
        presenter.SetQuantity(3);

        Assert.Equal(Tone.Success, presenter.Preview.Tone);
        Assert.False(presenter.SaveIsDanger);
    }

    [Fact]
    public void Target_balance_in_the_wrong_direction_returns_a_warning()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        Assert.Equal("Lý do \"" + StockAdjustmentReason.Damaged.DisplayName() + "\" không thể đưa tồn về 12.", presenter.SetTargetBalance(12));
        Assert.Null(presenter.SetTargetBalance(7));
        Assert.Equal(3, presenter.Draft.Quantity);
    }

    [Fact]
    public void Other_reason_requires_a_note()
    {
        var (host, presenter, _) = Create();
        using var _h = host;

        presenter.SetReason(StockAdjustmentReason.Other);
        presenter.SetQuantity(1);
        Assert.Equal(StockAdjustmentPresenter.RequiredNotePlaceholder, presenter.NotePlaceholder);
        Assert.False(presenter.Draft.CanSave);

        presenter.SetNote("  đếm lại kệ  ");
        Assert.True(presenter.Draft.CanSave);
    }

    [Fact]
    public async Task Save_commits_the_typed_quantity_and_calls_the_service()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        view.TypedQuantity = 2;

        Assert.Null(await presenter.SaveAsync());

        Assert.Equal(new StockAdjustmentRequest(1, -2, StockAdjustmentReason.Damaged), host.Inventory.LastAdjustment);
        Assert.Equal(8, presenter.Adjusted!.BalanceAfter);
    }

    [Fact]
    public async Task Save_without_quantity_focuses_it_and_does_not_call_the_service()
    {
        var (host, presenter, view) = Create();
        using var _h = host;

        Assert.Equal("Nhập số lượng điều chỉnh lớn hơn 0.", await presenter.SaveAsync());

        Assert.Equal(1, view.QuantityFocused);
        Assert.Empty(host.Inventory.Calls);
        Assert.Null(presenter.Adjusted);
    }

    [Fact]
    public async Task Service_failure_is_returned_for_the_error_banner()
    {
        var (host, presenter, view) = Create();
        using var _h = host;
        host.Inventory.FailAdjustWith = Error.Forbidden();
        view.TypedQuantity = 1;

        Assert.Equal(Error.Forbidden().Message, await presenter.SaveAsync());
        Assert.Null(presenter.Adjusted);
    }

    [Fact]
    public void Using_the_presenter_before_for_throws()
    {
        using var host = new TestHost();
        Assert.Throws<InvalidOperationException>(() => host.Get<StockAdjustmentPresenter>().Draft);
    }
}
