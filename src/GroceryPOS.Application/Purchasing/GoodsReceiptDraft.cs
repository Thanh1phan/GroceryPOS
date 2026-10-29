using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Purchasing;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Purchasing;

/// <summary>Outcome of a scan on the goods receipt editor.</summary>
public enum GoodsReceiptScanOutcome
{
    /// <summary>Nothing typed.</summary>
    Empty = 0,

    /// <summary>A new line was added at the last cost price.</summary>
    Added = 1,

    /// <summary>The product was already on the receipt; its quantity was increased.</summary>
    Increased = 2,

    /// <summary>No product has this barcode or code.</summary>
    NotFound = 3,

    /// <summary>The product exists but is no longer sold (the domain refuses to receive it).</summary>
    Inactive = 4,
}

public sealed record GoodsReceiptScanResult(GoodsReceiptScanOutcome Outcome, GoodsReceiptDraftLine? Line, string Message);

/// <summary>
/// One product on the goods receipt editor, with the same amount pipeline as <see cref="GoodsReceiptLine"/>
/// (cost × quantity − line discount − share of the receipt discount) and a preview of what completing it will do to
/// the product: new moving-average cost (as <c>Product.ApplyPurchaseCost</c>), shelf price after and margin.
/// </summary>
public sealed class GoodsReceiptDraftLine
{
    internal GoodsReceiptDraftLine(StockItem product, decimal quantity, decimal unitCost)
    {
        Product = product;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public StockItem Product { get; }

    public int ProductId => Product.ProductId;

    public string ProductCode => Product.ProductCode;

    public string ProductName => Product.ProductName;

    public string Unit => Product.Unit;

    /// <summary>Stock when the editor was opened (tồn hiện tại).</summary>
    public decimal StockBefore => Product.StockQuantity;

    /// <summary>Cost price before this receipt (giá vốn hiện tại).</summary>
    public decimal CurrentCost => Product.CostPrice;

    public decimal CurrentSellPrice => Product.SellPrice;

    public decimal Quantity { get; internal set; }

    /// <summary>Supplier price per unit before discounts.</summary>
    public decimal UnitCost { get; internal set; }

    public decimal DiscountAmount { get; internal set; }

    /// <summary>Optional new shelf price applied on completion.</summary>
    public decimal? NewSellPrice { get; internal set; }

    public decimal AllocatedDiscount { get; internal set; }

    public decimal GrossAmount => Money.Round(UnitCost * Quantity);

    public decimal NetAmount => GrossAmount - DiscountAmount;

    public decimal FinalAmount => NetAmount - AllocatedDiscount;

    /// <summary>Landed unit cost after all discounts (giá nhập thực tế).</summary>
    public decimal EffectiveUnitCost => Quantity > 0 ? Money.Round(FinalAmount / Quantity) : 0;

    public decimal StockAfter => StockBefore + Quantity;

    /// <summary>Weighted-average cost after receiving this line – same formula as <c>Product.ApplyPurchaseCost</c>.</summary>
    public decimal NewAverageCost => StockBefore <= 0
        ? EffectiveUnitCost
        : Math.Round(((StockBefore * CurrentCost) + (Quantity * EffectiveUnitCost)) / (StockBefore + Quantity), 0, MidpointRounding.AwayFromZero);

    public decimal SellPriceAfter => NewSellPrice ?? CurrentSellPrice;

    /// <summary>Selling at or below the new cost – same rule as <see cref="GoodsReceiptPostedLine.IsBelowCost"/>.</summary>
    public bool IsBelowCost => SellPriceAfter <= NewAverageCost;

    /// <summary>Gross margin of the shelf price after receiving, in percent (null when the shelf price is 0).</summary>
    public decimal? MarginPercent => SellPriceAfter > 0
        ? Math.Round((SellPriceAfter - NewAverageCost) * 100 / SellPriceAfter, 1, MidpointRounding.AwayFromZero)
        : null;

    /// <summary>Landed cost vs. the current cost in percent (null when there is no current cost).</summary>
    public decimal? CostChangePercent => CurrentCost > 0
        ? Math.Round((EffectiveUnitCost - CurrentCost) * 100 / CurrentCost, 1, MidpointRounding.AwayFromZero)
        : null;

    /// <summary>Short status for the grid.</summary>
    public string StatusName =>
        !Product.IsActive ? "Ngừng KD"
        : IsBelowCost ? "Bán ≤ giá vốn"
        : UnitCost == 0 ? "Giá nhập 0 ₫"
        : CostChangePercent is >= GoodsReceiptDraft.CostJumpPercent ? "Giá nhập tăng"
        : NewSellPrice is not null && NewSellPrice != CurrentSellPrice ? "Đổi giá bán"
        : string.Empty;
}

/// <summary>
/// UI-free state of the goods receipt editor (phiếu nhập hàng): supplier, lines scanned (<c>24*mã</c>) or picked
/// at the last cost price, line and receipt discounts allocated like the domain, amount paid (or "trả đủ"),
/// the resulting supplier debt, a per-line preview of the new average cost and margin, the blocker that keeps
/// "Lưu" / "Hoàn thành" disabled and <see cref="BuildRequest"/> for <see cref="IGoodsReceiptService.SaveDraftAsync"/>.
/// </summary>
public sealed class GoodsReceiptDraft
{
    /// <summary>A landed cost this many percent above the current cost is flagged.</summary>
    public const decimal CostJumpPercent = 20;

    private readonly IReadOnlyList<StockItem> _catalog;
    private readonly Dictionary<int, StockItem> _byId;
    private readonly List<GoodsReceiptDraftLine> _lines = [];
    private decimal _paidAmount;

    public GoodsReceiptDraft(IEnumerable<StockItem> catalog, bool canChangePrices)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog.ToList();
        _byId = _catalog.GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.First());
        CanChangePrices = canChangePrices;
    }

    /// <summary>Raised whenever anything on the receipt changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<StockItem> Catalog => _catalog;

    /// <summary>Whether the user may set new shelf prices (<c>prices.change</c>).</summary>
    public bool CanChangePrices { get; }

    /// <summary>Id of the saved draft; null until first saved.</summary>
    public int? Id { get; private set; }

    /// <summary>Receipt code (<c>PN261029-0001</c>) once saved.</summary>
    public string? Code { get; private set; }

    public int? SupplierId { get; private set; }

    public string? SupplierInvoiceNo { get; private set; }

    public string? Note { get; private set; }

    public decimal DiscountAmount { get; private set; }

    /// <summary>When set, the amount paid follows <see cref="Total"/> (thanh toán đủ).</summary>
    public bool PayInFull { get; private set; }

    /// <summary>True when something changed since the draft was loaded or last saved.</summary>
    public bool IsDirty { get; private set; }

    public IReadOnlyList<GoodsReceiptDraftLine> Lines => _lines;

    public decimal TotalQuantity => _lines.Sum(l => l.Quantity);

    public decimal GrossAmount => _lines.Sum(l => l.GrossAmount);

    public decimal LineDiscountTotal => _lines.Sum(l => l.DiscountAmount);

    /// <summary>After line discounts, before the receipt discount.</summary>
    public decimal Subtotal => _lines.Sum(l => l.NetAmount);

    public decimal Total => Math.Max(0, Subtotal - DiscountAmount);

    public decimal PaidAmount => PayInFull ? Total : _paidAmount;

    /// <summary>Unpaid part that becomes supplier debt on completion.</summary>
    public decimal DebtAmount => Math.Max(0, Total - PaidAmount);

    public IEnumerable<GoodsReceiptDraftLine> BelowCostLines => _lines.Where(l => l.IsBelowCost);

    /// <summary>Why the receipt cannot be saved or completed; null when it can.</summary>
    public string? Blocker =>
        SupplierId is null ? "Chưa chọn nhà cung cấp."
        : _lines.Count == 0 ? PurchasingErrors.EmptyReceipt.Message
        : _lines.FirstOrDefault(l => !l.Product.IsActive) is { } inactive ? $"Sản phẩm \"{inactive.ProductName}\" đã ngừng kinh doanh – hãy bỏ dòng này."
        : DiscountAmount > Subtotal ? PurchasingErrors.DiscountTooLarge.Message
        : PaidAmount > Total ? PurchasingErrors.OverPaid.Message
        : !CanChangePrices && _lines.Any(l => l.NewSellPrice is not null) ? PurchasingErrors.PriceChangeNotAllowed.Message
        : null;

    /// <summary>Non-blocking hints (below cost, zero cost, cost jumps), one per line.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var warnings = new List<string>();
            var below = _lines.Where(l => l.IsBelowCost).ToList();
            if (below.Count > 0)
            {
                warnings.Add($"{below.Count} SP có giá bán ≤ giá vốn mới: {string.Join(", ", below.Take(3).Select(l => l.ProductName))}{(below.Count > 3 ? "…" : string.Empty)}");
            }

            var free = _lines.Count(l => l.UnitCost == 0);
            if (free > 0)
            {
                warnings.Add($"{free} SP có giá nhập 0 ₫ (hàng tặng?).");
            }

            var jumps = _lines.Count(l => l.CostChangePercent >= CostJumpPercent);
            if (jumps > 0)
            {
                warnings.Add($"{jumps} SP có giá nhập tăng từ {CostJumpPercent:0}% trở lên so với giá vốn hiện tại.");
            }

            return warnings;
        }
    }

    public bool CanSave => Blocker is null;

    /// <summary>Rebuilds the editor from a saved draft (lines whose product is no longer in the catalog keep their snapshot).</summary>
    public static GoodsReceiptDraft FromDto(IEnumerable<StockItem> catalog, bool canChangePrices, GoodsReceiptDto receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var draft = new GoodsReceiptDraft(catalog, canChangePrices)
        {
            Id = receipt.Id,
            Code = receipt.Code,
            SupplierId = receipt.SupplierId,
            SupplierInvoiceNo = receipt.SupplierInvoiceNo,
            Note = receipt.Note,
            DiscountAmount = receipt.DiscountAmount,
        };
        foreach (var l in receipt.Lines)
        {
            var product = draft._byId.GetValueOrDefault(l.ProductId)
                ?? new StockItem(l.ProductId, l.ProductCode, null, l.ProductName, 0, string.Empty, l.Unit, 0, 0, l.UnitCost, 0, IsActive: false);
            draft._lines.Add(new GoodsReceiptDraftLine(product, l.Quantity, l.UnitCost)
            {
                DiscountAmount = l.DiscountAmount,
                NewSellPrice = l.NewSellPrice,
            });
        }

        draft.PayInFull = receipt.Total > 0 && receipt.PaidAmount == receipt.Total;
        draft._paidAmount = receipt.PaidAmount;
        draft.Allocate();
        return draft;
    }

    public GoodsReceiptDraftLine? Find(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);

    public void SetSupplier(int? supplierId)
    {
        if (supplierId != SupplierId)
        {
            SupplierId = supplierId is > 0 ? supplierId : null;
            OnChanged();
        }
    }

    public void SetReference(string? supplierInvoiceNo, string? note)
    {
        var invoice = Clean(supplierInvoiceNo);
        var cleanNote = Clean(note);
        if (invoice != SupplierInvoiceNo || cleanNote != Note)
        {
            SupplierInvoiceNo = invoice;
            Note = cleanNote;
            OnChanged();
        }
    }

    /// <summary>
    /// Adds a product at its last cost price, or increases the quantity of its line. Inactive products are refused
    /// as the domain would refuse them on save.
    /// </summary>
    public Result<GoodsReceiptDraftLine> Add(int productId, decimal quantity = 1)
    {
        if (!_byId.TryGetValue(productId, out var product))
        {
            return PurchasingErrors.ProductNotFound(productId);
        }

        if (!product.IsActive)
        {
            return Error.Validation($"Sản phẩm \"{product.ProductName}\" đã ngừng kinh doanh.");
        }

        if (Find(productId) is { } existing)
        {
            var increased = RoundQty(existing.Quantity + quantity);
            if (increased > MaxQuantity)
            {
                return Error.Validation($"Số lượng nhập của \"{product.ProductName}\" quá lớn.");
            }

            existing.Quantity = increased;
            OnChanged();
            return existing;
        }

        var rounded = RoundQty(quantity);
        if (rounded <= 0 || rounded > MaxQuantity)
        {
            return Error.Validation($"Số lượng nhập của \"{product.ProductName}\" không hợp lệ.");
        }

        var line = new GoodsReceiptDraftLine(product, rounded, Money.Round(Math.Max(0, product.CostPrice)));
        _lines.Add(line);
        OnChanged();
        return line;
    }

    /// <summary>Barcode or product code scan (<c>24*8934588012112</c> = 24 units).</summary>
    public GoodsReceiptScanResult Scan(string? input)
    {
        var (quantity, text) = PosCart.ParseScan(input);
        if (text.Length == 0)
        {
            return new GoodsReceiptScanResult(GoodsReceiptScanOutcome.Empty, null, string.Empty);
        }

        var product = _catalog.FirstOrDefault(p => string.Equals(p.Barcode, text, StringComparison.Ordinal))
            ?? _catalog.FirstOrDefault(p => string.Equals(p.ProductCode, text, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return new GoodsReceiptScanResult(GoodsReceiptScanOutcome.NotFound, null, $"Không tìm thấy sản phẩm có mã \"{text}\".");
        }

        if (!product.IsActive)
        {
            return new GoodsReceiptScanResult(GoodsReceiptScanOutcome.Inactive, null, $"Sản phẩm \"{product.ProductName}\" đã ngừng kinh doanh, không thể nhập.");
        }

        var existed = Find(product.ProductId) is not null;
        var added = Add(product.ProductId, quantity);
        if (added.IsFailure)
        {
            return new GoodsReceiptScanResult(GoodsReceiptScanOutcome.NotFound, null, added.Error.Message);
        }

        var line = added.Value;
        return new GoodsReceiptScanResult(
            existed ? GoodsReceiptScanOutcome.Increased : GoodsReceiptScanOutcome.Added,
            line,
            $"{line.ProductName}: SL {FormatQty(line.Quantity)} {line.Unit} × {Vnd.Format(line.UnitCost)}");
    }

    /// <summary>Adds reorder suggestions not on the receipt yet (quantity and last cost pre-filled); returns how many were added.</summary>
    public int AddSuggestions(IEnumerable<ReorderSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        var added = 0;
        foreach (var s in suggestions)
        {
            if (s.SuggestedQuantity <= 0 || Find(s.ProductId) is not null
                || !_byId.TryGetValue(s.ProductId, out var product) || !product.IsActive)
            {
                continue;
            }

            _lines.Add(new GoodsReceiptDraftLine(product, RoundQty(s.SuggestedQuantity), Money.Round(Math.Max(0, s.LastCostPrice))));
            added++;
        }

        if (added > 0)
        {
            OnChanged();
        }

        return added;
    }

    /// <summary>Edits a line with the same rules (and messages) as <see cref="GoodsReceiptLine"/>; a rejected edit changes nothing.</summary>
    public Result UpdateLine(int productId, decimal quantity, decimal unitCost, decimal discountAmount = 0, decimal? newSellPrice = null)
    {
        if (Find(productId) is not { } line)
        {
            return Error.NotFound("Sản phẩm không có trong phiếu nhập.");
        }

        var name = line.ProductName;
        var qty = RoundQty(quantity);
        if (qty <= 0)
        {
            return Error.Validation($"Số lượng nhập của \"{name}\" phải lớn hơn 0.");
        }

        if (qty > MaxQuantity)
        {
            return Error.Validation($"Số lượng nhập của \"{name}\" quá lớn.");
        }

        if (unitCost < 0)
        {
            return Error.Validation($"Giá nhập của \"{name}\" không được âm.");
        }

        if (discountAmount < 0)
        {
            return Error.Validation($"Chiết khấu của \"{name}\" không được âm.");
        }

        if (newSellPrice is < 0)
        {
            return Error.Validation($"Giá bán mới của \"{name}\" không được âm.");
        }

        var cost = Money.Round(unitCost);
        var discount = Money.Round(discountAmount);
        if (discount > Money.Round(cost * qty))
        {
            return Error.Validation($"Chiết khấu của \"{name}\" vượt quá thành tiền.");
        }

        var price = newSellPrice is { } p ? Money.Round(p) : (decimal?)null;
        if (price is not null && price != line.NewSellPrice && !CanChangePrices)
        {
            return PurchasingErrors.PriceChangeNotAllowed;
        }

        line.Quantity = qty;
        line.UnitCost = cost;
        line.DiscountAmount = discount;
        line.NewSellPrice = price;
        OnChanged();
        return Result.Success();
    }

    public bool Remove(int productId)
    {
        var removed = _lines.RemoveAll(l => l.ProductId == productId) > 0;
        if (removed)
        {
            OnChanged();
        }

        return removed;
    }

    public Result SetDiscount(decimal amount)
    {
        if (amount < 0)
        {
            return Error.Validation("Chiết khấu phiếu nhập không được âm.");
        }

        DiscountAmount = Money.Round(amount);
        OnChanged();
        return Result.Success();
    }

    /// <summary>Sets the amount paid now and turns "trả đủ" off.</summary>
    public Result SetPaidAmount(decimal amount)
    {
        if (amount < 0)
        {
            return Error.Validation("Số tiền thanh toán không được âm.");
        }

        PayInFull = false;
        _paidAmount = Money.Round(amount);
        OnChanged();
        return Result.Success();
    }

    public void SetPayInFull(bool payInFull)
    {
        if (payInFull != PayInFull)
        {
            if (!payInFull)
            {
                _paidAmount = Total;
            }

            PayInFull = payInFull;
            OnChanged();
        }
    }

    /// <summary>The request for <see cref="IGoodsReceiptService.SaveDraftAsync"/>; fails with the blocker.</summary>
    public Result<SaveGoodsReceiptRequest> BuildRequest()
    {
        if (Blocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        var lines = _lines
            .Select(l => new GoodsReceiptLineInput(l.ProductId, l.Quantity, l.UnitCost, l.DiscountAmount, l.NewSellPrice))
            .ToList();
        return new SaveGoodsReceiptRequest(Id, SupplierId!.Value, lines, DiscountAmount, PaidAmount, SupplierInvoiceNo, Note);
    }

    /// <summary>Records that the draft was saved (keeps the lines, takes the id and code).</summary>
    public void MarkSaved(GoodsReceiptDto saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Id = saved.Id;
        Code = saved.Code;
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string FormatQty(decimal quantity) => quantity.ToString("#,0.###", Vnd.Culture);

    private const decimal MaxQuantity = 1_000_000;

    private static decimal RoundQty(decimal quantity) =>
        Math.Round(quantity, GoodsReceiptLine.QuantityDecimals, MidpointRounding.AwayFromZero);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void Allocate()
    {
        var shares = Money.Allocate(Math.Min(DiscountAmount, Subtotal), _lines.Select(l => l.NetAmount).ToList());
        for (var i = 0; i < _lines.Count; i++)
        {
            _lines[i].AllocatedDiscount = shares[i];
        }
    }

    private void OnChanged()
    {
        Allocate();
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
