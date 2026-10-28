using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Inventory;

namespace GroceryPOS.Application.Inventory;

/// <summary>Outcome of a scan on the count sheet.</summary>
public enum StockCountScanOutcome
{
    /// <summary>Nothing typed.</summary>
    Empty = 0,

    /// <summary>The product's counted quantity was increased.</summary>
    Counted = 1,

    /// <summary>No product has this barcode or code.</summary>
    NotFound = 2,
}

public sealed record StockCountScanResult(StockCountScanOutcome Outcome, StockCountSheetLine? Line, string Message);

/// <summary>One product on the count sheet. <see cref="Counted"/> is null until the product has been counted.</summary>
public sealed class StockCountSheetLine
{
    internal StockCountSheetLine(StockItem product) => Product = product;

    public StockItem Product { get; }

    public int ProductId => Product.ProductId;

    public string ProductCode => Product.ProductCode;

    public string ProductName => Product.ProductName;

    public string CategoryName => Product.CategoryName;

    public string Unit => Product.Unit;

    /// <summary>Book quantity when the sheet was loaded (tồn sổ sách).</summary>
    public decimal Recorded => Product.StockQuantity;

    public decimal UnitCost => Product.CostPrice;

    public decimal? Counted { get; internal set; }

    public bool IsCounted => Counted.HasValue;

    /// <summary>Counted − recorded (negative = thiếu); null while not counted.</summary>
    public decimal? Variance => Counted is { } counted ? counted - Recorded : null;

    /// <summary>Same rounding as <see cref="StockCountLineResult.VarianceValue"/>.</summary>
    public decimal VarianceValue => Variance is { } v ? Math.Round(v * UnitCost, 0, MidpointRounding.AwayFromZero) : 0;

    public string StatusName => Variance switch
    {
        null => "Chưa đếm",
        < 0 => "Thiếu",
        > 0 => "Thừa",
        _ => "Khớp",
    };
}

/// <summary>
/// UI-free state of the stock count screen (phiếu kiểm kê): products to count (picked one by one, by category or
/// all at once), counted quantities typed or scanned (each scan +1, <c>12*BIA01</c> +12), variance and its value
/// per line and in total, and the blocker that keeps "Hoàn tất kiểm kê" disabled. Lines that were never counted
/// are left out of <see cref="BuildRequest"/>, so their stock stays unchanged (counting one shelf at a time).
/// </summary>
public sealed class StockCountSheet
{
    private readonly IReadOnlyList<StockItem> _catalog;
    private readonly Dictionary<int, StockItem> _byId;
    private readonly List<StockCountSheetLine> _lines = [];

    public StockCountSheet(IEnumerable<StockItem> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog.ToList();
        _byId = _catalog.GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Raised whenever a line, a count or the note changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Every product that may be counted (inactive ones included: they can still sit on a shelf).</summary>
    public IReadOnlyList<StockItem> Catalog => _catalog;

    public IReadOnlyList<StockCountSheetLine> Lines => _lines;

    public string? Note { get; private set; }

    public int CountedCount => _lines.Count(l => l.IsCounted);

    public int UncountedCount => _lines.Count - CountedCount;

    public int MismatchCount => _lines.Count(l => l.Variance is { } v && v != 0);

    /// <summary>Value of shortages (≤ 0).</summary>
    public decimal ShortageValue => _lines.Where(l => l.Variance < 0).Sum(l => l.VarianceValue);

    /// <summary>Value of surpluses (≥ 0).</summary>
    public decimal SurplusValue => _lines.Where(l => l.Variance > 0).Sum(l => l.VarianceValue);

    public decimal NetVarianceValue => _lines.Sum(l => l.VarianceValue);

    /// <summary>Why the count cannot be completed yet; null when it can.</summary>
    public string? Blocker =>
        _lines.Count == 0 ? "Chưa có sản phẩm nào trong phiếu kiểm kê."
        : CountedCount == 0 ? "Chưa nhập số lượng thực tế cho sản phẩm nào."
        : null;

    /// <summary>Non-blocking hint shown next to the totals.</summary>
    public string? Warning => UncountedCount > 0 && CountedCount > 0
        ? $"{UncountedCount:#,0} sản phẩm chưa đếm sẽ giữ nguyên tồn kho."
        : null;

    public bool CanComplete => Blocker is null;

    public StockCountSheetLine? Find(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);

    /// <summary>Adds a product (or returns its existing line).</summary>
    public StockCountSheetLine? Add(int productId)
    {
        if (Find(productId) is { } existing)
        {
            return existing;
        }

        if (!_byId.TryGetValue(productId, out var product))
        {
            return null;
        }

        var line = new StockCountSheetLine(product);
        _lines.Add(line);
        OnChanged();
        return line;
    }

    /// <summary>Adds every active product of a category not on the sheet yet; returns how many were added.</summary>
    public int AddCategory(int categoryId) => AddRange(_catalog.Where(p => p.IsActive && p.CategoryId == categoryId));

    /// <summary>Adds every active product not on the sheet yet; returns how many were added.</summary>
    public int AddAllActive() => AddRange(_catalog.Where(p => p.IsActive));

    public bool Remove(int productId)
    {
        var removed = _lines.RemoveAll(l => l.ProductId == productId) > 0;
        if (removed)
        {
            OnChanged();
        }

        return removed;
    }

    /// <summary>Removes the lines that have not been counted; returns how many were removed.</summary>
    public int RemoveUncounted()
    {
        var removed = _lines.RemoveAll(l => !l.IsCounted);
        if (removed > 0)
        {
            OnChanged();
        }

        return removed;
    }

    /// <summary>Sets (or with null clears) the counted quantity of a product, adding it to the sheet if needed.</summary>
    public Result SetCounted(int productId, decimal? counted)
    {
        var line = Find(productId) ?? Add(productId);
        if (line is null)
        {
            return InventoryErrors.ProductNotFound(productId);
        }

        if (counted is < 0)
        {
            return InventoryErrors.NegativeCount(line.ProductName);
        }

        line.Counted = counted is { } c ? Round(c) : null;
        OnChanged();
        return Result.Success();
    }

    /// <summary>Copies the book quantity into every uncounted line ("khớp sổ sách"); returns how many were set.</summary>
    public int MarkUncountedAsRecorded()
    {
        var changed = 0;
        foreach (var line in _lines.Where(l => !l.IsCounted))
        {
            line.Counted = Math.Max(0, line.Recorded);
            changed++;
        }

        if (changed > 0)
        {
            OnChanged();
        }

        return changed;
    }

    /// <summary>
    /// Barcode or product code scan: adds the product if needed and increases its count by one, or by the
    /// quantity prefix (<c>12*8934588012112</c>).
    /// </summary>
    public StockCountScanResult Scan(string? input)
    {
        var (quantity, text) = PosCart.ParseScan(input);
        if (text.Length == 0)
        {
            return new StockCountScanResult(StockCountScanOutcome.Empty, null, string.Empty);
        }

        var product = _catalog.FirstOrDefault(p => string.Equals(p.Barcode, text, StringComparison.Ordinal))
            ?? _catalog.FirstOrDefault(p => string.Equals(p.ProductCode, text, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return new StockCountScanResult(StockCountScanOutcome.NotFound, null, $"Không tìm thấy sản phẩm có mã \"{text}\".");
        }

        var line = Add(product.ProductId)!;
        line.Counted = Round((line.Counted ?? 0) + quantity);
        OnChanged();
        return new StockCountScanResult(
            StockCountScanOutcome.Counted,
            line,
            $"{line.ProductName}: đã đếm {FormatQty(line.Counted.Value)} {line.Unit}");
    }

    public void SetNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed != Note)
        {
            Note = trimmed;
            OnChanged();
        }
    }

    /// <summary>The request for <see cref="IInventoryService.CountAsync"/> with the counted lines only.</summary>
    public Result<StockCountRequest> BuildRequest()
    {
        if (Blocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        var lines = _lines
            .Where(l => l.IsCounted)
            .Select(l => new StockCountLine(l.ProductId, l.Counted!.Value))
            .ToList();
        return new StockCountRequest(lines, Note);
    }

    public static string FormatQty(decimal quantity) => quantity.ToString("#,0.###", Vnd.Culture);

    private int AddRange(IEnumerable<StockItem> products)
    {
        var added = 0;
        foreach (var product in products)
        {
            if (Find(product.ProductId) is null)
            {
                _lines.Add(new StockCountSheetLine(product));
                added++;
            }
        }

        if (added > 0)
        {
            OnChanged();
        }

        return added;
    }

    private static decimal Round(decimal quantity) =>
        Math.Round(quantity, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
