namespace GroceryPOS.Domain.Sales;

public enum PaymentMethod
{
    /// <summary>Tiền mặt – the only method that can produce change.</summary>
    Cash = 1,

    /// <summary>Chuyển khoản (bank transfer / VietQR).</summary>
    BankTransfer = 2,

    /// <summary>Thẻ ngân hàng (card terminal).</summary>
    Card = 3,
}

public enum SaleStatus
{
    /// <summary>Đang bán – cart is being built at the counter.</summary>
    Draft = 0,

    /// <summary>Hoàn tất – paid; stock and loyalty have been applied.</summary>
    Completed = 1,

    /// <summary>Đã hủy – voided after completion by a manager.</summary>
    Voided = 2,
}

public static class SaleLabels
{
    public static string Of(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.BankTransfer => "Chuyển khoản",
        PaymentMethod.Card => "Thẻ",
        _ => method.ToString(),
    };

    public static string Of(SaleStatus status) => status switch
    {
        SaleStatus.Draft => "Đang bán",
        SaleStatus.Completed => "Hoàn tất",
        SaleStatus.Voided => "Đã hủy",
        _ => status.ToString(),
    };
}
