namespace GroceryPOS.Application.Common;

/// <summary>
/// Store identity printed on receipts and documents (tên cửa hàng, địa chỉ, điện thoại, MST, lời cảm ơn).
/// Bound from the <c>Store</c> section of appsettings.json; later editable from the Settings screen.
/// </summary>
public sealed record StoreProfile
{
    public const string SectionName = "Store";

    public static StoreProfile Default { get; } = new();

    public string Name { get; init; } = "Tạp hóa GroceryPOS";

    public string? Address { get; init; }

    public string? Phone { get; init; }

    /// <summary>Mã số thuế, printed when set.</summary>
    public string? TaxCode { get; init; }

    /// <summary>Thank-you lines at the bottom of the receipt; "\n" separates lines.</summary>
    public string ReceiptFooter { get; init; } = "Cảm ơn quý khách và hẹn gặp lại!";

    /// <summary>Receipt paper width in millimetres (80 or 58).</summary>
    public int ReceiptPaperWidthMm { get; init; } = 80;

    /// <summary>Show the print preview automatically after each checkout.</summary>
    public bool PreviewReceiptAfterCheckout { get; init; } = true;
}
