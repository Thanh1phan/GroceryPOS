using GroceryPOS.Application.Common;
using GroceryPOS.Application.Sales;
using GroceryPOS.Domain.Partners;

namespace GroceryPOS.Application.Settings;

/// <summary>
/// The store-wide settings an owner edits on the <b>Cấu hình</b> screen: store identity printed on documents, receipt
/// layout, loyalty policy and the sales rules that differ from shop to shop. Persisted in the database
/// (<see cref="StoreSettingsCodec"/>) on top of the defaults from appsettings.json, and projected onto the
/// <see cref="StoreProfile"/> and <see cref="SalesOptions"/> the use cases already consume.
/// </summary>
public sealed record StoreSettings
{
    // ---- Cửa hàng -------------------------------------------------------------------------------------
    public string StoreName { get; init; } = StoreProfile.Default.Name;

    public string? Address { get; init; }

    public string? Phone { get; init; }

    public string? TaxCode { get; init; }

    // ---- Hóa đơn --------------------------------------------------------------------------------------
    public string ReceiptFooter { get; init; } = StoreProfile.Default.ReceiptFooter;

    /// <summary>80 or 58 (mm).</summary>
    public int ReceiptPaperWidthMm { get; init; } = 80;

    public bool PreviewReceiptAfterCheckout { get; init; } = true;

    // ---- Tích điểm ------------------------------------------------------------------------------------
    /// <summary>VND spent per earned point; 0 turns earning off.</summary>
    public decimal VndPerPoint { get; init; } = LoyaltyPolicy.Default.VndPerPoint;

    /// <summary>Discount (VND) per redeemed point; 0 turns redeeming off.</summary>
    public decimal PointValueVnd { get; init; } = LoyaltyPolicy.Default.PointValueVnd;

    // ---- Bán hàng -------------------------------------------------------------------------------------
    public int ReturnWindowDays { get; init; } = SalesOptions.Default.ReturnWindowDays;

    public bool AllowNegativeStock { get; init; } = SalesOptions.Default.AllowNegativeStock;

    public bool RequireOpenShift { get; init; } = SalesOptions.Default.RequireOpenShift;

    public decimal ShiftVarianceNoteThreshold { get; init; } = SalesOptions.Default.ShiftVarianceNoteThreshold;

    public bool LoyaltyEarningEnabled => VndPerPoint > 0;

    public bool LoyaltyRedeemingEnabled => PointValueVnd > 0;

    /// <summary>Snapshot of the settings carried by the profile/options the app currently runs with.</summary>
    public static StoreSettings From(StoreProfile store, SalesOptions sales)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sales);
        return new StoreSettings
        {
            StoreName = store.Name,
            Address = store.Address,
            Phone = store.Phone,
            TaxCode = store.TaxCode,
            ReceiptFooter = store.ReceiptFooter,
            ReceiptPaperWidthMm = store.ReceiptPaperWidthMm <= 58 ? 58 : 80,
            PreviewReceiptAfterCheckout = store.PreviewReceiptAfterCheckout,
            VndPerPoint = sales.Loyalty.VndPerPoint,
            PointValueVnd = sales.Loyalty.PointValueVnd,
            ReturnWindowDays = sales.ReturnWindowDays,
            AllowNegativeStock = sales.AllowNegativeStock,
            RequireOpenShift = sales.RequireOpenShift,
            ShiftVarianceNoteThreshold = sales.ShiftVarianceNoteThreshold,
        };
    }

    /// <summary>The store profile with these settings applied (fields the screen does not edit are kept).</summary>
    public StoreProfile ApplyTo(StoreProfile store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store with
        {
            Name = StoreName.Trim(),
            Address = Clean(Address),
            Phone = Clean(Phone),
            TaxCode = Clean(TaxCode),
            ReceiptFooter = NormalizeFooter(ReceiptFooter),
            ReceiptPaperWidthMm = ReceiptPaperWidthMm,
            PreviewReceiptAfterCheckout = PreviewReceiptAfterCheckout,
        };
    }

    /// <summary>The sales options with these settings applied (prefixes, offset… are kept).</summary>
    public SalesOptions ApplyTo(SalesOptions sales)
    {
        ArgumentNullException.ThrowIfNull(sales);
        return sales with
        {
            Loyalty = new LoyaltyPolicy(VndPerPoint, PointValueVnd),
            ReturnWindowDays = ReturnWindowDays,
            AllowNegativeStock = AllowNegativeStock,
            RequireOpenShift = RequireOpenShift,
            ShiftVarianceNoteThreshold = ShiftVarianceNoteThreshold,
        };
    }

    /// <summary>Trimmed text fields, empty optional fields as null, footer line breaks as "\n".</summary>
    public StoreSettings Normalize() => this with
    {
        StoreName = StoreName?.Trim() ?? string.Empty,
        Address = Clean(Address),
        Phone = Clean(Phone),
        TaxCode = Clean(TaxCode),
        ReceiptFooter = NormalizeFooter(ReceiptFooter),
    };

    /// <summary>Footer lines trimmed, CR/LF unified to "\n", trailing empty lines dropped.</summary>
    public static string NormalizeFooter(string? footer)
    {
        if (string.IsNullOrWhiteSpace(footer))
        {
            return string.Empty;
        }

        var lines = footer.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        return string.Join('\n', lines);
    }

    public static int FooterLineCount(string? footer)
    {
        var normalized = NormalizeFooter(footer);
        return normalized.Length == 0 ? 0 : normalized.Split('\n').Length;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One changed setting, with display values, for the confirmation text and the audit entry.</summary>
public sealed record SettingChange(string Key, string Section, string Label, string OldValue, string NewValue)
{
    public override string ToString() => $"{Label}: {OldValue} → {NewValue}";
}

/// <summary>Outcome of saving: the settings now in force and what changed (empty when nothing did).</summary>
public sealed record SettingsSaveResult(StoreSettings Settings, IReadOnlyList<SettingChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}
