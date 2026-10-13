namespace GroceryPOS.Domain.Shifts;

/// <summary>
/// What happened at the counter during a shift, computed from the sales, voids and returns
/// recorded by the shift's user between opening and closing. Frozen on the shift when it closes.
/// </summary>
/// <param name="SalesCount">Invoices completed during the shift (including ones voided later).</param>
/// <param name="SalesTotal">Σ invoice totals of those invoices.</param>
/// <param name="DiscountTotal">Σ discounts (line + invoice + points) given on those invoices.</param>
/// <param name="CashSales">Cash kept in the drawer from sales: cash received minus change given.</param>
/// <param name="TransferSales">Received by bank transfer.</param>
/// <param name="CardSales">Received by card.</param>
/// <param name="VoidCount">Invoices voided by the shift's user during the shift.</param>
/// <param name="VoidTotal">Σ totals of the voided invoices.</param>
/// <param name="VoidCash">Cash handed back to customers for voided invoices.</param>
/// <param name="ReturnCount">Customer returns taken during the shift.</param>
/// <param name="CashRefunds">Refunded in cash from the drawer.</param>
/// <param name="TransferRefunds">Refunded by bank transfer.</param>
public sealed record ShiftTotals(
    int SalesCount,
    decimal SalesTotal,
    decimal DiscountTotal,
    decimal CashSales,
    decimal TransferSales,
    decimal CardSales,
    int VoidCount,
    decimal VoidTotal,
    decimal VoidCash,
    int ReturnCount,
    decimal CashRefunds,
    decimal TransferRefunds)
{
    public static ShiftTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Doanh thu thuần: sales minus voids and refunds.</summary>
    public decimal NetRevenue => SalesTotal - VoidTotal - CashRefunds - TransferRefunds;

    /// <summary>Effect of counter activity on the drawer (before manual pay-ins/outs).</summary>
    public decimal NetCash => CashSales - VoidCash - CashRefunds;
}
