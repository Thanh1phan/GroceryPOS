namespace GroceryPOS.Application.Catalog;

public sealed record CategoryDto(int Id, string Name, string? Description, bool IsActive);

public sealed record SaveCategoryRequest(string Name, string? Description);
