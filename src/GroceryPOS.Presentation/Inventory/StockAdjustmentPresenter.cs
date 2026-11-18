using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Presentation.Mvp;
using Microsoft.Extensions.DependencyInjection;

namespace GroceryPOS.Presentation.Inventory;

/// <summary>The Điều chỉnh tồn kho dialog as seen by its presenter.</summary>
public interface IStockAdjustmentView : IView
{
    /// <summary>Commits a half-typed quantity and returns it (the numeric box only applies its text on leave/Enter).</summary>
    decimal CommitQuantity();

    void FocusQuantity();
}

/// <summary>A reason in the Lý do list, with the direction it implies.</summary>
public sealed record StockAdjustmentReasonOption(StockAdjustmentReason Reason, string Name);

/// <summary>
/// Điều chỉnh tồn kho logic over <see cref="StockAdjustmentDraft"/>: header texts, reason list, the live preview
/// (stock and value after, or why it cannot be saved), the target-balance warning and the save call.
/// </summary>
public sealed class StockAdjustmentPresenter : Presenter<IStockAdjustmentView>
{
    public const string Title = "Điều chỉnh tồn kho";
    public const string DefaultNotePlaceholder = "Ví dụ: lô hết hạn 20/10, vỡ khi vận chuyển…";
    public const string RequiredNotePlaceholder = "Bắt buộc với lý do khác";

    private StockAdjustmentDraft? _draft;

    public StockAdjustmentPresenter(IServiceScopeFactory scopeFactory)
        : base(scopeFactory)
    {
    }

    public static IReadOnlyList<StockAdjustmentReasonOption> ReasonOptions { get; } =
        StockAdjustmentDraft.Reasons.Select(r => new StockAdjustmentReasonOption(r, Describe(r))).ToList();

    public StockAdjustmentDraft Draft => _draft ?? throw new InvalidOperationException("Call For(product) before using the presenter.");

    public StockItem Product => Draft.Product;

    /// <summary>The saved adjustment, once <see cref="SaveAsync"/> succeeded.</summary>
    public StockAdjustmentResult? Adjusted { get; private set; }

    public string Heading => $"{Product.ProductCode} – {Product.ProductName}";

    public string CurrentStockText =>
        $"Tồn hiện tại: {Qty(Product.StockQuantity)} {Product.Unit} · định mức {Qty(Product.MinStockLevel)} · giá vốn {Vnd.Format(Product.CostPrice)}";

    public string QuantityCaption => $"Số lượng điều chỉnh ({Product.Unit})";

    public string NotePlaceholder => Draft.NoteRequired ? RequiredNotePlaceholder : DefaultNotePlaceholder;

    /// <summary>Decreases are saved with the red button.</summary>
    public bool SaveIsDanger => !Draft.Increase;

    /// <summary>The value the "tồn thực tế sau điều chỉnh" box shows: empty until a quantity is entered.</summary>
    public decimal? SuggestedTarget => Draft.Quantity > 0 ? Math.Max(0, Draft.BalanceAfter) : null;

    /// <summary>The live preview under the fields.</summary>
    public (string Text, Tone Tone) Preview
    {
        get
        {
            var draft = Draft;
            if (draft.Quantity <= 0)
            {
                return ("Nhập số lượng cần điều chỉnh hoặc số tồn thực tế đếm được.", Tone.Muted);
            }

            if (draft.Blocker is { } blocker)
            {
                return (blocker, Tone.Danger);
            }

            var sign = draft.Increase ? "+" : "−";
            var low = draft.WillBeLowStock ? "\n⚠ Sau điều chỉnh sản phẩm sẽ dưới định mức tồn kho." : string.Empty;
            var text = $"Tồn: {Qty(Product.StockQuantity)} → {Qty(draft.BalanceAfter)} {Product.Unit} ({sign}{Qty(draft.Quantity)})\n"
                + $"Giá trị điều chỉnh: {Vnd.Format(draft.CostValue)}{low}";
            return (text, draft.WillBeLowStock ? Tone.Warning : draft.Increase ? Tone.Success : Tone.Danger);
        }
    }

    public static string Describe(StockAdjustmentReason reason) => reason.Direction() switch
    {
        < 0 => $"{reason.DisplayName()} (giảm)",
        > 0 => $"{reason.DisplayName()} (tăng)",
        _ => $"{reason.DisplayName()} (tăng hoặc giảm)",
    };

    /// <summary>Chooses the product to adjust; call before the view is built.</summary>
    public StockAdjustmentPresenter For(StockItem product)
    {
        ArgumentNullException.ThrowIfNull(product);
        _draft = new StockAdjustmentDraft(product);
        Adjusted = null;
        return this;
    }

    public void SetReason(StockAdjustmentReason reason) => Draft.SetReason(reason);

    public void SetIncrease(bool increase) => Draft.SetIncrease(increase);

    public void SetQuantity(decimal quantity) => Draft.SetQuantity(quantity);

    public void SetNote(string? note) => Draft.SetNote(note);

    /// <summary>Sets the quantity from the stock counted on the shelf; the warning for the target box, or null when applied.</summary>
    public string? SetTargetBalance(decimal target) =>
        Draft.SetTargetBalance(target)
            ? null
            : $"Lý do \"{Draft.Reason.DisplayName()}\" không thể đưa tồn về {Qty(target)}.";

    /// <summary>Saves the adjustment; the message for the dialog's error banner, or null when saved.</summary>
    public async Task<string?> SaveAsync()
    {
        Draft.SetQuantity(View.CommitQuantity());
        var request = Draft.BuildRequest();
        if (request.IsFailure)
        {
            if (Draft.Quantity <= 0)
            {
                View.FocusQuantity();
            }

            return request.Error.Message;
        }

        var result = await UseAsync<IInventoryService, Result<StockAdjustmentResult>>(inventory => inventory.AdjustAsync(request.Value));
        if (result.IsFailure)
        {
            return result.Error.Message;
        }

        Adjusted = result.Value;
        return null;
    }

    private static string Qty(decimal value) => StockCountSheet.FormatQty(value);
}
