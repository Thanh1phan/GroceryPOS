using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Application.Inventory;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Inventory;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Purchasing;

/// <summary>One product on the supplier return screen with the quantity and price picked to send back.</summary>
public sealed class SupplierReturnSheetLine
{
    internal SupplierReturnSheetLine(
        int productId,
        string productCode,
        string? barcode,
        string productName,
        string unit,
        decimal stockQuantity,
        decimal costPrice,
        decimal? receivedQuantity,
        decimal returnedBefore,
        decimal maxQuantity,
        decimal defaultUnitPrice,
        decimal? maxUnitPrice)
    {
        ProductId = productId;
        ProductCode = productCode;
        Barcode = barcode;
        ProductName = productName;
        Unit = unit;
        StockQuantity = stockQuantity;
        CostPrice = costPrice;
        ReceivedQuantity = receivedQuantity;
        ReturnedBefore = returnedBefore;
        MaxQuantity = Math.Max(0, maxQuantity);
        DefaultUnitPrice = defaultUnitPrice;
        MaxUnitPrice = maxUnitPrice;
        UnitPrice = defaultUnitPrice;
    }

    public int ProductId { get; }
    public string ProductCode { get; }
    public string? Barcode { get; }
    public string ProductName { get; }
    public string Unit { get; }

    /// <summary>Stock on hand now; goods cannot be returned beyond it.</summary>
    public decimal StockQuantity { get; }

    /// <summary>Current moving-average cost: the value at which the goods leave stock.</summary>
    public decimal CostPrice { get; }

    /// <summary>Quantity on the goods receipt (null for a return not tied to a receipt).</summary>
    public decimal? ReceivedQuantity { get; }

    /// <summary>Quantity of the receipt line already returned by earlier returns.</summary>
    public decimal ReturnedBefore { get; }

    /// <summary>Most that can be returned now: receipt remainder and stock on hand, whichever is smaller.</summary>
    public decimal MaxQuantity { get; }

    /// <summary>Price used when the user does not type one: landed cost of the receipt line, or the current cost.</summary>
    public decimal DefaultUnitPrice { get; }

    /// <summary>Upper bound for the return price (landed cost of the receipt line); null = no bound.</summary>
    public decimal? MaxUnitPrice { get; }

    public decimal Quantity { get; internal set; }

    public decimal UnitPrice { get; internal set; }

    public bool IsSelected => Quantity > 0;

    /// <summary>Value of the line, rounded as <c>SupplierReturnLine.Amount</c>.</summary>
    public decimal Amount => Money.Round(Quantity * UnitPrice);

    /// <summary>Stock value leaving the store, as <c>SupplierReturnLine.CostValue</c>.</summary>
    public decimal CostValue => Money.Round(Quantity * CostPrice);

    public bool PriceChanged => UnitPrice != DefaultUnitPrice;

    /// <summary>Nothing more can be returned from this line (already returned or out of stock).</summary>
    public bool IsExhausted => MaxQuantity <= 0;

    /// <summary>Short note for the grid: why the line is limited.</summary>
    public string LimitText =>
        ReceivedQuantity is { } received && received - ReturnedBefore <= 0 ? "Đã trả hết"
        : StockQuantity <= 0 ? "Hết tồn kho"
        : ReceivedQuantity is { } r && StockQuantity < r - ReturnedBefore ? $"Tồn chỉ còn {SupplierReturnSheet.FormatQty(StockQuantity)}"
        : string.Empty;
}

public enum SupplierReturnScanOutcome
{
    Empty,
    NotFound,
    NotOnReceipt,
    AtMaximum,
    Added,
    Increased,
}

public sealed record SupplierReturnScanResult(SupplierReturnScanOutcome Outcome, SupplierReturnSheetLine? Line, string Message)
{
    public bool IsSuccess => Outcome is SupplierReturnScanOutcome.Added or SupplierReturnScanOutcome.Increased;
}

/// <summary>
/// UI-free state of the supplier return screen (phiếu trả hàng NCC <c>TN…</c>). Two modes: against a completed goods
/// receipt (every receipt line listed, capped by what is left to return, price capped by the landed cost) or a free
/// return to a chosen supplier (products scanned from stock at their current cost). Settlement is either automatic
/// (offset the supplier's debt first, take the rest back in money) or a typed refund amount. Blockers mirror
/// <c>SupplierReturn.Create</c>, so what the screen shows is what <see cref="ISupplierReturnService.CreateAsync"/> posts.
/// </summary>
public sealed class SupplierReturnSheet
{
    /// <summary>One-click reasons offered next to the reason box.</summary>
    public static readonly IReadOnlyList<string> QuickReasons =
    [
        "Hàng lỗi / hư hỏng",
        "Hàng cận / hết hạn",
        "Giao sai hàng",
        "Giao thừa số lượng",
        "Hàng bán chậm",
    ];

    private readonly List<SupplierReturnSheetLine> _lines = new();
    private readonly IReadOnlyList<StockItem> _catalog;
    private decimal _manualRefund;

    private SupplierReturnSheet(IEnumerable<StockItem> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog.ToList();
    }

    /// <summary>Return against a completed goods receipt: every line of the receipt, nothing picked yet.</summary>
    public static SupplierReturnSheet FromReceipt(ReturnableReceiptDto receipt, IEnumerable<StockItem> catalog)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var sheet = new SupplierReturnSheet(catalog)
        {
            Receipt = receipt,
            SupplierId = receipt.SupplierId,
            SupplierName = receipt.SupplierName,
            SupplierDebt = receipt.SupplierDebtBalance,
        };
        foreach (var line in receipt.Lines)
        {
            var product = sheet._catalog.FirstOrDefault(p => p.ProductId == line.ProductId);
            sheet._lines.Add(new SupplierReturnSheetLine(
                line.ProductId,
                line.ProductCode,
                product?.Barcode,
                line.ProductName,
                line.Unit,
                line.StockQuantity,
                product?.CostPrice ?? line.LandedUnitCost,
                line.ReceivedQuantity,
                line.ReturnedQuantity,
                line.MaxReturnable,
                line.LandedUnitCost,
                line.LandedUnitCost));
        }

        return sheet;
    }

    /// <summary>Free return (not tied to a receipt): products are scanned or picked from stock.</summary>
    public static SupplierReturnSheet ForSupplier(int supplierId, string supplierName, decimal debtBalance, IEnumerable<StockItem> catalog)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        return new SupplierReturnSheet(catalog)
        {
            SupplierId = supplierId,
            SupplierName = supplierName,
            SupplierDebt = debtBalance,
        };
    }

    /// <summary>Raised whenever a line, the settlement, reason or note changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The receipt this return is made against, or null for a free return.</summary>
    public ReturnableReceiptDto? Receipt { get; private init; }

    public bool IsAgainstReceipt => Receipt is not null;

    public int SupplierId { get; private init; }

    public string SupplierName { get; private init; } = string.Empty;

    /// <summary>What the store owes the supplier before this return.</summary>
    public decimal SupplierDebt { get; private init; }

    public IReadOnlyList<StockItem> Catalog => _catalog;

    public IReadOnlyList<SupplierReturnSheetLine> Lines => _lines;

    public IEnumerable<SupplierReturnSheetLine> SelectedLines => _lines.Where(l => l.IsSelected);

    public int SelectedLineCount => _lines.Count(l => l.IsSelected);

    public decimal TotalQuantity => _lines.Sum(l => l.Quantity);

    /// <summary>Value of the goods sent back (giá trị hàng trả).</summary>
    public decimal Total => _lines.Sum(l => l.Amount);

    /// <summary>Stock value at the current average cost.</summary>
    public decimal CostValue => _lines.Sum(l => l.CostValue);

    /// <summary>Total − cost value: negative when the supplier takes the goods back below our average cost.</summary>
    public decimal CostDifference => Total - CostValue;

    /// <summary>True: offset the supplier's debt first and take only the rest back in money.</summary>
    public bool AutoSettle { get; private set; } = true;

    /// <summary>Money the supplier pays back now.</summary>
    public decimal RefundAmount => AutoSettle ? Math.Max(0, Total - SupplierDebt) : _manualRefund;

    public PaymentMethod RefundMethod { get; private set; } = PaymentMethod.Cash;

    /// <summary>Part of the value deducted from the supplier's debt (cấn trừ công nợ).</summary>
    public decimal DebtOffset => Total - RefundAmount;

    public decimal DebtAfter => SupplierDebt - DebtOffset;

    public string? Reason { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Why the return cannot be posted yet, or null when it can.</summary>
    public string? Blocker
    {
        get
        {
            if (Receipt is { CanReturn: false } receipt)
            {
                return $"Phiếu nhập {receipt.Code} không còn hàng nào có thể trả (đã trả hết hoặc hết tồn kho).";
            }

            if (SelectedLineCount == 0)
            {
                return IsAgainstReceipt ? "Chọn sản phẩm và số lượng trả lại nhà cung cấp." : "Quét mã hoặc tìm sản phẩm cần trả nhà cung cấp.";
            }

            foreach (var line in SelectedLines)
            {
                if (line.Quantity > line.StockQuantity)
                {
                    return $"Không đủ tồn kho để trả \"{line.ProductName}\" (tồn {FormatQty(line.StockQuantity)} {line.Unit}).";
                }

                if (line.Quantity > line.MaxQuantity)
                {
                    return $"\"{line.ProductName}\" chỉ còn có thể trả tối đa {FormatQty(line.MaxQuantity)} {line.Unit}.";
                }

                if (line.MaxUnitPrice is { } max && line.UnitPrice > max)
                {
                    return $"Giá trả của \"{line.ProductName}\" không được cao hơn giá nhập {Vnd.Format(max)}.";
                }
            }

            if (RefundAmount < 0 || RefundAmount > Total)
            {
                return $"Tiền NCC hoàn lại không được vượt quá giá trị hàng trả ({Vnd.Format(Total)}).";
            }

            if (DebtOffset > SupplierDebt)
            {
                return $"Công nợ với \"{SupplierName}\" chỉ còn {Vnd.Format(SupplierDebt)}. "
                    + $"Phần còn lại ({Vnd.Format(DebtOffset - SupplierDebt)}) cần nhận lại bằng tiền.";
            }

            if (Reason is null)
            {
                return "Vui lòng nhập lý do trả hàng.";
            }

            return null;
        }
    }

    public bool CanSubmit => Blocker is null;

    /// <summary>Short summary for the confirmation prompt.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (DebtOffset > 0)
            {
                parts.Add($"cấn trừ công nợ {Vnd.Format(DebtOffset)}");
            }

            if (RefundAmount > 0)
            {
                parts.Add($"nhận lại {Vnd.Format(RefundAmount)} {(RefundMethod == PaymentMethod.Cash ? "tiền mặt" : "chuyển khoản")}");
            }

            var settlement = parts.Count == 0 ? string.Empty : $" – {string.Join(", ", parts)}";
            return $"Trả {SelectedLineCount} sản phẩm cho {SupplierName}, giá trị {Vnd.Format(Total)}{settlement}";
        }
    }

    public static string FormatQty(decimal quantity) => quantity.ToString("#,0.###", Vnd.Culture);

    public SupplierReturnSheetLine? Find(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);

    public SupplierReturnSheetLine LineOf(int productId) =>
        Find(productId) ?? throw new ArgumentOutOfRangeException(nameof(productId), productId, "Sản phẩm không có trong phiếu trả hàng.");

    /// <summary>
    /// Barcode or product code scan (<c>6*mã</c> = 6 units). Against a receipt the product must be on it; a free
    /// return adds the product from stock. The quantity is capped at what can be returned.
    /// </summary>
    public SupplierReturnScanResult Scan(string? input)
    {
        var (quantity, text) = PosCart.ParseScan(input);
        if (text.Length == 0)
        {
            return new SupplierReturnScanResult(SupplierReturnScanOutcome.Empty, null, string.Empty);
        }

        var product = _catalog.FirstOrDefault(p => string.Equals(p.Barcode, text, StringComparison.Ordinal))
            ?? _catalog.FirstOrDefault(p => string.Equals(p.ProductCode, text, StringComparison.OrdinalIgnoreCase));
        var line = product is null
            ? _lines.FirstOrDefault(l => string.Equals(l.Barcode, text, StringComparison.Ordinal)
                || string.Equals(l.ProductCode, text, StringComparison.OrdinalIgnoreCase))
            : Find(product.ProductId);

        if (product is null && line is null)
        {
            return new SupplierReturnScanResult(SupplierReturnScanOutcome.NotFound, null, $"Không tìm thấy sản phẩm có mã \"{text}\".");
        }

        if (line is null && IsAgainstReceipt)
        {
            return new SupplierReturnScanResult(
                SupplierReturnScanOutcome.NotOnReceipt, null, $"\"{product!.ProductName}\" không có trong phiếu nhập {Receipt!.Code}.");
        }

        return Add(line?.ProductId ?? product!.ProductId, quantity);
    }

    /// <summary>Adds <paramref name="quantity"/> of a product (free return: from stock; receipt: a receipt line).</summary>
    public SupplierReturnScanResult Add(int productId, decimal quantity = 1)
    {
        var line = Find(productId);
        var existed = line is { IsSelected: true };
        if (line is null)
        {
            if (IsAgainstReceipt || _catalog.FirstOrDefault(p => p.ProductId == productId) is not { } product)
            {
                return new SupplierReturnScanResult(SupplierReturnScanOutcome.NotOnReceipt, null, "Sản phẩm không thể trả theo phiếu này.");
            }

            line = new SupplierReturnSheetLine(
                product.ProductId,
                product.ProductCode,
                product.Barcode,
                product.ProductName,
                product.Unit,
                product.StockQuantity,
                product.CostPrice,
                receivedQuantity: null,
                returnedBefore: 0,
                maxQuantity: product.StockQuantity,
                defaultUnitPrice: Money.Round(product.CostPrice),
                maxUnitPrice: null);
            if (line.IsExhausted)
            {
                return new SupplierReturnScanResult(
                    SupplierReturnScanOutcome.AtMaximum, null, $"\"{product.ProductName}\" đã hết tồn kho, không thể trả nhà cung cấp.");
            }

            _lines.Add(line);
        }

        if (line.Quantity >= line.MaxQuantity)
        {
            OnChanged();
            return new SupplierReturnScanResult(
                SupplierReturnScanOutcome.AtMaximum,
                line,
                line.IsExhausted
                    ? $"\"{line.ProductName}\" không còn hàng để trả ({line.LimitText.ToLowerInvariant()})."
                    : $"\"{line.ProductName}\" đã chọn tối đa {FormatQty(line.MaxQuantity)} {line.Unit}.");
        }

        SetQuantity(productId, line.Quantity + quantity);
        return new SupplierReturnScanResult(
            existed ? SupplierReturnScanOutcome.Increased : SupplierReturnScanOutcome.Added,
            line,
            $"{line.ProductName}: trả {FormatQty(line.Quantity)} {line.Unit} × {Vnd.Format(line.UnitPrice)}");
    }

    /// <summary>Sets the quantity, rounded like stock quantities and clamped to 0…maximum returnable.</summary>
    public void SetQuantity(int productId, decimal quantity)
    {
        var line = LineOf(productId);
        var rounded = Math.Round(quantity, StockMovement.QuantityDecimals, MidpointRounding.AwayFromZero);
        line.Quantity = Math.Clamp(rounded, 0, line.MaxQuantity);
        OnChanged();
    }

    /// <summary>+1 / −1 for the stepper buttons.</summary>
    public void Step(int productId, int delta) => SetQuantity(productId, LineOf(productId).Quantity + delta);

    /// <summary>Takes everything returnable of the line, or nothing when it is already fully picked.</summary>
    public void ToggleLine(int productId)
    {
        var line = LineOf(productId);
        SetQuantity(productId, line.Quantity >= line.MaxQuantity ? 0 : line.MaxQuantity);
    }

    /// <summary>Picks everything returnable on every line.</summary>
    public void ReturnAll()
    {
        foreach (var line in _lines)
        {
            line.Quantity = line.MaxQuantity;
        }

        OnChanged();
    }

    /// <summary>Removes a free-return line; on a receipt the line stays and its quantity goes back to 0.</summary>
    public bool Remove(int productId)
    {
        if (Find(productId) is not { } line)
        {
            return false;
        }

        if (IsAgainstReceipt)
        {
            line.Quantity = 0;
        }
        else
        {
            _lines.Remove(line);
        }

        OnChanged();
        return true;
    }

    /// <summary>Sets the return price per unit (whole đồng, not above the receipt's landed cost).</summary>
    public Result SetUnitPrice(int productId, decimal unitPrice)
    {
        var line = LineOf(productId);
        if (unitPrice < 0 || unitPrice != Money.Round(unitPrice))
        {
            return Error.Validation("Giá trả phải là số nguyên đồng, không âm.");
        }

        if (line.MaxUnitPrice is { } max && unitPrice > max)
        {
            return Error.Validation($"Giá trả không được cao hơn giá nhập {Vnd.Format(max)}.");
        }

        line.UnitPrice = unitPrice;
        OnChanged();
        return Result.Success();
    }

    /// <summary>Offset the debt first and take only the rest back in money (default).</summary>
    public void SetAutoSettle(bool auto)
    {
        if (!auto)
        {
            _manualRefund = RefundAmount;
        }

        AutoSettle = auto;
        OnChanged();
    }

    /// <summary>Types the amount the supplier pays back now; the rest is deducted from the debt.</summary>
    public void SetRefundAmount(decimal amount)
    {
        _manualRefund = Math.Max(0, Money.Round(amount));
        AutoSettle = false;
        OnChanged();
    }

    /// <summary>The supplier pays back the whole value; the debt is left as it is.</summary>
    public void RefundEverything() => SetRefundAmount(Total);

    public void SetRefundMethod(PaymentMethod method)
    {
        if (method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Chỉ nhận tiền NCC hoàn lại bằng tiền mặt hoặc chuyển khoản.");
        }

        RefundMethod = method;
        OnChanged();
    }

    public void SetReason(string? reason)
    {
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        OnChanged();
    }

    public void SetNote(string? note)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        OnChanged();
    }

    /// <summary>The request for <see cref="ISupplierReturnService.CreateAsync"/>, or the blocker as a validation error.</summary>
    public Result<CreateSupplierReturnRequest> BuildRequest()
    {
        if (Blocker is { } blocker)
        {
            return Error.Validation(blocker);
        }

        var refund = RefundAmount;
        return new CreateSupplierReturnRequest(
            SupplierId,
            SelectedLines
                .Select(l => new SupplierReturnLineInput(l.ProductId, l.Quantity, l.PriceChanged ? l.UnitPrice : null))
                .ToList(),
            Reason!,
            Receipt?.ReceiptId,
            refund,
            refund > 0 ? RefundMethod : null,
            Note);
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
