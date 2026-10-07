using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Catalog;

public sealed class Category : AuditableEntity
{
    private Category()
    {
    }

    public Category(string name, string? description = null)
    {
        Update(name, description);
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? description)
    {
        Name = Guard.NotEmpty(name, "category.name", "Tên danh mục không được để trống", 100);
        Description = Guard.Optional(description);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
