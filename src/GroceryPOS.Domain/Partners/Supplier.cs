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

    public void Update(string name, string? contactPerson, string? phone, string? email, string? address, string? taxCode)
    {
        Name = Guard.NotEmpty(name, "supplier.name", "Tên nhà cung cấp không được để trống", 200);
        ContactPerson = Guard.Optional(contactPerson, 100);
        Phone = PhoneNumber.NormalizeOptional(phone);
        Email = Guard.Optional(email, 100);
        Address = Guard.Optional(address);
        TaxCode = Guard.Optional(taxCode, 20);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
