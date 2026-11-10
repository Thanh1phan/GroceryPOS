using GroceryPOS.Application.Common;
using GroceryPOS.Application.Formatting;
using GroceryPOS.Domain.Sales;

namespace GroceryPOS.Application.Purchasing;

/// <summary>
/// UI-free state of the "Trả nợ nhà cung cấp" dialog (phiếu chi): amount (pre-filled with the whole debt, never more),
/// cash or bank transfer, reference and note, the remaining debt and the confirmation text.
/// </summary>
public sealed class SupplierPaymentDraft
{
    public SupplierPaymentDraft(SupplierPayableDto supplier)
    {
        Supplier = supplier ?? throw new ArgumentNullException(nameof(supplier));
        Amount = Math.Max(0, supplier.DebtBalance);
    }

    public SupplierPayableDto Supplier { get; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; } = PaymentMethod.Cash;

    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    public decimal Debt => Supplier.DebtBalance;

    public decimal DebtAfter => Debt - Amount;

    public bool SettlesDebt => Amount > 0 && DebtAfter == 0;

    /// <summary>Half of the debt rounded to the nearest 1.000 ₫ (to the đồng for debts under 2.000 ₫), never above the debt.</summary>
    public decimal HalfAmount
    {
        get
        {
            if (Debt <= 0)
            {
                return 0;
            }

            var rounded = Math.Round(Debt / 2 / 1_000, MidpointRounding.AwayFromZero) * 1_000;
            if (rounded <= 0 || Debt < 2_000)
            {
                rounded = Math.Round(Debt / 2, 0, MidpointRounding.AwayFromZero);
            }

            return Math.Min(rounded, Debt);
        }
    }

    public string ReferencePlaceholder => Method == PaymentMethod.BankTransfer ? "Mã giao dịch / số UNC" : "Số chứng từ (không bắt buộc)";

    /// <summary>"Công nợ còn lại: 1.500.000 ₫" or "Sau khi trả: hết nợ nhà cung cấp."</summary>
    public string AfterText => DebtAfter <= 0 ? "Sau khi trả: hết nợ nhà cung cấp." : $"Công nợ còn lại: {Vnd.Format(DebtAfter)}";

    public string? Blocker => Amount <= 0
        ? "Số tiền phải lớn hơn 0."
        : Amount > Debt
            ? $"Không được vượt quá công nợ {Vnd.Format(Math.Max(0, Debt))}."
            : null;

    public bool CanSave => Blocker is null;

    public string ConfirmText =>
        $"Lập phiếu chi {Vnd.Format(Amount)} ({(Method == PaymentMethod.Cash ? "tiền mặt" : "chuyển khoản")}) trả nợ {Supplier.Name}?"
        + (SettlesDebt ? "\nSau phiếu chi này nhà cung cấp hết công nợ." : $"\n{AfterText}");

    public void SetAmount(decimal amount) => Amount = Math.Max(0, Math.Round(amount, 0, MidpointRounding.AwayFromZero));

    public void PayAll() => SetAmount(Debt);

    public void PayHalf() => SetAmount(HalfAmount);

    public void SetMethod(PaymentMethod method)
    {
        if (method is not (PaymentMethod.Cash or PaymentMethod.BankTransfer))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Chỉ trả nợ bằng tiền mặt hoặc chuyển khoản.");
        }

        Method = method;
    }

    public void SetReference(string? reference) => Reference = Clean(reference);

    public void SetNote(string? note) => Note = Clean(note);

    public Result<SupplierPaymentRequest> BuildRequest() =>
        Blocker is { } blocker
            ? Error.Validation(blocker)
            : new SupplierPaymentRequest(Supplier.SupplierId, Amount, Method, Reference, Note);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
