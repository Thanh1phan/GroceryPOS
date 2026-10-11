using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Partners;

public sealed class Supplier : AuditableEntity
{
    private Supplier()
    {
    }

    public Supplier(string code, string name)
    {
        Code = Guard.NotEmpty(code, "supplier.code", "Mã nhà cung cấp không được để trống", 32).ToUpperInvariant();
        Name = Guard.NotEmpty(name, "supplier.name", "Tên nhà cung cấp không được để trống", 200);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactPerson { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Address { get; private set; }
    public string? TaxCode { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Amount the store still owes this supplier (công nợ phải trả), VND.</summary>
    public decimal DebtBalance { get; private set; }

    public void Update(string name, string? contactPerson, string? phone, string? email, string? address, string? taxCode)
    {
        Name = Guard.NotEmpty(name, "supplier.name", "Tên nhà cung cấp không được để trống", 200);
        ContactPerson = Guard.Optional(contactPerson, 100);
        Phone = PhoneNumber.NormalizeOptional(phone);
        Email = Guard.Optional(email, 100);
        Address = Guard.Optional(address);
        TaxCode = Guard.Optional(taxCode, 20);
    }

    /// <summary>Adds the unpaid part of a completed goods receipt to the payable balance.</summary>
    public void AddDebt(decimal amount) =>
        DebtBalance += Guard.NotNegative(amount, "supplier.debt", "Số tiền công nợ không hợp lệ.");

    /// <summary>Records a payment to the supplier (or reverses debt); cannot pay more than is owed.</summary>
    public void PayDebt(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("supplier.payment", "Số tiền thanh toán phải lớn hơn 0.");
        }

        if (amount > DebtBalance)
        {
            throw new DomainException("supplier.payment", $"Số tiền thanh toán vượt quá công nợ hiện tại ({DebtBalance:#,0} ₫).");
        }

        DebtBalance -= amount;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
